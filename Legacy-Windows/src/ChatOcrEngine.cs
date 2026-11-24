using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace RoMulti
{
    public enum ChatRegionMode
    {
        DualAuto,
        BottomLeftKingdom,
        TopLeftJungle
    }

    public sealed class OcrLineBox
    {
        public double PreviewMinX { get; set; }
        public double PreviewMaxX { get; set; }
        public double PreviewTopY { get; set; }
        public double PreviewBottomY { get; set; }
    }

    public sealed class ChatCaptureResult
    {
        public bool Captured { get; set; }
        public bool LooksBlack { get; set; }
        public byte[] RawCropPng { get; set; }
        public List<string> OcrLines { get; set; }
        public List<OcrLineBox> LineBoxes { get; set; }
        public string DiagnosticNote { get; set; }

        public ChatCaptureResult()
        {
            OcrLines = new List<string>();
            LineBoxes = new List<OcrLineBox>();
        }
    }

    public sealed class RawDualCrop : IDisposable
    {
        public ChatRegionMode RegionMode { get; set; }
        public Bitmap TopCrop { get; set; }
        public Bitmap BottomCrop { get; set; }
        public int TopCropOriginY { get; set; }
        public int BottomCropOriginY { get; set; }
        public int BottomPreviewOffsetY { get; set; }
        public byte[] CombinedPreviewPng { get; set; }
        public int PreviewWidth { get; set; }
        public int PreviewHeight { get; set; }
        public bool LooksBlack { get; set; }
        public string Error { get; set; }

        public void Dispose()
        {
            if (TopCrop != null) { TopCrop.Dispose(); TopCrop = null; }
            if (BottomCrop != null) { BottomCrop.Dispose(); BottomCrop = null; }
        }
    }

    public static class ChatOcrEngine
    {
        private static readonly object EngineLock = new object();
        private static OcrEngine _cachedEngine;
        private static bool _engineInitAttempted;

        public static string GetAvailableOcrLanguage()
        {
            var eng = GetEngine();
            return eng != null ? eng.RecognizerLanguage.LanguageTag : null;
        }

        private static OcrEngine GetEngine()
        {
            lock (EngineLock)
            {
                if (!_engineInitAttempted)
                {
                    _engineInitAttempted = true;
                    try
                    {
                        _cachedEngine = OcrEngine.TryCreateFromUserProfileLanguages();
                        if (_cachedEngine == null && OcrEngine.AvailableRecognizerLanguages.Count > 0)
                        {
                            _cachedEngine = OcrEngine.TryCreateFromLanguage(OcrEngine.AvailableRecognizerLanguages[0]);
                        }
                    }
                    catch
                    {
                        _cachedEngine = null;
                    }
                }
                return _cachedEngine;
            }
        }

        public static void PokeChatHoverBackground(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || NativeMethods.IsIconic(hwnd)) return;
            try
            {
                // Clover Retribution places chat at BOTTOM-LEFT in 1st Kingdom and TOP-LEFT in Jungle.
                // Gently hover both corners in the background without stealing focus.
                NativeMethods.RECT cr;
                int botY = 220;
                if (NativeMethods.GetClientRect(hwnd, out cr))
                {
                    int h = cr.Bottom - cr.Top;
                    if (h > 80) botY = (int)(h * 0.84);
                }

                // Top-left (Jungle)
                NativeMethods.PostMessage(hwnd, NativeMethods.WM_MOUSEMOVE, IntPtr.Zero, (IntPtr)((90 << 16) | (90 & 0xFFFF)));
                // Bottom-left (1st Kingdom)
                NativeMethods.PostMessage(hwnd, NativeMethods.WM_MOUSEMOVE, IntPtr.Zero, (IntPtr)((botY << 16) | (90 & 0xFFFF)));
            }
            catch { }
        }

        public static ChatRegionMode GetRegionModeForPlaceId(string placeId)
        {
            if (string.Equals(placeId, "14839995292", StringComparison.Ordinal))
                return ChatRegionMode.BottomLeftKingdom;
            if (string.Equals(placeId, "140627118796712", StringComparison.Ordinal))
                return ChatRegionMode.TopLeftJungle;
            return ChatRegionMode.DualAuto;
        }

        public static RawDualCrop CaptureRawCropsSync(IntPtr hwnd, string placeId = null)
        {
            var raw = new RawDualCrop();
            if (hwnd == IntPtr.Zero)
            {
                raw.Error = "Window handle is empty (not visible yet).";
                return raw;
            }
            if (NativeMethods.IsIconic(hwnd))
            {
                raw.Error = "Window is minimized.";
                return raw;
            }

            ChatRegionMode mode = GetRegionModeForPlaceId(placeId);
            raw.RegionMode = mode;

            Bitmap topCrop, bottomCrop;
            int topOriginY, bottomOriginY;
            bool looksBlack;
            string capErr;
            if (!CaptureChatCropsForMode(hwnd, mode, out topCrop, out topOriginY, out bottomCrop, out bottomOriginY, out looksBlack, out capErr))
            {
                raw.Error = capErr ?? "PrintWindow capture failed.";
                return raw;
            }

            raw.TopCrop = topCrop;
            raw.BottomCrop = bottomCrop;
            raw.TopCropOriginY = topOriginY;
            raw.BottomCropOriginY = bottomOriginY;
            raw.LooksBlack = looksBlack;

            if (mode == ChatRegionMode.BottomLeftKingdom && bottomCrop != null)
            {
                raw.BottomPreviewOffsetY = 0;
                raw.PreviewWidth = bottomCrop.Width;
                raw.PreviewHeight = bottomCrop.Height;
                raw.CombinedPreviewPng = EncodeBitmapToPng(bottomCrop);
            }
            else if (mode == ChatRegionMode.TopLeftJungle && topCrop != null)
            {
                raw.BottomPreviewOffsetY = 0;
                raw.PreviewWidth = topCrop.Width;
                raw.PreviewHeight = topCrop.Height;
                raw.CombinedPreviewPng = EncodeBitmapToPng(topCrop);
            }
            else if (topCrop != null && bottomCrop != null)
            {
                raw.BottomPreviewOffsetY = topCrop.Height + 4;
                using (var combined = CombineVerticalPreview(topCrop, bottomCrop))
                {
                    raw.PreviewWidth = combined.Width;
                    raw.PreviewHeight = combined.Height;
                    raw.CombinedPreviewPng = EncodeBitmapToPng(combined);
                }
            }
            return raw;
        }

        public static async Task<ChatCaptureResult> RecognizeFromRawCropsAsync(RawDualCrop raw)
        {
            var res = new ChatCaptureResult();
            if (raw == null || (raw.TopCrop == null && raw.BottomCrop == null))
            {
                res.DiagnosticNote = raw != null && !string.IsNullOrEmpty(raw.Error) ? raw.Error : "No frame captured.";
                return res;
            }

            res.Captured = true;
            res.LooksBlack = raw.LooksBlack;
            res.RawCropPng = raw.CombinedPreviewPng;

            if (raw.LooksBlack)
            {
                res.DiagnosticNote = "Captured frame is completely black (window occluded). Enable Smart Auto-Peek to wake & capture automatically.";
                return res;
            }

            var allPositioned = new List<PositionedOcrLine>();

            if (raw.TopCrop != null)
            {
                var topLines = await RecognizePositionedLinesAsync(raw.TopCrop, raw.TopCropOriginY, previewOffsetY: 0, cropTag: "top");
                allPositioned.AddRange(topLines);
            }

            if (raw.BottomCrop != null)
            {
                var bottomLines = await RecognizePositionedLinesAsync(raw.BottomCrop, raw.BottomCropOriginY, previewOffsetY: raw.BottomPreviewOffsetY, cropTag: "bot");
                if (allPositioned.Count == 0)
                    allPositioned.AddRange(bottomLines);
                else
                    MergePositionedLines(allPositioned, bottomLines);
            }

            allPositioned.Sort((a, b) =>
            {
                int cmpY = a.CenterY.CompareTo(b.CenterY);
                return cmpY != 0 ? cmpY : a.MinX.CompareTo(b.MinX);
            });

            var merged = new List<string>(allPositioned.Count);
            var boxes = new List<OcrLineBox>(allPositioned.Count);
            foreach (var p in allPositioned)
            {
                merged.Add(p.Text);
                boxes.Add(new OcrLineBox
                {
                    PreviewMinX = p.MinX,
                    PreviewMaxX = p.MaxX,
                    PreviewTopY = p.PreviewTopY,
                    PreviewBottomY = p.PreviewBottomY
                });
            }

            res.OcrLines = merged;
            res.LineBoxes = boxes;

            string regionLabel = raw.RegionMode == ChatRegionMode.BottomLeftKingdom
                ? "1st Kingdom Chat Box (Bottom-Left ONLY)"
                : (raw.RegionMode == ChatRegionMode.TopLeftJungle
                    ? "Jungle Chat Box (Top-Left ONLY)"
                    : "Dual Chat Corners");
            res.DiagnosticNote = string.Format("Captured {0} [{1}x{2}], OCR read {3} line(s).",
                regionLabel, raw.PreviewWidth, raw.PreviewHeight, res.OcrLines.Count);
            return res;
        }

        /// <summary>
        /// Crops a razor-sharp tight text-box PNG around ONLY the matched event line(s)
        /// (plus clean padding, upscaled 2x when compact) so Discord shows just the exact event text box!
        /// </summary>
        public static byte[] CropEventBoxFromCapture(ChatCaptureResult cap, IList<int> sourceLineIndices)
        {
            if (cap == null || cap.RawCropPng == null || cap.RawCropPng.Length == 0)
                return null;
            if (sourceLineIndices == null || sourceLineIndices.Count == 0 || cap.LineBoxes == null || cap.LineBoxes.Count == 0)
                return cap.RawCropPng;

            double minY = double.MaxValue;
            double maxY = double.MinValue;
            double maxX = 0;
            bool foundAny = false;

            foreach (int idx in sourceLineIndices)
            {
                if (idx < 0 || idx >= cap.LineBoxes.Count) continue;
                var b = cap.LineBoxes[idx];
                if (b.PreviewTopY < minY) minY = b.PreviewTopY;
                if (b.PreviewBottomY > maxY) maxY = b.PreviewBottomY;
                if (b.PreviewMaxX > maxX) maxX = b.PreviewMaxX;
                foundAny = true;
            }

            if (!foundAny || maxY <= minY)
                return cap.RawCropPng;

            try
            {
                using (var ms = new MemoryStream(cap.RawCropPng))
                using (var src = new Bitmap(ms))
                {
                    int padY = 10;
                    int y1 = Math.Max(0, (int)Math.Floor(minY) - padY);
                    int y2 = Math.Min(src.Height, (int)Math.Ceiling(maxY) + padY);
                    int cropH = Math.Max(22, y2 - y1);
                    int cropW = Math.Min(src.Width, Math.Max(260, (int)Math.Ceiling(maxX) + 26));
                    if (cropW > src.Width) cropW = src.Width;
                    if (y1 + cropH > src.Height) cropH = src.Height - y1;
                    if (cropW < 32 || cropH < 16) return cap.RawCropPng;

                    // Upscale 2x when small so the event text box is super crisp & readable on Discord/mobile
                    int outScale = (cropW < 420 && cropH < 120) ? 2 : 1;
                    using (var tight = new Bitmap(cropW * outScale, cropH * outScale, PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(tight))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawImage(src,
                                new Rectangle(0, 0, tight.Width, tight.Height),
                                new Rectangle(0, y1, cropW, cropH),
                                GraphicsUnit.Pixel);
                        }
                        return EncodeBitmapToPng(tight);
                    }
                }
            }
            catch
            {
                return cap.RawCropPng;
            }
        }

        public static async Task<ChatCaptureResult> CaptureAndReadWindowAsync(IntPtr hwnd, bool pokeBackgroundHover = true, string placeId = null)
        {
            if (pokeBackgroundHover)
            {
                PokeChatHoverBackground(hwnd);
            }

            using (var raw = CaptureRawCropsSync(hwnd, placeId))
            {
                try
                {
                    return await RecognizeFromRawCropsAsync(raw);
                }
                catch (Exception ex)
                {
                    return new ChatCaptureResult { DiagnosticNote = "OCR capture error: " + ex.Message };
                }
            }
        }

        public static async Task<List<string>> RecognizeFromImageFileAsync(string imagePath)
        {
            using (var bmp = (Bitmap)Image.FromFile(imagePath))
            {
                return await RecognizeChatLinesAsync(bmp);
            }
        }

        private sealed class PositionedOcrLine
        {
            public double CenterY;
            public double MinX;
            public double MaxX;
            public double LineHeight;
            public double PreviewTopY;
            public double PreviewBottomY;
            public string Text;
            public string SourceTag;
        }

        public static async Task<List<string>> RecognizeChatLinesAsync(Bitmap rawCrop)
        {
            var positioned = await RecognizePositionedLinesAsync(rawCrop, originY: 0, previewOffsetY: 0, cropTag: "crop");
            var merged = new List<string>(positioned.Count);
            foreach (var p in positioned)
                merged.Add(p.Text);
            return merged;
        }

        internal static int ComputeAdaptiveScale(int cropWidth, int cropHeight, int pad)
        {
            if (cropWidth <= 0 || cropHeight <= 0) return 3;
            int desired = (cropWidth < 340 || cropHeight < 170) ? 4 : 3;
            int maxEdge = Math.Max(cropWidth, cropHeight);
            while (desired > 2 && (maxEdge * desired) + (pad * 2) > 2600)
                desired--;
            return desired;
        }

        private static bool HasAnyEventOrPriorityLine(List<PositionedOcrLine> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                string t = lines[i].Text;
                if (string.IsNullOrWhiteSpace(t)) continue;
                if (t.IndexOf("[Event", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.IndexOf("[Even", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.IndexOf("[Leak", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.IndexOf("[Leek", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.IndexOf("Something feels", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.IndexOf("strange energy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.IndexOf("mysterious energy", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static async Task<List<PositionedOcrLine>> RecognizePositionedLinesAsync(Bitmap rawCrop, int originY, int previewOffsetY, string cropTag)
        {
            var engine = GetEngine();
            var positioned = new List<PositionedOcrLine>();
            if (engine == null || rawCrop == null) return positioned;

            const int pad = 24;
            int scale = ComputeAdaptiveScale(rawCrop.Width, rawCrop.Height, pad);

            // Pass 1: Adaptive high-quality color upscale + gentle sharpen (best on dark chat boxes & low-res fonts)
            using (var pass1 = UpscaleColorClean(rawCrop, scale: scale, pad: pad))
            {
                var lines1 = await RunWinRtOcrPositionedAsync(engine, pass1, cropTag + ":p1", scale, pad, originY, previewOffsetY, rawCrop.Width);
                positioned.AddRange(lines1);
            }

            // Pass 2: Stroke-aware local-contrast isolation (isolates outlined Roblox chat text, including blurry low-res
            // gray outlines, while wiping solid orange walls, neon purple floors, and bright white stairs to pure white!)
            using (var pass2 = IsolateOutlinedChatText(rawCrop, scale: scale, pad: pad))
            {
                var lines2 = await RunWinRtOcrPositionedAsync(engine, pass2, cropTag + ":p2", scale, pad, originY, previewOffsetY, rawCrop.Width);
                MergePositionedLines(positioned, lines2);
            }

            // Pass 3 (low-res / bright-sky colored fallback): run when Pass 1+2 read 0 lines, OR on very compact low-res
            // windows when no event/rift line was recovered yet (only replaces a Y-band if OCR quality is strictly higher).
            if (positioned.Count == 0 || (scale >= 4 && !HasAnyEventOrPriorityLine(positioned)))
            {
                using (var pass3 = PreprocessBitmap(rawCrop, scale: scale, pad: pad, mode: PreprocessMode.ColoredEventText))
                {
                    var lines3 = await RunWinRtOcrPositionedAsync(engine, pass3, cropTag + ":p3", scale, pad, originY, previewOffsetY, rawCrop.Width);
                    MergePositionedLines(positioned, lines3);
                }
            }

            // Sort strictly top-to-bottom (oldest message first, newest message last)
            positioned.Sort((a, b) =>
            {
                int cmpY = a.CenterY.CompareTo(b.CenterY);
                return cmpY != 0 ? cmpY : a.MinX.CompareTo(b.MinX);
            });

            return positioned;
        }

        private static void MergePositionedLines(List<PositionedOcrLine> target, List<PositionedOcrLine> incoming)
        {
            foreach (var inc in incoming)
            {
                // Merge ONLY across different passes/crops at the exact same original vertical Y-band
                // (tighter on low-res fonts so closely stacked lines never collapse into one!)
                // and overlapping horizontal X-span. Keeps separate physical lines of identical events intact!
                int matchIdx = -1;
                double bestYDiff = double.MaxValue;
                for (int i = 0; i < target.Count; i++)
                {
                    var ex = target[i];
                    if (ex.SourceTag == inc.SourceTag) continue;

                    double lineH = Math.Max(ex.LineHeight, inc.LineHeight);
                    double yTol = lineH > 0 ? Math.Min(8.5, Math.Max(4.5, lineH * 0.55)) : 8.5;
                    double yDiff = Math.Abs(ex.CenterY - inc.CenterY);
                    if (yDiff > yTol) continue;

                    bool xOverlap = (ex.MinX <= inc.MaxX + 14.0) && (ex.MaxX >= inc.MinX - 14.0);
                    if (!xOverlap) continue;

                    if (yDiff < bestYDiff)
                    {
                        bestYDiff = yDiff;
                        matchIdx = i;
                    }
                }

                if (matchIdx < 0)
                {
                    target.Add(inc);
                }
                else
                {
                    // Keep whichever pass read a cleaner/more complete line at this physical row
                    if (ScoreOcrQuality(inc.Text) > ScoreOcrQuality(target[matchIdx].Text))
                    {
                        target[matchIdx] = inc;
                    }
                }
            }
        }

        private static int ScoreOcrQuality(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            int score = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == ' ')
                    score += 2;
                else if (c >= '0' && c <= '9')
                    score += 1;
                else if (c == '[' || c == ']' || c == ';' || c == ',' || c == '.' || c == '\'' || c == '-' || c == '!' || c == '?')
                    score += 1;
                else
                    score -= 6; // Heavily penalize OCR garbage glyphs (•, », +, ~, etc.)
            }

            // Reward clean readable English-like words and penalize garbled fragments
            string[] tokens = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string tok in tokens)
            {
                string trimmed = tok.Trim('[', ']', '(', ')', '.', ',', ';', ':', '!', '?', '\'', '"', '-');
                if (trimmed.Length == 0) continue;

                bool hasInnerPunct = false;
                bool hasDigit = false;
                bool hasLetter = false;
                int upperInsideWord = 0;
                for (int i = 0; i < trimmed.Length; i++)
                {
                    char c = trimmed[i];
                    if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                    {
                        hasLetter = true;
                        if (i > 0 && c >= 'A' && c <= 'Z') upperInsideWord++;
                    }
                    else if (c >= '0' && c <= '9')
                    {
                        hasDigit = true;
                    }
                    else if (c != '\'' && c != '-')
                    {
                        hasInnerPunct = true;
                    }
                }

                if (hasInnerPunct || (hasDigit && hasLetter) || upperInsideWord >= 2)
                    score -= 10; // Garbled soup like "seen!smnethinp", "2Shccp", "JESeiyeNEvents"
                else if (trimmed.Length == 1 && trimmed != "a" && trimmed != "A" && trimmed != "I" && trimmed != "R" && trimmed != "M" && !(trimmed[0] >= '1' && trimmed[0] <= '7'))
                    score -= 4;  // Stray single-letter noise like "h", "n", "o"
                else if (trimmed.Length >= 3 && hasLetter && !hasDigit)
                    score += 3;
            }
            if (IsExplicitChatTagOrPriority(text))
                score += 6;
            return score;
        }

        private static bool IsExplicitChatTagOrPriority(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return text.IndexOf("[Event", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("[Even", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("Event]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("[Leak", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("[Leek", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("Leak]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("[Tips", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("[System", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("[Clover", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("Something feels", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("strange energy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("mysterious energy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("unlock chat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("to chat click", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Bitmap UpscaleColorClean(Bitmap src, int scale, int pad)
        {
            int dstW = (src.Width * scale) + (pad * 2);
            int dstH = (src.Height * scale) + (pad * 2);
            var work = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (var gw = Graphics.FromImage(work))
                {
                    gw.DrawImage(src, 0, 0, src.Width, src.Height);
                }
                // Sharpen at native pixel resolution BEFORE upscaling: restores 1px low-res
                // font strokes cleanly without amplifying bicubic ringing (and runs ~10x faster).
                SharpenBitmapInPlace(work, scale >= 4 ? 0.24f : 0.16f);

                var dst = new Bitmap(dstW, dstH, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(dst))
                {
                    g.Clear(Color.FromArgb(28, 28, 34));
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(work, pad, pad, src.Width * scale, src.Height * scale);
                }
                return dst;
            }
            finally
            {
                work.Dispose();
            }
        }

        private static void SharpenBitmapInPlace(Bitmap bmp, float amount)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            if (w < 3 || h < 3 || amount <= 0.01f) return;

            var rect = new Rectangle(0, 0, w, h);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                int totalBytes = Math.Abs(stride) * h;
                byte[] src = new byte[totalBytes];
                byte[] dst = new byte[totalBytes];
                Marshal.Copy(data.Scan0, src, 0, totalBytes);
                System.Buffer.BlockCopy(src, 0, dst, 0, totalBytes);

                float centerW = 1.0f + (4.0f * amount);
                for (int y = 1; y < h - 1; y++)
                {
                    int row = y * stride;
                    int up = (y - 1) * stride;
                    int dn = (y + 1) * stride;
                    for (int x = 1; x < w - 1; x++)
                    {
                        int off = x * 4;
                        int idx = row + off;
                        int idxU = up + off;
                        int idxD = dn + off;
                        int idxL = idx - 4;
                        int idxR = idx + 4;

                        for (int ch = 0; ch < 3; ch++)
                        {
                            float val = src[idx + ch] * centerW
                                - (src[idxU + ch] + src[idxD + ch] + src[idxL + ch] + src[idxR + ch]) * amount;
                            int iv = (int)Math.Round(val);
                            if (iv < 0) iv = 0;
                            else if (iv > 255) iv = 255;
                            dst[idx + ch] = (byte)iv;
                        }
                        dst[idx + 3] = 255;
                    }
                }

                Marshal.Copy(dst, 0, data.Scan0, totalBytes);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        public static Bitmap CropTopLeftChat(Bitmap fullBmp)
        {
            int unusedY;
            return CropTopLeftChat(fullBmp, out unusedY);
        }

        public static Bitmap CropTopLeftChat(Bitmap fullBmp, out int cropY)
        {
            int fullW = fullBmp.Width;
            int fullH = fullBmp.Height;

            // Jungle places its taller [Here | Global | Friends] chat window at TOP-LEFT.
            // Capture down to ~54% height (up to 540px on 1080p) so the newest lines at the bottom are NEVER clipped!
            cropY = fullH >= 420 ? 36 : 20;
            int cropW = fullW < 600
                ? Math.Min(fullW, (int)(fullW * 0.68))
                : Math.Min(fullW, Math.Min(600, Math.Max(340, (int)(fullW * 0.48))));
            int cropH = fullH < 420
                ? Math.Min(fullH - cropY, (int)(fullH * 0.68))
                : Math.Min(fullH - cropY, Math.Min(540, Math.Max(260, (int)(fullH * 0.54))));
            if (cropH < 40) { cropY = 0; cropH = fullH; }

            var cropBmp = new Bitmap(cropW, cropH, PixelFormat.Format32bppArgb);
            using (var gc = Graphics.FromImage(cropBmp))
            {
                gc.DrawImage(fullBmp, new Rectangle(0, 0, cropW, cropH), new Rectangle(0, cropY, cropW, cropH), GraphicsUnit.Pixel);
            }
            return cropBmp;
        }

        public static Bitmap CropBottomLeftChat(Bitmap fullBmp)
        {
            int unusedY;
            return CropBottomLeftChat(fullBmp, out unusedY);
        }

        public static Bitmap CropBottomLeftChat(Bitmap fullBmp, out int cropY)
        {
            int fullW = fullBmp.Width;
            int fullH = fullBmp.Height;

            // 1st Kingdom places chat strictly in the BOTTOM-LEFT corner (x ~0-48%, y ~65-100%).
            // Excluding the center-bottom HP/Level HUD avoids confusing OCR line detection.
            int cropW = fullW < 600
                ? Math.Min(fullW, (int)(fullW * 0.64))
                : Math.Min(fullW, Math.Min(580, Math.Max(310, (int)(fullW * 0.48))));
            int cropH = fullH < 420
                ? Math.Min(fullH, (int)(fullH * 0.52))
                : Math.Min(fullH, Math.Min(360, Math.Max(180, (int)(fullH * 0.34))));
            cropY = Math.Max(0, fullH - cropH);

            var cropBmp = new Bitmap(cropW, cropH, PixelFormat.Format32bppArgb);
            using (var gc = Graphics.FromImage(cropBmp))
            {
                gc.DrawImage(fullBmp, new Rectangle(0, 0, cropW, cropH), new Rectangle(0, cropY, cropW, cropH), GraphicsUnit.Pixel);
            }
            return cropBmp;
        }

        private static Bitmap CombineVerticalPreview(Bitmap topCrop, Bitmap bottomCrop)
        {
            int w = Math.Max(topCrop.Width, bottomCrop.Width);
            int divider = 4;
            int h = topCrop.Height + divider + bottomCrop.Height;
            var combined = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(combined))
            {
                g.Clear(Color.FromArgb(20, 24, 34));
                g.DrawImage(topCrop, 0, 0, topCrop.Width, topCrop.Height);
                using (var pen = new SolidBrush(Color.FromArgb(59, 130, 246)))
                {
                    g.FillRectangle(pen, 0, topCrop.Height, w, divider);
                }
                g.DrawImage(bottomCrop, 0, topCrop.Height + divider, bottomCrop.Width, bottomCrop.Height);
            }
            return combined;
        }

        private static bool CaptureChatCropsForMode(
            IntPtr hwnd,
            ChatRegionMode mode,
            out Bitmap topCrop,
            out int topOriginY,
            out Bitmap bottomCrop,
            out int bottomOriginY,
            out bool looksBlack,
            out string error)
        {
            topCrop = null;
            topOriginY = 0;
            bottomCrop = null;
            bottomOriginY = 0;
            looksBlack = false;
            error = null;

            NativeMethods.RECT clientRect;
            if (!NativeMethods.GetClientRect(hwnd, out clientRect))
            {
                error = "GetClientRect failed.";
                return false;
            }

            int fullW = clientRect.Right - clientRect.Left;
            int fullH = clientRect.Bottom - clientRect.Top;
            if (fullW < 64 || fullH < 64)
            {
                error = "Client window area too small (" + fullW + "x" + fullH + ").";
                return false;
            }

            using (var fullBmp = new Bitmap(fullW, fullH, PixelFormat.Format32bppArgb))
            {
                bool ok;
                using (var g = Graphics.FromImage(fullBmp))
                {
                    IntPtr hdc = g.GetHdc();
                    try
                    {
                        ok = NativeMethods.PrintWindow(hwnd, hdc, NativeMethods.PW_CLIENTONLY | NativeMethods.PW_RENDERFULLCONTENT);
                        if (!ok)
                        {
                            ok = NativeMethods.PrintWindow(hwnd, hdc, NativeMethods.PW_RENDERFULLCONTENT);
                        }
                    }
                    finally
                    {
                        g.ReleaseHdc(hdc);
                    }
                }

                if (!ok)
                {
                    error = "PrintWindow returned false (Win32 error " + Marshal.GetLastWin32Error() + ").";
                    return false;
                }

                if (mode == ChatRegionMode.BottomLeftKingdom)
                {
                    bottomCrop = CropBottomLeftChat(fullBmp, out bottomOriginY);
                    looksBlack = CheckIsMostlyBlack(bottomCrop);
                }
                else if (mode == ChatRegionMode.TopLeftJungle)
                {
                    topCrop = CropTopLeftChat(fullBmp, out topOriginY);
                    looksBlack = CheckIsMostlyBlack(topCrop);
                }
                else
                {
                    topCrop = CropTopLeftChat(fullBmp, out topOriginY);
                    bottomCrop = CropBottomLeftChat(fullBmp, out bottomOriginY);
                    looksBlack = CheckIsMostlyBlack(topCrop) && CheckIsMostlyBlack(bottomCrop);
                }
                return true;
            }
        }

        private static bool CheckIsMostlyBlack(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                int totalBytes = Math.Abs(stride) * bmp.Height;
                byte[] buf = new byte[totalBytes];
                Marshal.Copy(data.Scan0, buf, 0, totalBytes);

                int sampled = 0;
                int nonBlack = 0;
                int stepY = Math.Max(1, bmp.Height / 30);
                int stepX = Math.Max(1, bmp.Width / 30);

                for (int y = 0; y < bmp.Height; y += stepY)
                {
                    int row = y * stride;
                    for (int x = 0; x < bmp.Width; x += stepX)
                    {
                        int idx = row + x * 4;
                        byte b = buf[idx];
                        byte g = buf[idx + 1];
                        byte r = buf[idx + 2];
                        sampled++;
                        if (r > 8 || g > 8 || b > 8)
                            nonBlack++;
                    }
                }

                return sampled > 0 && ((double)nonBlack / sampled) < 0.01;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        private enum PreprocessMode
        {
            ColoredEventText,
            BrightText
        }

        private static Bitmap PreprocessBitmap(Bitmap src, int scale, int pad, PreprocessMode mode)
        {
            int dstW = (src.Width * scale) + (pad * 2);
            int dstH = (src.Height * scale) + (pad * 2);
            var dst = new Bitmap(dstW, dstH, PixelFormat.Format32bppArgb);

            using (var g = Graphics.FromImage(dst))
            {
                g.Clear(Color.Black);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, pad, pad, src.Width * scale, src.Height * scale);
            }

            var rect = new Rectangle(0, 0, dstW, dstH);
            BitmapData data = dst.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                int totalBytes = Math.Abs(stride) * dstH;
                byte[] buf = new byte[totalBytes];
                Marshal.Copy(data.Scan0, buf, 0, totalBytes);

                for (int y = 0; y < dstH; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < dstW; x++)
                    {
                        int idx = row + x * 4;
                        byte b = buf[idx];
                        byte g = buf[idx + 1];
                        byte r = buf[idx + 2];

                        byte maxC = r > g ? (r > b ? r : b) : (g > b ? g : b);
                        byte minC = r < g ? (r < b ? r : b) : (g < b ? g : b);
                        int sat = maxC - minC;

                        int outVal;
                        if (mode == PreprocessMode.ColoredEventText)
                        {
                            // Pass 1: Smooth anti-aliased saturation isolation (eliminates bright white/gray sky
                            // while preserving smooth anti-aliased strokes of purple, gold, red, and blue text!)
                            if (sat < 34 || maxC < 85)
                            {
                                outVal = 255;
                            }
                            else
                            {
                                int strength = (int)((sat - 30) * 2.4 + (maxC - 80) * 0.6);
                                if (strength > 255) strength = 255;
                                outVal = 255 - strength;
                            }
                        }
                        else
                        {
                            // Pass 2: Smooth anti-aliased max-channel inversion on dark translucent chat box
                            // (preserves every thin stroke of deep red [Leak]/Blood Wisp, purple, gold, and white text!)
                            int sig = Math.Max((int)maxC, (int)(sat * 1.35));
                            if (sig < 78)
                            {
                                outVal = 255;
                            }
                            else
                            {
                                int strength = (int)((sig - 74) * 2.15);
                                if (strength > 255) strength = 255;
                                outVal = 255 - strength;
                            }
                        }

                        byte bOut = (byte)outVal;
                        buf[idx] = bOut;
                        buf[idx + 1] = bOut;
                        buf[idx + 2] = bOut;
                        buf[idx + 3] = 255;
                    }
                }

                Marshal.Copy(buf, 0, data.Scan0, totalBytes);
            }
            finally
            {
                dst.UnlockBits(data);
            }

            return dst;
        }

        /// <summary>
        /// Stroke-aware local-contrast isolator: Roblox chat text ALWAYS has a crisp 1px dark TextStroke outline
        /// immediately bordering bright/colored letter strokes. Checking for a dark outline pixel (luma &lt;= 55)
        /// in a tiny 5x5 neighborhood wipes uniform orange walls, neon purple floors, and bright white stairs
        /// cleanly to pure white while leaving red [Leak], purple [Event], gold, and blue letters razor-sharp!
        /// </summary>
        private static Bitmap IsolateOutlinedChatText(Bitmap src, int scale, int pad)
        {
            int w = src.Width;
            int h = src.Height;
            var isolated = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            var rect = new Rectangle(0, 0, w, h);
            BitmapData srcData = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData dstData = isolated.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int srcStride = srcData.Stride;
                int dstStride = dstData.Stride;
                byte[] sBuf = new byte[Math.Abs(srcStride) * h];
                byte[] dBuf = new byte[Math.Abs(dstStride) * h];
                Marshal.Copy(srcData.Scan0, sBuf, 0, sBuf.Length);

                byte[] luma = new byte[w * h];
                byte[] maxCh = new byte[w * h];
                byte[] satCh = new byte[w * h];
                for (int y = 0; y < h; y++)
                {
                    int sRow = y * srcStride;
                    int lRow = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        int idx = sRow + x * 4;
                        byte b = sBuf[idx];
                        byte g = sBuf[idx + 1];
                        byte r = sBuf[idx + 2];
                        byte mx = r > g ? (r > b ? r : b) : (g > b ? g : b);
                        byte mn = r < g ? (r < b ? r : b) : (g < b ? g : b);
                        luma[lRow + x] = (byte)((r * 30 + g * 59 + b * 11) / 100);
                        maxCh[lRow + x] = mx;
                        satCh[lRow + x] = (byte)(mx - mn);
                    }
                }

                for (int y = 0; y < h; y++)
                {
                    int dRow = y * dstStride;
                    int lRow = y * w;
                    int yMin = y > 2 ? y - 2 : 0;
                    int yMax = y + 2 < h ? y + 2 : h - 1;

                    for (int x = 0; x < w; x++)
                    {
                        byte pL = luma[lRow + x];
                        byte pM = maxCh[lRow + x];
                        byte pS = satCh[lRow + x];
                        byte minL = 255;
                        byte minS = 255;

                        int xMin = x > 2 ? x - 2 : 0;
                        int xMax = x + 2 < w ? x + 2 : w - 1;
                        for (int ny = yMin; ny <= yMax; ny++)
                        {
                            int nRow = ny * w;
                            for (int nx = xMin; nx <= xMax; nx++)
                            {
                                byte vL = luma[nRow + nx];
                                if (vL < minL) minL = vL;
                                byte vS = satCh[nRow + nx];
                                if (vS < minS) minS = vS;
                            }
                        }

                        int outVal = 255;
                        int peak = pM > pL ? pM : pL;
                        int diffL = pL - minL;
                        int diffM = pM - minL;
                        // Case A1: Crisp dark 1px TextStroke outline (normal/medium res)
                        // Wipes uniform orange walls, neon purple floors, and bright white stairs cleanly to white!
                        if (minL <= 56)
                        {
                            if (diffL >= 32 || (pM >= 76 && diffM >= 36))
                            {
                                int strength = (int)((peak - minL - 24) * 3.15);
                                if (strength < 0) strength = 0;
                                if (strength > 255) strength = 255;
                                outVal = 255 - strength;
                            }
                        }
                        else if (minL <= 74)
                        {
                            // Case A2: Low-resolution anti-aliased gray TextStroke bordering bright/colored strokes
                            if (diffL >= 42 || (pM >= 92 && diffM >= 46 && pS >= 34))
                            {
                                int strength = (int)((peak - minL - 30) * 3.0);
                                if (strength < 0) strength = 0;
                                if (strength > 255) strength = 255;
                                outVal = 255 - strength;
                            }
                        }
                        // Case B: Saturated colored event text (e.g. gold Spirit Rift / purple Devil Rift)
                        // directly over low-saturation bright neutral sky/sand (never matches uniform colored walls!)
                        if (outVal > 60 && pS >= 48 && minS <= 30 && (pS - minS) >= 36)
                        {
                            int strength = (int)((pS - minS - 28) * 2.85);
                            if (strength > 255) strength = 255;
                            int cand = 255 - strength;
                            if (cand < outVal) outVal = cand;
                        }

                        int dIdx = dRow + x * 4;
                        byte bo = (byte)outVal;
                        dBuf[dIdx] = bo;
                        dBuf[dIdx + 1] = bo;
                        dBuf[dIdx + 2] = bo;
                        dBuf[dIdx + 3] = 255;
                    }
                }

                Marshal.Copy(dBuf, 0, dstData.Scan0, dBuf.Length);
            }
            finally
            {
                src.UnlockBits(srcData);
                isolated.UnlockBits(dstData);
            }

            int dstW = (w * scale) + (pad * 2);
            int dstH = (h * scale) + (pad * 2);
            var upscaled = new Bitmap(dstW, dstH, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(upscaled))
            {
                g.Clear(Color.White);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(isolated, pad, pad, w * scale, h * scale);
            }
            isolated.Dispose();
            return upscaled;
        }

        private static byte[] EncodeBitmapToPng(Bitmap bmp)
        {
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        private static async Task<List<PositionedOcrLine>> RunWinRtOcrPositionedAsync(
            OcrEngine engine,
            Bitmap bmp,
            string sourceTag,
            int scale,
            int pad,
            int originY,
            int previewOffsetY,
            int rawCropWidth)
        {
            var lines = new List<PositionedOcrLine>();
            byte[] pngBytes = EncodeBitmapToPng(bmp);

            using (var ras = new InMemoryRandomAccessStream())
            {
                using (var writer = new DataWriter(ras.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(pngBytes);
                    await AwaitOp(writer.StoreAsync());
                    await AwaitOp(writer.FlushAsync());
                    writer.DetachStream();
                }

                ras.Seek(0);
                var decoder = await AwaitOp(BitmapDecoder.CreateAsync(ras));
                using (var sbmp = await AwaitOp(decoder.GetSoftwareBitmapAsync()))
                {
                    var ocrResult = await AwaitOp(engine.RecognizeAsync(sbmp));
                    if (ocrResult != null && ocrResult.Lines != null)
                    {
                        double maxChatLeftMargin = Math.Max(44.0, rawCropWidth * 0.20);

                        foreach (var line in ocrResult.Lines)
                        {
                            if (string.IsNullOrWhiteSpace(line.Text)) continue;

                            string finalText = line.Text.Trim();
                            double centerY = originY;
                            double minX = 0;
                            double maxX = 0;
                            double lineHeight = 14.0;
                            double previewTopY = previewOffsetY;
                            double previewBottomY = previewOffsetY + 14;

                            if (line.Words != null && line.Words.Count > 0)
                            {
                                // Sort words left-to-right and slice off any distant right-side HUD element
                                // (like "4900 $", "Level 83", "...") separated by a wide horizontal gap.
                                var sortedWords = new List<OcrWord>(line.Words.Count);
                                foreach (var w in line.Words)
                                    sortedWords.Add(w);
                                sortedWords.Sort((a, b) => a.BoundingRect.X.CompareTo(b.BoundingRect.X));

                                double maxWordGap = Math.Max(36.0, rawCropWidth * 0.09);
                                var keptWords = new List<OcrWord>();
                                double prevRightOrig = 0;
                                for (int wi = 0; wi < sortedWords.Count; wi++)
                                {
                                    var w = sortedWords[wi];
                                    double leftOrig = (w.BoundingRect.X - pad) / (double)scale;
                                    double rightOrig = (w.BoundingRect.X + w.BoundingRect.Width - pad) / (double)scale;
                                    if (wi > 0 && (leftOrig - prevRightOrig) > maxWordGap)
                                        break;
                                    keptWords.Add(w);
                                    prevRightOrig = rightOrig;
                                }

                                if (keptWords.Count == 0) continue;

                                double sumY = 0;
                                double sumH = 0;
                                minX = double.MaxValue;
                                maxX = double.MinValue;
                                double minScaledY = double.MaxValue;
                                double maxScaledY = double.MinValue;
                                var wordTexts = new List<string>(keptWords.Count);
                                foreach (var w in keptWords)
                                {
                                    sumY += w.BoundingRect.Y + (w.BoundingRect.Height * 0.5);
                                    sumH += w.BoundingRect.Height;
                                    if (w.BoundingRect.X < minX) minX = w.BoundingRect.X;
                                    double rx = w.BoundingRect.X + w.BoundingRect.Width;
                                    if (rx > maxX) maxX = rx;
                                    if (w.BoundingRect.Y < minScaledY) minScaledY = w.BoundingRect.Y;
                                    double by = w.BoundingRect.Y + w.BoundingRect.Height;
                                    if (by > maxScaledY) maxScaledY = by;
                                    wordTexts.Add(w.Text);
                                }

                                double avgScaledY = sumY / keptWords.Count;
                                centerY = originY + ((avgScaledY - pad) / (double)scale);
                                lineHeight = Math.Max(6.0, (sumH / keptWords.Count) / (double)scale);
                                minX = (minX - pad) / (double)scale;
                                maxX = (maxX - pad) / (double)scale;
                                previewTopY = previewOffsetY + ((minScaledY - pad) / (double)scale);
                                previewBottomY = previewOffsetY + ((maxScaledY - pad) / (double)scale);
                                finalText = string.Join(" ", wordTexts).Trim();
                            }

                            // Chat lines ALWAYS start near the left edge of the chat box (<= 20% crop width).
                            // Drop right-shifted HUD/hotbar text ("Cooking Sheep", "4900 $", "Dark Magic", "Level 83", "...").
                            if (rawCropWidth > 0 && minX > maxChatLeftMargin && !IsExplicitChatTagOrPriority(finalText))
                            {
                                continue;
                            }

                            if (!string.IsNullOrWhiteSpace(finalText))
                            {
                                lines.Add(new PositionedOcrLine
                                {
                                    CenterY = centerY,
                                    MinX = minX,
                                    MaxX = maxX,
                                    LineHeight = lineHeight,
                                    PreviewTopY = previewTopY,
                                    PreviewBottomY = previewBottomY,
                                    Text = finalText,
                                    SourceTag = sourceTag
                                });
                            }
                        }
                    }
                }
            }

            return lines;
        }

        private static Task<T> AwaitOp<T>(IAsyncOperation<T> op)
        {
            if (op.Status == AsyncStatus.Completed)
                return Task.FromResult(op.GetResults());

            var tcs = new TaskCompletionSource<T>();
            op.Completed = (info, status) =>
            {
                if (status == AsyncStatus.Completed)
                    tcs.TrySetResult(info.GetResults());
                else if (status == AsyncStatus.Canceled)
                    tcs.TrySetCanceled();
                else
                    tcs.TrySetException(info.ErrorCode ?? new Exception("WinRT async operation failed."));
            };
            return tcs.Task;
        }
    }
}

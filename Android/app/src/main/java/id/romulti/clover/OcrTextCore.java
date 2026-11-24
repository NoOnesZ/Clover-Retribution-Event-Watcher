package id.romulti.clover;

import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;
import java.util.Locale;

/**
 * Pure-Java low-resolution OCR preprocessing, stroke isolation, HUD exclusion,
 * and multi-pass Y-band quality merging. Kept Android-free so desktop JVM checks
 * exercise the exact same pixel and line-merging pipeline as the Android APK.
 */
final class OcrTextCore {
    static final int PAD_PX = 24;
    static final int MAX_UPSCALED_EDGE = 2200;

    static final class RawWord {
        final int left;
        final int top;
        final int right;
        final int bottom;
        final String text;

        RawWord(int left, int top, int right, int bottom, String text) {
            this.left = left;
            this.top = top;
            this.right = right;
            this.bottom = bottom;
            this.text = text == null ? "" : text;
        }
    }

    static final class RawLine {
        final int left;
        final int top;
        final int right;
        final int bottom;
        final String text;
        final List<RawWord> words;

        RawLine(int left, int top, int right, int bottom, String text, List<RawWord> words) {
            this.left = left;
            this.top = top;
            this.right = right;
            this.bottom = bottom;
            this.text = text == null ? "" : text;
            this.words = words == null ? Collections.<RawWord>emptyList() : words;
        }
    }

    static final class PositionedLine {
        final double centerY;
        final double minX;
        final double maxX;
        final double lineHeight;
        final String text;
        final String passTag;

        PositionedLine(double centerY, double minX, double maxX, double lineHeight, String text, String passTag) {
            this.centerY = centerY;
            this.minX = minX;
            this.maxX = maxX;
            this.lineHeight = lineHeight;
            this.text = text;
            this.passTag = passTag;
        }
    }

    static int computeUpscaleFactor(int cropWidth, int cropHeight) {
        if (cropWidth <= 0 || cropHeight <= 0) return 1;
        int desired;
        if (cropWidth < 460 || cropHeight < 180) {
            desired = 4;
        } else if (cropWidth < 920 || cropHeight < 340) {
            desired = 3;
        } else if (cropWidth < 1350) {
            desired = 2;
        } else {
            desired = 1;
        }
        int maxEdge = Math.max(cropWidth, cropHeight);
        while (desired > 1 && (maxEdge * desired) + (PAD_PX * 2) > MAX_UPSCALED_EDGE) {
            desired--;
        }
        return Math.max(1, desired);
    }

    /**
     * Stroke-aware local-contrast isolator tuned for Roblox chat text (including
     * low-resolution screens where the 1px dark TextStroke outline softens to dark gray).
     * Wipes uniform 3D walls, floors, and skies cleanly to pure white while keeping
     * outlined/colored broadcast letters sharp dark ink.
     */
    static int[] isolateOutlinedChatPixels(int[] srcArgb, int w, int h) {
        int count = w * h;
        int[] out = new int[count];
        int[] luma = new int[count];
        int[] maxCh = new int[count];
        int[] satCh = new int[count];

        for (int i = 0; i < count; i++) {
            int px = srcArgb[i];
            int r = (px >>> 16) & 0xFF;
            int g = (px >>> 8) & 0xFF;
            int b = px & 0xFF;
            int mx = Math.max(r, Math.max(g, b));
            int mn = Math.min(r, Math.min(g, b));
            luma[i] = (r * 30 + g * 59 + b * 11) / 100;
            maxCh[i] = mx;
            satCh[i] = mx - mn;
        }

        for (int y = 0; y < h; y++) {
            int row = y * w;
            int yMin = Math.max(0, y - 2);
            int yMax = Math.min(h - 1, y + 2);
            for (int x = 0; x < w; x++) {
                int idx = row + x;
                int pL = luma[idx];
                int pM = maxCh[idx];
                int pS = satCh[idx];
                int minL = 255;
                int minS = 255;

                int xMin = Math.max(0, x - 2);
                int xMax = Math.min(w - 1, x + 2);
                for (int ny = yMin; ny <= yMax; ny++) {
                    int nRow = ny * w;
                    for (int nx = xMin; nx <= xMax; nx++) {
                        int nIdx = nRow + nx;
                        int vL = luma[nIdx];
                        if (vL < minL) minL = vL;
                        int vS = satCh[nIdx];
                        if (vS < minS) minS = vS;
                    }
                }

                int outVal = 255;
                int peak = Math.max(pM, pL);
                int diffL = pL - minL;
                int diffM = pM - minL;

                // Case A1: Crisp dark 1px TextStroke outline (standard & medium res)
                if (minL <= 56) {
                    if (diffL >= 32 || (pM >= 76 && diffM >= 36)) {
                        int strength = (int) ((peak - minL - 24) * 3.15);
                        strength = Math.max(0, Math.min(255, strength));
                        outVal = 255 - strength;
                    }
                } else if (minL <= 74) {
                    // Case A2: Low-resolution anti-aliased gray outline bordering bright/colored strokes
                    if (diffL >= 42 || (pM >= 92 && diffM >= 46 && pS >= 34)) {
                        int strength = (int) ((peak - minL - 30) * 3.0);
                        strength = Math.max(0, Math.min(255, strength));
                        outVal = 255 - strength;
                    }
                }

                // Case B: Saturated colored event text over low-saturation bright sky/sand
                if (outVal > 60 && pS >= 48 && minS <= 30 && (pS - minS) >= 36) {
                    int strength = (int) ((pS - minS - 28) * 2.85);
                    strength = Math.max(0, Math.min(255, strength));
                    int cand = 255 - strength;
                    if (cand < outVal) outVal = cand;
                }

                out[idx] = 0xFF000000 | (outVal << 16) | (outVal << 8) | outVal;
            }
        }
        return out;
    }

    /**
     * Dedicated outlined-purple isolator for Priority #1 Devil Rift ("[Event] Something feels off...").
     * Eliminates surrounding cyan [Tips], green [System], orange [Raid], white player chat, and
     * uniform neon purple floors (via 5x5 dark TextStroke check). Returns null when no outlined
     * purple text is present so normal frames incur zero extra ML Kit overhead.
     */
    static int[] isolatePurpleEventPixels(int[] srcArgb, int w, int h) {
        int count = w * h;
        int[] luma = new int[count];
        int[] purpleSig = new int[count];
        int candidates = 0;

        for (int i = 0; i < count; i++) {
            int px = srcArgb[i];
            int r = (px >>> 16) & 0xFF;
            int g = (px >>> 8) & 0xFF;
            int b = px & 0xFF;
            luma[i] = (r * 30 + g * 59 + b * 11) / 100;
            int sig = Math.min(r - g, b - g);
            if (sig >= 18 && b >= 95 && r >= 70) {
                purpleSig[i] = sig;
                candidates++;
            }
        }
        if (candidates < 25) return null;

        int[] out = new int[count];
        int outlined = 0;
        for (int y = 0; y < h; y++) {
            int row = y * w;
            int yMin = Math.max(0, y - 2);
            int yMax = Math.min(h - 1, y + 2);
            for (int x = 0; x < w; x++) {
                int idx = row + x;
                int outVal = 255;
                int sig = purpleSig[idx];
                if (sig >= 18) {
                    int minL = 255;
                    int xMin = Math.max(0, x - 2);
                    int xMax = Math.min(w - 1, x + 2);
                    for (int ny = yMin; ny <= yMax; ny++) {
                        int nRow = ny * w;
                        for (int nx = xMin; nx <= xMax; nx++) {
                            int vL = luma[nRow + nx];
                            if (vL < minL) minL = vL;
                        }
                    }
                    if (minL <= 84) {
                        int str = Math.min(255, (int) ((sig - 15) * 3.3));
                        outVal = 255 - str;
                        if (outVal < 180) outlined++;
                    }
                }
                out[idx] = 0xFF000000 | (outVal << 16) | (outVal << 8) | outVal;
            }
        }
        return outlined >= 20 ? out : null;
    }

    /**
     * Dedicated outlined priority-accent isolator for Yellow/Gold (Spirit Rift, Light Wisp),
     * Green/Mint (Wind Wisp), and Red (Blood Wisp, Leak Event). Wipes cyan [Tips], purple lines
     * (e.g. Jungle Mammoth directly above Yellow Spirit Rift), white chat, and uniform walls cleanly
     * to pure white. Returns null when fewer than 20 outlined accent pixels exist so idle frames
     * incur zero extra ML Kit overhead.
     */
    static int[] isolatePriorityAccentPixels(int[] srcArgb, int w, int h) {
        int count = w * h;
        int[] luma = new int[count];
        int[] accentSig = new int[count];
        int candidates = 0;

        for (int i = 0; i < count; i++) {
            int px = srcArgb[i];
            int r = (px >>> 16) & 0xFF;
            int g = (px >>> 8) & 0xFF;
            int b = px & 0xFF;
            luma[i] = (r * 30 + g * 59 + b * 11) / 100;

            int sig = 0;
            // Yellow / Gold (Spirit Rift, Light Wisp)
            if (r >= 155 && g >= 140 && b <= 145) {
                int ySig = Math.min(r - b, g - b);
                if (ySig >= 34 && ySig > sig) sig = ySig;
            }
            // Green / Mint-Green (Wind Wisp)
            if (g >= 130 && (g - r) >= 20 && (g - b) >= 8) {
                int gSig = ((g - r) + (g - b)) / 2;
                if (gSig >= 20 && gSig > sig) sig = gSig;
            }
            // Red (Blood Wisp, Leak Event)
            if (r >= 150 && (r - g) >= 38 && (r - b) >= 38) {
                int rSig = Math.min(r - g, r - b);
                if (rSig >= 38 && rSig > sig) sig = rSig;
            }

            if (sig > 0) {
                accentSig[i] = sig;
                candidates++;
            }
        }
        if (candidates < 25) return null;

        int[] out = new int[count];
        int outlined = 0;
        for (int y = 0; y < h; y++) {
            int row = y * w;
            int yMin = Math.max(0, y - 2);
            int yMax = Math.min(h - 1, y + 2);
            for (int x = 0; x < w; x++) {
                int idx = row + x;
                int outVal = 255;
                int sig = accentSig[idx];
                if (sig > 0) {
                    int minL = 255;
                    int xMin = Math.max(0, x - 2);
                    int xMax = Math.min(w - 1, x + 2);
                    for (int ny = yMin; ny <= yMax; ny++) {
                        int nRow = ny * w;
                        for (int nx = xMin; nx <= xMax; nx++) {
                            int vL = luma[nRow + nx];
                            if (vL < minL) minL = vL;
                        }
                    }
                    if (minL <= 84) {
                        int str = Math.max(0, Math.min(255, (int) ((sig - 16) * 3.2)));
                        outVal = 255 - str;
                        if (outVal < 180) outlined++;
                    }
                }
                out[idx] = 0xFF000000 | (outVal << 16) | (outVal << 8) | outVal;
            }
        }
        return outlined >= 20 ? out : null;
    }

    /**
     * Gentle 3x3 unsharp mask to restore crisp letter edges after low-res upscaling.
     */
    static int[] sharpenPixels(int[] srcArgb, int w, int h, float amount) {
        if (w < 3 || h < 3 || amount <= 0.01f) return srcArgb;
        int[] out = new int[w * h];
        System.arraycopy(srcArgb, 0, out, 0, srcArgb.length);
        float centerWeight = 1.0f + (4.0f * amount);
        for (int y = 1; y < h - 1; y++) {
            int row = y * w;
            for (int x = 1; x < w - 1; x++) {
                int idx = row + x;
                int c = srcArgb[idx];
                int u = srcArgb[idx - w];
                int d = srcArgb[idx + w];
                int l = srcArgb[idx - 1];
                int r = srcArgb[idx + 1];

                int cr = (c >>> 16) & 0xFF;
                int cg = (c >>> 8) & 0xFF;
                int cb = c & 0xFF;

                int nr = ((u >>> 16) & 0xFF) + ((d >>> 16) & 0xFF) + ((l >>> 16) & 0xFF) + ((r >>> 16) & 0xFF);
                int ng = ((u >>> 8) & 0xFF) + ((d >>> 8) & 0xFF) + ((l >>> 8) & 0xFF) + ((r >>> 8) & 0xFF);
                int nb = (u & 0xFF) + (d & 0xFF) + (l & 0xFF) + (r & 0xFF);

                int or = clampByte(Math.round(cr * centerWeight - nr * amount));
                int og = clampByte(Math.round(cg * centerWeight - ng * amount));
                int ob = clampByte(Math.round(cb * centerWeight - nb * amount));
                out[idx] = 0xFF000000 | (or << 16) | (og << 8) | ob;
            }
        }
        return out;
    }

    private static int clampByte(int value) {
        if (value < 0) return 0;
        if (value > 255) return 255;
        return value;
    }

    static List<PositionedLine> filterAndNormalizeLines(
            List<RawLine> rawLines, String passTag, int scale, int pad, int rawCropWidth) {
        return filterAndNormalizeLines(rawLines, passTag, scale, pad, rawCropWidth, 0);
    }

    static List<PositionedLine> filterAndNormalizeLines(
            List<RawLine> rawLines, String passTag, int scale, int pad, int rawCropWidth, int originY) {
        List<PositionedLine> result = new ArrayList<>();
        if (rawLines == null || rawLines.isEmpty()) return result;
        double invScale = 1.0 / Math.max(1, scale);
        double maxChatLeftMargin = Math.max(56.0, rawCropWidth * 0.24);

        for (RawLine line : rawLines) {
            if (line == null || line.text.trim().isEmpty()) continue;

            String finalText = line.text.trim();
            double minX = (line.left - pad) * invScale;
            double maxX = (line.right - pad) * invScale;
            double centerY = (((line.top + line.bottom) * 0.5) - pad) * invScale;
            double lineHeight = Math.max(6.0, (line.bottom - line.top) * invScale);

            if (!line.words.isEmpty()) {
                List<RawWord> sorted = new ArrayList<>(line.words);
                Collections.sort(sorted, Comparator.comparingInt(w -> w.left));

                double maxGap = Math.max(36.0, Math.max(lineHeight * 2.6, rawCropWidth * 0.09));
                List<RawWord> kept = new ArrayList<>(sorted.size());
                double prevRightOrig = 0.0;
                for (int i = 0; i < sorted.size(); i++) {
                    RawWord w = sorted.get(i);
                    if (w.text.trim().isEmpty()) continue;
                    double leftOrig = (w.left - pad) * invScale;
                    double rightOrig = (w.right - pad) * invScale;
                    if (!kept.isEmpty() && (leftOrig - prevRightOrig) > maxGap) {
                        break;
                    }
                    kept.add(w);
                    prevRightOrig = rightOrig;
                }
                if (kept.isEmpty()) continue;

                double sumY = 0.0;
                double sumH = 0.0;
                double wMinX = Double.MAX_VALUE;
                double wMaxX = -Double.MAX_VALUE;
                StringBuilder sb = new StringBuilder();
                for (RawWord w : kept) {
                    if (sb.length() > 0) sb.append(' ');
                    sb.append(w.text.trim());
                    sumY += ((w.top + w.bottom) * 0.5 - pad) * invScale;
                    sumH += Math.max(4.0, (w.bottom - w.top) * invScale);
                    double lo = (w.left - pad) * invScale;
                    double ro = (w.right - pad) * invScale;
                    if (lo < wMinX) wMinX = lo;
                    if (ro > wMaxX) wMaxX = ro;
                }
                finalText = sb.toString().trim();
                centerY = sumY / kept.size();
                lineHeight = Math.max(6.0, sumH / kept.size());
                minX = wMinX;
                maxX = wMaxX;
            }

            if (rawCropWidth > 0 && minX > maxChatLeftMargin && !isExplicitChatStart(finalText)) {
                continue;
            }
            if (!finalText.isEmpty()) {
                result.add(new PositionedLine(originY + centerY, minX, maxX, lineHeight, finalText, passTag));
            }
        }
        return result;
    }

    static List<PositionedLine> mergePasses(List<PositionedLine> pass1, List<PositionedLine> pass2) {
        List<PositionedLine> merged = new ArrayList<>();
        if (pass1 != null) merged.addAll(pass1);
        if (pass2 != null) {
            for (PositionedLine inc : pass2) {
                int matchIdx = -1;
                double bestYDiff = Double.MAX_VALUE;
                for (int i = 0; i < merged.size(); i++) {
                    PositionedLine ex = merged.get(i);
                    if (ex.passTag.equals(inc.passTag)) continue;
                    double lineH = Math.max(ex.lineHeight, inc.lineHeight);
                    double yTol = Math.min(10.0, Math.max(4.5, lineH * 0.60));
                    double yDiff = Math.abs(ex.centerY - inc.centerY);
                    if (yDiff > yTol) continue;
                    boolean xOverlap = (ex.minX <= inc.maxX + 16.0) && (ex.maxX >= inc.minX - 16.0);
                    if (!xOverlap) continue;
                    if (yDiff < bestYDiff) {
                        bestYDiff = yDiff;
                        matchIdx = i;
                    }
                }
                if (matchIdx < 0) {
                    merged.add(inc);
                } else if (scoreOcrQuality(inc.text) > scoreOcrQuality(merged.get(matchIdx).text)) {
                    merged.set(matchIdx, inc);
                }
            }
        }
        Collections.sort(merged, (a, b) -> {
            int cmpY = Double.compare(a.centerY, b.centerY);
            return cmpY != 0 ? cmpY : Double.compare(a.minX, b.minX);
        });
        return merged;
    }

    static List<String> toLineTexts(List<PositionedLine> positioned) {
        List<String> texts = new ArrayList<>(positioned.size());
        for (PositionedLine line : positioned) {
            texts.add(line.text);
        }
        return texts;
    }

    static List<String> toLineTextsWithGapBreaks(List<PositionedLine> positioned) {
        List<String> texts = new ArrayList<>(positioned.size() + 1);
        PositionedLine prev = null;
        for (PositionedLine line : positioned) {
            if (prev != null) {
                double maxH = Math.max(prev.lineHeight, line.lineHeight);
                double gapThreshold = Math.max(90.0, maxH * 4.5);
                if (line.centerY - prev.centerY > gapThreshold) {
                    texts.add("");
                }
            }
            texts.add(line.text);
            prev = line;
        }
        return texts;
    }

    static int scoreOcrQuality(String text) {
        if (text == null || text.trim().isEmpty()) return 0;
        int score = 0;
        for (int i = 0; i < text.length(); i++) {
            char c = text.charAt(i);
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == ' ') {
                score += 2;
            } else if (c >= '0' && c <= '9') {
                score += 1;
            } else if (c == '[' || c == ']' || c == ';' || c == ',' || c == '.' || c == '\''
                    || c == '-' || c == '!' || c == '?' || c == ':') {
                score += 1;
            } else {
                score -= 6;
            }
        }

        String[] tokens = text.trim().split("\\s+");
        for (String tok : tokens) {
            String trimmed = trimPunct(tok);
            if (trimmed.isEmpty()) continue;

            boolean hasInnerPunct = false;
            boolean hasDigit = false;
            boolean hasLetter = false;
            int upperInsideWord = 0;
            for (int i = 0; i < trimmed.length(); i++) {
                char c = trimmed.charAt(i);
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) {
                    hasLetter = true;
                    if (i > 0 && c >= 'A' && c <= 'Z') upperInsideWord++;
                } else if (c >= '0' && c <= '9') {
                    hasDigit = true;
                } else if (c != '\'' && c != '-') {
                    hasInnerPunct = true;
                }
            }

            if (hasInnerPunct || (hasDigit && hasLetter) || upperInsideWord >= 2) {
                score -= 10;
            } else if (trimmed.length() == 1 && !isAllowedSingleChar(trimmed.charAt(0))) {
                score -= 4;
            } else if (trimmed.length() >= 3 && hasLetter && !hasDigit) {
                score += 3;
            }
        }
        if (isExplicitChatStart(text)) score += 6;
        return score;
    }

    private static boolean isAllowedSingleChar(char c) {
        return c == 'a' || c == 'A' || c == 'I' || c == 'R' || c == 'M'
            || (c >= '1' && c <= '7');
    }

    private static String trimPunct(String tok) {
        int start = 0;
        int end = tok.length();
        while (start < end && isTrimPunct(tok.charAt(start))) start++;
        while (end > start && isTrimPunct(tok.charAt(end - 1))) end--;
        return tok.substring(start, end);
    }

    private static boolean isTrimPunct(char c) {
        return c == '[' || c == ']' || c == '(' || c == ')' || c == '.' || c == ','
            || c == ';' || c == ':' || c == '!' || c == '?' || c == '\'' || c == '"' || c == '-';
    }

    private static boolean isExplicitChatStart(String text) {
        String lower = text.toLowerCase(Locale.ROOT);
        return lower.contains("[event") || lower.contains("[even") || lower.contains("event]")
            || lower.contains("[leak") || lower.contains("[leek") || lower.contains("leak]")
            || lower.contains("[tips") || lower.contains("[system") || lower.contains("[clover")
            || lower.contains("[raid")
            || lower.contains("something feels") || lower.contains("strange energy")
            || lower.contains("mysterious energy")
            || lower.contains("wind wisp") || lower.contains("blood wisp") || lower.contains("light wisp")
            || lower.contains("unlock chat") || lower.contains("to chat click");
    }

    private OcrTextCore() {}
}

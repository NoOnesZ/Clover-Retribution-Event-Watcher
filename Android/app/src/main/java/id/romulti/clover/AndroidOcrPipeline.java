package id.romulti.clover;

import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.Rect;

import com.google.mlkit.vision.text.Text;

import java.util.ArrayList;
import java.util.List;

/**
 * Android Bitmap + ML Kit bridge for the low-resolution multi-pass chat OCR pipeline.
 */
final class AndroidOcrPipeline {
    static final class BandPasses {
        final Bitmap cleanPass;
        final Bitmap isolatedPass;
        final Bitmap purplePass;
        final Bitmap accentPass;
        final int scale;
        final int purpleScale;
        final int accentScale;
        final int pad;
        final int rawCropWidth;
        final int originY;
        final String bandTag;

        BandPasses(Bitmap cleanPass, Bitmap isolatedPass, Bitmap purplePass, Bitmap accentPass,
                int scale, int purpleScale, int accentScale, int pad, int rawCropWidth,
                int originY, String bandTag) {
            this.cleanPass = cleanPass;
            this.isolatedPass = isolatedPass;
            this.purplePass = purplePass;
            this.accentPass = accentPass;
            this.scale = scale;
            this.purpleScale = purpleScale;
            this.accentScale = accentScale;
            this.pad = pad;
            this.rawCropWidth = rawCropWidth;
            this.originY = originY;
            this.bandTag = bandTag;
        }

        void recycle() {
            if (cleanPass != null && !cleanPass.isRecycled()) cleanPass.recycle();
            if (isolatedPass != null && !isolatedPass.isRecycled()) isolatedPass.recycle();
            if (purplePass != null && !purplePass.isRecycled()) purplePass.recycle();
            if (accentPass != null && !accentPass.isRecycled()) accentPass.recycle();
        }
    }

    static BandPasses prepareBandPasses(Bitmap frame, int frameWidth, int frameHeight, boolean top) {
        int y = top ? 0 : (int) (frameHeight * 0.48);
        int bottom = top ? (int) (frameHeight * 0.52) : frameHeight;
        double widthRatio = frameWidth <= 1400 ? 0.78 : 0.72;
        int cropWidth = Math.max(1, Math.min(frameWidth, (int) Math.round(frameWidth * widthRatio)));
        int cropHeight = Math.max(1, Math.min(frameHeight - y, bottom - y));

        Bitmap rawCrop = Bitmap.createBitmap(frame, 0, y, cropWidth, cropHeight);
        try {
            return prepareCropPasses(rawCrop, y, top ? "top" : "bot");
        } finally {
            rawCrop.recycle();
        }
    }

    static BandPasses prepareCropPasses(Bitmap rawCrop) {
        return prepareCropPasses(rawCrop, 0, "crop");
    }

    static BandPasses prepareCropPasses(Bitmap rawCrop, int originY, String bandTag) {
        int cropW = rawCrop.getWidth();
        int cropH = rawCrop.getHeight();
        int scale = OcrTextCore.computeUpscaleFactor(cropW, cropH);
        int pad = OcrTextCore.PAD_PX;
        int priorityScale = Math.max(scale, 5);
        int maxEdge = Math.max(cropW, cropH);
        while (priorityScale > 2 && (maxEdge * priorityScale) + (pad * 2) > OcrTextCore.MAX_UPSCALED_EDGE) {
            priorityScale--;
        }

        int[] rawPixels = new int[cropW * cropH];
        rawCrop.getPixels(rawPixels, 0, cropW, 0, 0, cropW, cropH);

        Bitmap cleanPass = null;
        Bitmap isolatedPass = null;
        Bitmap purplePass = null;
        Bitmap accentPass = null;
        try {
            cleanPass = buildCleanUpscaled(rawCrop, rawPixels, scale, pad);
            isolatedPass = buildUpscaledFromPixels(
                OcrTextCore.isolateOutlinedChatPixels(rawPixels, cropW, cropH), cropW, cropH, scale, pad);
            int[] purplePixels = OcrTextCore.isolatePurpleEventPixels(rawPixels, cropW, cropH);
            if (purplePixels != null) {
                purplePass = buildUpscaledFromPixels(purplePixels, cropW, cropH, priorityScale, pad);
            }
            int[] accentPixels = OcrTextCore.isolatePriorityAccentPixels(rawPixels, cropW, cropH);
            if (accentPixels != null) {
                accentPass = buildUpscaledFromPixels(accentPixels, cropW, cropH, priorityScale, pad);
            }
            return new BandPasses(cleanPass, isolatedPass, purplePass, accentPass,
                scale, priorityScale, priorityScale, pad, cropW, originY, bandTag);
        } catch (RuntimeException | Error failure) {
            if (cleanPass != null && !cleanPass.isRecycled()) cleanPass.recycle();
            if (isolatedPass != null && !isolatedPass.isRecycled()) isolatedPass.recycle();
            if (purplePass != null && !purplePass.isRecycled()) purplePass.recycle();
            if (accentPass != null && !accentPass.isRecycled()) accentPass.recycle();
            throw failure;
        }
    }

    private static Bitmap buildCleanUpscaled(Bitmap rawCrop, int[] rawPixels, int scale, int pad) {
        int w = rawCrop.getWidth();
        int h = rawCrop.getHeight();
        int dstW = (w * scale) + (pad * 2);
        int dstH = (h * scale) + (pad * 2);

        Bitmap source = rawCrop;
        Bitmap sharpened = null;
        Bitmap canvasBmp = null;
        try {
            if (scale >= 2 && w >= 3 && h >= 3) {
                // Pre-upscale sharpening restores 1px low-res strokes cleanly without ringing
                // and runs on the compact raw crop (up to 16x fewer pixels than post-upscale).
                float amount = scale >= 3 ? 0.22f : 0.14f;
                int[] sharpPixels = OcrTextCore.sharpenPixels(rawPixels, w, h, amount);
                sharpened = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888);
                sharpened.setPixels(sharpPixels, 0, w, 0, 0, w, h);
                source = sharpened;
            }

            canvasBmp = Bitmap.createBitmap(dstW, dstH, Bitmap.Config.ARGB_8888);
            Canvas canvas = new Canvas(canvasBmp);
            canvas.drawColor(0xFF1C1C22);
            Paint paint = new Paint(Paint.FILTER_BITMAP_FLAG | Paint.DITHER_FLAG);
            canvas.drawBitmap(
                source,
                new Rect(0, 0, w, h),
                new Rect(pad, pad, pad + w * scale, pad + h * scale),
                paint);
            return canvasBmp;
        } catch (RuntimeException | Error failure) {
            if (canvasBmp != null && !canvasBmp.isRecycled()) canvasBmp.recycle();
            throw failure;
        } finally {
            if (sharpened != null && !sharpened.isRecycled()) sharpened.recycle();
        }
    }

    private static Bitmap buildUpscaledFromPixels(int[] isoPixels, int w, int h, int scale, int pad) {
        Bitmap isolated = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888);
        Bitmap upscaled = null;
        try {
            isolated.setPixels(isoPixels, 0, w, 0, 0, w, h);
            int dstW = (w * scale) + (pad * 2);
            int dstH = (h * scale) + (pad * 2);
            upscaled = Bitmap.createBitmap(dstW, dstH, Bitmap.Config.ARGB_8888);
            Canvas canvas = new Canvas(upscaled);
            canvas.drawColor(0xFFFFFFFF);
            Paint paint = new Paint(Paint.FILTER_BITMAP_FLAG | Paint.DITHER_FLAG);
            canvas.drawBitmap(
                isolated,
                new Rect(0, 0, w, h),
                new Rect(pad, pad, pad + w * scale, pad + h * scale),
                paint);
            return upscaled;
        } catch (RuntimeException | Error failure) {
            if (upscaled != null && !upscaled.isRecycled()) upscaled.recycle();
            throw failure;
        } finally {
            isolated.recycle();
        }
    }

    static List<OcrTextCore.PositionedLine> extractPositionedLines(
            Text text, String passTag, int scale, int pad, int rawCropWidth) {
        return extractPositionedLines(text, passTag, scale, pad, rawCropWidth, 0);
    }

    static List<OcrTextCore.PositionedLine> extractPositionedLines(
            Text text, String passTag, int scale, int pad, int rawCropWidth, int originY) {
        List<OcrTextCore.RawLine> rawLines = new ArrayList<>();
        if (text != null) {
            for (Text.TextBlock block : text.getTextBlocks()) {
                for (Text.Line line : block.getLines()) {
                    Rect box = line.getBoundingBox();
                    if (box == null) continue;
                    List<OcrTextCore.RawWord> words = new ArrayList<>();
                    for (Text.Element element : line.getElements()) {
                        Rect eBox = element.getBoundingBox();
                        if (eBox == null) continue;
                        words.add(new OcrTextCore.RawWord(
                            eBox.left, eBox.top, eBox.right, eBox.bottom, element.getText()));
                    }
                    rawLines.add(new OcrTextCore.RawLine(
                        box.left, box.top, box.right, box.bottom, line.getText(), words));
                }
            }
        }
        return OcrTextCore.filterAndNormalizeLines(rawLines, passTag, scale, pad, rawCropWidth, originY);
    }

    private AndroidOcrPipeline() {}
}

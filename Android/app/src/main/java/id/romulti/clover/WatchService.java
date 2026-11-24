package id.romulti.clover;

import android.app.Activity;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.ServiceInfo;
import android.content.res.Configuration;
import android.graphics.Bitmap;
import android.graphics.PixelFormat;
import android.graphics.Point;
import android.graphics.Rect;
import android.hardware.display.DisplayManager;
import android.hardware.display.VirtualDisplay;
import android.media.Image;
import android.media.ImageReader;
import android.media.projection.MediaProjection;
import android.media.projection.MediaProjectionManager;
import android.os.Build;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.IBinder;
import android.os.SystemClock;
import android.text.format.DateFormat;
import android.view.WindowManager;

import com.google.mlkit.vision.common.InputImage;
import com.google.mlkit.vision.text.TextRecognition;
import com.google.mlkit.vision.text.TextRecognizer;
import com.google.mlkit.vision.text.latin.TextRecognizerOptions;

import java.net.URI;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.RejectedExecutionException;
import java.util.concurrent.ThreadPoolExecutor;
import java.util.concurrent.TimeUnit;

public final class WatchService extends Service {
    static final String EXTRA_RESULT = "capture_result";
    static final String EXTRA_PERMISSION = "capture_permission";
    private static final String STOP = "id.romulti.clover.STOP";
    private static final String CHANNEL = "clover_capture";
    private static final int NOTICE_ID = 21;
    private static final int SCAN_MS = 3000;
    private static final int MAX_EDGE = 2160;
    private static volatile boolean running;

    private HandlerThread thread;
    private Handler worker;
    private SharedPreferences settings;
    private MediaProjection projection;
    private VirtualDisplay display;
    private ImageReader reader;
    private TextRecognizer recognizer;
    private URI webhook;
    private int region;
    private boolean includeCommon;
    private boolean inFlight;
    private boolean isVisible = true;
    private int generation;
    private long nextScan;
    private long lastFrame;
    private final EventHistory history = new EventHistory();
    private volatile boolean active;
    private volatile String stopMessage = "Pemantauan dihentikan.";
    private final ThreadPoolExecutor delivery = new ThreadPoolExecutor(
        1, 1, 0L, TimeUnit.MILLISECONDS, new ArrayBlockingQueue<>(12));

    private final MediaProjection.Callback projectionCallback = new MediaProjection.Callback() {
        @Override
        public void onStop() {
            if (active) end("Izin capture berakhir. Tekan Mulai untuk melanjutkan.");
        }

        @Override
        public void onCapturedContentResize(int width, int height) {
            resizeCapture(width, height);
        }

        @Override
        public void onCapturedContentVisibilityChanged(boolean visible) {
            isVisible = visible;
            if (active && !visible) setStatus("Aktif · buka Roblox untuk melanjutkan scan.");
        }
    };

    private final Runnable watchdog = new Runnable() {
        @Override
        public void run() {
            if (!active) return;
            if (isVisible && SystemClock.elapsedRealtime() - lastFrame >= 12000) {
                setStatus("Aktif · belum ada frame. Periksa jendela Roblox dan izin berbagi layar.");
            }
            worker.postDelayed(this, 6000);
        }
    };

    static boolean isRunning() {
        return running;
    }

    @Override
    public void onCreate() {
        super.onCreate();
        settings = DetectorSettings.get(this);
        thread = new HandlerThread("CloverScreenCapture");
        thread.start();
        worker = new Handler(thread.getLooper());
    }

    @Override
    @SuppressWarnings("deprecation")
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent == null || STOP.equals(intent.getAction())) {
            stopSelf();
            return START_NOT_STICKY;
        }
        if (active) return START_NOT_STICKY;

        int result = intent.getIntExtra(EXTRA_RESULT, Activity.RESULT_CANCELED);
        Intent permission = Build.VERSION.SDK_INT >= 33
            ? intent.getParcelableExtra(EXTRA_PERMISSION, Intent.class)
            : intent.getParcelableExtra(EXTRA_PERMISSION);
        if (result != Activity.RESULT_OK || permission == null) {
            end("Izin capture tidak tersedia.");
            return START_NOT_STICKY;
        }

        try {
            Notification notice = createNotification();
            if (Build.VERSION.SDK_INT >= 29)
                startForeground(NOTICE_ID, notice, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION);
            else
                startForeground(NOTICE_ID, notice);
            webhook = DiscordWebhook.parse(settings.getString(DetectorSettings.WEBHOOK, ""));
            region = settings.getInt(DetectorSettings.REGION, 0);
            includeCommon = settings.getBoolean(DetectorSettings.COMMON, false);
            active = true;
            running = true;
            setStatus("Aktif · menunggu frame Roblox.");
            worker.post(() -> startProjection(result, permission));
        } catch (RuntimeException failure) {
            end("Capture gagal dimulai. Buka kembali aplikasi dan izinkan capture.");
        }
        return START_NOT_STICKY;
    }

    private Notification createNotification() {
        NotificationManager manager = getSystemService(NotificationManager.class);
        manager.createNotificationChannel(new NotificationChannel(
            CHANNEL, "Clover screen capture", NotificationManager.IMPORTANCE_LOW));
        PendingIntent open = PendingIntent.getActivity(this, 1, new Intent(this, MainActivity.class),
            PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        PendingIntent stop = PendingIntent.getService(this, 2, new Intent(this, WatchService.class).setAction(STOP),
            PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        Notification.Action stopAction = new Notification.Action.Builder(
            android.graphics.drawable.Icon.createWithResource(this, android.R.drawable.ic_media_pause),
            "Hentikan", stop).build();
        return new Notification.Builder(this, CHANNEL)
            .setSmallIcon(android.R.drawable.ic_menu_camera)
            .setContentTitle("Clover Event Detector")
            .setContentText("Memantau chat dari layar yang kamu bagikan.")
            .setContentIntent(open)
            .addAction(stopAction)
            .setOngoing(true)
            .build();
    }

    private void startProjection(int result, Intent permission) {
        if (!active) return;
        try {
            MediaProjectionManager manager = getSystemService(MediaProjectionManager.class);
            projection = manager.getMediaProjection(result, permission);
            if (projection == null) throw new IllegalStateException("Capture permission expired");
            projection.registerCallback(projectionCallback, worker);
            recognizer = TextRecognition.getClient(TextRecognizerOptions.DEFAULT_OPTIONS);
            Point size = displaySize();
            resizeCapture(size.x, size.y);
            lastFrame = SystemClock.elapsedRealtime();
            worker.postDelayed(watchdog, 6000);
        } catch (RuntimeException failure) {
            end("Capture layar gagal. Coba mulai lagi dari aplikasi.");
        }
    }

    @SuppressWarnings("deprecation")
    private Point displaySize() {
        WindowManager manager = getSystemService(WindowManager.class);
        if (Build.VERSION.SDK_INT >= 30) {
            Rect bounds = manager.getMaximumWindowMetrics().getBounds();
            return new Point(bounds.width(), bounds.height());
        }
        Point size = new Point();
        manager.getDefaultDisplay().getRealSize(size);
        return size;
    }

    @Override
    public void onConfigurationChanged(Configuration config) {
        super.onConfigurationChanged(config);
        if (Build.VERSION.SDK_INT >= 34 || !active) return;
        Point size = displaySize();
        worker.post(() -> resizeCapture(size.x, size.y));
    }

    private void resizeCapture(int contentWidth, int contentHeight) {
        if (!active || projection == null || contentWidth <= 0 || contentHeight <= 0) return;
        double scale = Math.min(1.0, (double) MAX_EDGE / Math.max(contentWidth, contentHeight));
        int width = Math.max(2, (int) Math.round(contentWidth * scale));
        int height = Math.max(2, (int) Math.round(contentHeight * scale));
        if (reader != null && reader.getWidth() == width && reader.getHeight() == height) return;

        ImageReader replacement = null;
        try {
            int density = getResources().getDisplayMetrics().densityDpi;
            replacement = ImageReader.newInstance(width, height, PixelFormat.RGBA_8888, 2);
            replacement.setOnImageAvailableListener(this::onFrame, worker);
            if (display == null) {
                display = projection.createVirtualDisplay("Clover chat", width, height, density,
                    DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR, replacement.getSurface(), null, worker);
                if (display == null) throw new IllegalStateException("Virtual display unavailable");
            } else {
                display.setSurface(null);
                display.resize(width, height, density);
                display.setSurface(replacement.getSurface());
            }
            if (reader != null) reader.close();
            reader = replacement;
            nextScan = 0;
            generation++;
        } catch (RuntimeException failure) {
            if (replacement != null) replacement.close();
            end("Capture gagal menyesuaikan layar. Buka ulang pemantauan.");
        }
    }

    private void onFrame(ImageReader source) {
        if (source != reader || !active) return;
        try (Image image = source.acquireLatestImage()) {
            if (image == null) return;
            long now = SystemClock.elapsedRealtime();
            lastFrame = now;
            if (!isVisible || inFlight || now < nextScan) return;
            nextScan = now + SCAN_MS;
            if (image.getWidth() < image.getHeight()) {
                setStatus("Aktif · menunggu Roblox dalam posisi landscape.");
                return;
            }
            List<AndroidOcrPipeline.BandPasses> bands = cropChat(image);
            inFlight = true;
            recognizeBand(bands, 0, new ArrayList<>(), generation);
        } catch (RuntimeException failure) {
            inFlight = false;
            setStatus("Aktif · frame gagal dibaca. Scan akan dicoba lagi.");
        }
    }

    private List<AndroidOcrPipeline.BandPasses> cropChat(Image image) {
        Image.Plane plane = image.getPlanes()[0];
        int paddedWidth = plane.getRowStride() / plane.getPixelStride();
        Bitmap frame = Bitmap.createBitmap(paddedWidth, image.getHeight(), Bitmap.Config.ARGB_8888);
        List<AndroidOcrPipeline.BandPasses> bands = new ArrayList<>(2);
        try {
            frame.copyPixelsFromBuffer(plane.getBuffer());
            if (region != 2) bands.add(AndroidOcrPipeline.prepareBandPasses(frame, image.getWidth(), image.getHeight(), true));
            if (region != 1) bands.add(AndroidOcrPipeline.prepareBandPasses(frame, image.getWidth(), image.getHeight(), false));
            return bands;
        } catch (RuntimeException | Error problem) {
            recycleBands(bands, 0);
            throw problem;
        } finally {
            frame.recycle();
        }
    }

    private static void recycleBands(List<AndroidOcrPipeline.BandPasses> bands, int startIndex) {
        for (int i = startIndex; i < bands.size(); i++) {
            bands.get(i).recycle();
        }
    }

    private void recognizeBand(
            List<AndroidOcrPipeline.BandPasses> bands,
            int index,
            List<OcrTextCore.PositionedLine> allPositioned,
            int frameGeneration) {
        if (!active || frameGeneration != generation || !isVisible) {
            recycleBands(bands, index);
            inFlight = false;
            return;
        }
        if (index == bands.size()) {
            inFlight = false;
            onRecognized(OcrTextCore.toLineTextsWithGapBreaks(allPositioned));
            return;
        }

        AndroidOcrPipeline.BandPasses band = bands.get(index);
        try {
            recognizer.process(InputImage.fromBitmap(band.cleanPass, 0)).addOnCompleteListener(command -> {
                if (!worker.post(command)) command.run();
            }, task1 -> {
                if (!band.cleanPass.isRecycled()) band.cleanPass.recycle();
                if (!active || frameGeneration != generation || !isVisible) {
                    recycleBands(bands, index);
                    inFlight = false;
                    return;
                }
                List<OcrTextCore.PositionedLine> pass1 = task1.isSuccessful()
                    ? AndroidOcrPipeline.extractPositionedLines(
                        task1.getResult(), band.bandTag + ":p1", band.scale, band.pad, band.rawCropWidth, band.originY)
                    : null;

                try {
                    recognizer.process(InputImage.fromBitmap(band.isolatedPass, 0)).addOnCompleteListener(command -> {
                        if (!worker.post(command)) command.run();
                    }, task2 -> {
                        if (!band.isolatedPass.isRecycled()) band.isolatedPass.recycle();
                        if (!active || frameGeneration != generation || !isVisible) {
                            recycleBands(bands, index);
                            inFlight = false;
                            return;
                        }
                        List<OcrTextCore.PositionedLine> pass2 = task2.isSuccessful()
                            ? AndroidOcrPipeline.extractPositionedLines(
                                task2.getResult(), band.bandTag + ":p2", band.scale, band.pad, band.rawCropWidth, band.originY)
                            : null;
                        if (pass1 == null && pass2 == null) {
                            recycleBands(bands, index);
                            inFlight = false;
                            if (active) setStatus("Aktif · OCR gagal. Scan akan dicoba lagi.");
                            return;
                        }
                        List<OcrTextCore.PositionedLine> merged = OcrTextCore.mergePasses(pass1, pass2);
                        runPurplePass(bands, index, allPositioned, merged, frameGeneration);
                    });
                } catch (RuntimeException failure) {
                    recycleBands(bands, index);
                    inFlight = false;
                    if (active) setStatus("Aktif · OCR gagal. Scan akan dicoba lagi.");
                }
            });
        } catch (RuntimeException failure) {
            recycleBands(bands, index);
            inFlight = false;
            if (active) setStatus("Aktif · OCR gagal. Scan akan dicoba lagi.");
        }
    }

    private void runPurplePass(
            List<AndroidOcrPipeline.BandPasses> bands,
            int index,
            List<OcrTextCore.PositionedLine> allPositioned,
            List<OcrTextCore.PositionedLine> merged,
            int frameGeneration) {
        AndroidOcrPipeline.BandPasses band = bands.get(index);
        if (band.purplePass == null || band.purplePass.isRecycled()) {
            runAccentPass(bands, index, allPositioned, merged, frameGeneration);
            return;
        }
        try {
            recognizer.process(InputImage.fromBitmap(band.purplePass, 0)).addOnCompleteListener(command -> {
                if (!worker.post(command)) command.run();
            }, task3 -> {
                if (!band.purplePass.isRecycled()) band.purplePass.recycle();
                if (!active || frameGeneration != generation || !isVisible) {
                    recycleBands(bands, index);
                    inFlight = false;
                    return;
                }
                List<OcrTextCore.PositionedLine> nextMerged = merged;
                if (task3.isSuccessful()) {
                    List<OcrTextCore.PositionedLine> pass3 = AndroidOcrPipeline.extractPositionedLines(
                        task3.getResult(), band.bandTag + ":p3", band.purpleScale, band.pad, band.rawCropWidth, band.originY);
                    nextMerged = OcrTextCore.mergePasses(merged, pass3);
                }
                runAccentPass(bands, index, allPositioned, nextMerged, frameGeneration);
            });
        } catch (RuntimeException failure) {
            if (!band.purplePass.isRecycled()) band.purplePass.recycle();
            runAccentPass(bands, index, allPositioned, merged, frameGeneration);
        }
    }

    private void runAccentPass(
            List<AndroidOcrPipeline.BandPasses> bands,
            int index,
            List<OcrTextCore.PositionedLine> allPositioned,
            List<OcrTextCore.PositionedLine> merged,
            int frameGeneration) {
        AndroidOcrPipeline.BandPasses band = bands.get(index);
        if (band.accentPass == null || band.accentPass.isRecycled()) {
            appendBandAndContinue(bands, index, allPositioned, merged, frameGeneration);
            return;
        }
        try {
            recognizer.process(InputImage.fromBitmap(band.accentPass, 0)).addOnCompleteListener(command -> {
                if (!worker.post(command)) command.run();
            }, task4 -> {
                if (!band.accentPass.isRecycled()) band.accentPass.recycle();
                if (!active || frameGeneration != generation || !isVisible) {
                    recycleBands(bands, index);
                    inFlight = false;
                    return;
                }
                List<OcrTextCore.PositionedLine> finalMerged = merged;
                if (task4.isSuccessful()) {
                    List<OcrTextCore.PositionedLine> pass4 = AndroidOcrPipeline.extractPositionedLines(
                        task4.getResult(), band.bandTag + ":p4", band.accentScale, band.pad, band.rawCropWidth, band.originY);
                    finalMerged = OcrTextCore.mergePasses(merged, pass4);
                }
                appendBandAndContinue(bands, index, allPositioned, finalMerged, frameGeneration);
            });
        } catch (RuntimeException failure) {
            if (!band.accentPass.isRecycled()) band.accentPass.recycle();
            appendBandAndContinue(bands, index, allPositioned, merged, frameGeneration);
        }
    }

    private void appendBandAndContinue(
            List<AndroidOcrPipeline.BandPasses> bands,
            int index,
            List<OcrTextCore.PositionedLine> allPositioned,
            List<OcrTextCore.PositionedLine> bandMerged,
            int frameGeneration) {
        bands.get(index).recycle();
        List<OcrTextCore.PositionedLine> nextAll = allPositioned.isEmpty()
            ? bandMerged
            : OcrTextCore.mergePasses(allPositioned, bandMerged);
        recognizeBand(bands, index + 1, nextAll, frameGeneration);
    }

    private void onRecognized(List<String> lines) {
        EventMatcher.Frame frame = EventMatcher.scan(lines, includeCommon);
        List<EventMatcher.Detection> events = history.observe(frame, SystemClock.elapsedRealtime());
        String time = DateFormat.getTimeFormat(this).format(new Date());
        setStatus(frame.readable ? "Aktif · chat terbaca pada " + time
            : "Aktif · chat belum terbaca pada " + time);
        String privateServerCode = settings.getString(DetectorSettings.PRIVATE_SERVER, "");
        String discordUserId = settings.getString(DetectorSettings.DISCORD_USER_ID, "");
        for (EventMatcher.Detection event : events) {
            settings.edit().putString(DetectorSettings.LAST_EVENT, event.rule.title + " · " + time).apply();
            if (webhook == null) continue;
            try {
                delivery.execute(() -> sendAlert(event, privateServerCode, discordUserId));
            } catch (RejectedExecutionException e) {
                publishDelivery("Antrean Discord penuh. " + event.rule.title + " terdeteksi lokal.");
            }
        }
    }

    private void sendAlert(EventMatcher.Detection event, String privateServerCode, String discordUserId) {
        try {
            DiscordWebhook.sendEvent(webhook, event, privateServerCode, discordUserId);
            publishDelivery("Discord terkirim · " + event.rule.title);
        } catch (Exception e) {
            if (e instanceof InterruptedException) Thread.currentThread().interrupt();
            publishDelivery(event.rule.title + " · " + DiscordWebhook.explainFailure(e));
        }
    }

    private void publishDelivery(String message) {
        if (active) settings.edit().putString(DetectorSettings.DELIVERY, message).apply();
    }

    private void setStatus(String message) {
        settings.edit().putString(DetectorSettings.STATUS, message).apply();
    }

    private void end(String message) {
        stopMessage = message;
        active = false;
        stopSelf();
    }

    @Override
    public void onDestroy() {
        active = false;
        running = false;
        delivery.shutdownNow();
        stopForeground(STOP_FOREGROUND_REMOVE);
        setStatus(stopMessage);
        worker.post(() -> {
            worker.removeCallbacks(watchdog);
            if (display != null) display.release();
            if (reader != null) reader.close();
            if (projection != null) {
                projection.unregisterCallback(projectionCallback);
                projection.stop();
            }
            if (recognizer != null) recognizer.close();
            thread.quitSafely();
        });
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }
}

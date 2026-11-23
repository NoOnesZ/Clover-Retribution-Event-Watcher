package id.romulti.clover;

import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.content.res.ColorStateList;
import android.graphics.Color;
import android.graphics.Insets;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.media.projection.MediaProjectionManager;
import android.os.Build;
import android.os.Bundle;
import android.text.InputType;
import android.view.Gravity;
import android.view.View;
import android.view.WindowInsets;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.TextView;

import java.net.URI;

public final class MainActivity extends Activity {
    private static final int CAPTURE_REQUEST = 10;
    private static final int NOTIFICATIONS_REQUEST = 11;
    private static final int BACKGROUND = 0xff111316;
    private static final int PANEL = 0xff1b1f26;
    private static final int TEXT = 0xfff1f3f7;
    private static final int SECONDARY = 0xffbcc4d2;
    private static final int BLUE = 0xff3974e7;
    private static final String[] REGIONS = {"Otomatis (atas + bawah)", "Atas kiri • Jungle", "Bawah kiri • First Kingdom"};

    private SharedPreferences settings;
    private TextView status;
    private TextView lastEvent;
    private TextView deliveryStatus;
    private TextView webhookResult;
    private TextView privateServerResult;
    private EditText webhook;
    private EditText privateServer;
    private EditText discordUser;
    private Spinner region;
    private CheckBox common;
    private Button startStop;
    private Button testDiscord;
    private boolean preparing;
    private boolean testing;

    private final SharedPreferences.OnSharedPreferenceChangeListener onChange = (prefs, key) -> {
        if (DetectorSettings.STATUS.equals(key)) preparing = false;
        refreshStatus();
    };

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        settings = DetectorSettings.get(this);
        getWindow().setStatusBarColor(BACKGROUND);
        getWindow().setNavigationBarColor(BACKGROUND);

        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setClipToPadding(false);
        scroll.setBackgroundColor(BACKGROUND);
        scroll.setOnApplyWindowInsetsListener((view, insets) -> {
            if (Build.VERSION.SDK_INT >= 30) {
                Insets bars = insets.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
                view.setPadding(bars.left, bars.top, bars.right, bars.bottom);
            } else {
                view.setPadding(0, 0, 0, 0);
            }
            return insets;
        });
        setContentView(scroll);

        FrameLayout host = new FrameLayout(this);
        scroll.addView(host);
        LinearLayout page = column();
        int widthDp = Math.max(280, Math.min(560, getResources().getConfiguration().screenWidthDp));
        host.addView(page, new FrameLayout.LayoutParams(dp(widthDp), -2, Gravity.TOP | Gravity.CENTER_HORIZONTAL));
        page.setPadding(dp(20), dp(24), dp(20), dp(24));
        page.setFocusableInTouchMode(true);

        TextView title = text("Clover Event Detector", 28, TEXT);
        title.setTypeface(null, Typeface.BOLD);
        add(page, title, 0);
        add(page, text("RoMulti · Android", 15, SECONDARY), 4);

        LinearLayout capture = card();
        add(page, capture, 20);
        add(capture, section("Pemantauan"), 0);
        status = text("Siap memindai.", 16, TEXT);
        status.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        add(capture, status, 8);
        lastEvent = text("", 15, SECONDARY);
        add(capture, lastEvent, 6);
        deliveryStatus = text("", 14, SECONDARY);
        add(capture, deliveryStatus, 6);
        add(capture, text("Izinkan capture layar, lalu buka Clover dalam posisi landscape. Pilih Roblox saja kalau pilihan itu muncul.", 14, SECONDARY), 10);
        startStop = button("Mulai pemantauan", BLUE, true);
        startStop.setOnClickListener(view -> toggleCapture());
        add(capture, startStop, 16);

        LinearLayout server = card();
        add(page, server, 12);
        add(server, section("Server privat"), 0);
        add(server, text("Kode opsional yang ditampilkan di alert Discord.", 14, SECONDARY), 6);
        privateServer = formField("Kode Private Server");
        privateServer.setId(R.id.private_server_code);
        privateServer.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS);
        privateServer.setText(settings.getString(DetectorSettings.PRIVATE_SERVER, ""));
        add(server, privateServer, 4);
        Button saveServer = button("Simpan kode", 0xff303846, false);
        saveServer.setId(R.id.private_server_save);
        saveServer.setOnClickListener(view -> savePrivateServerCode());
        add(server, saveServer, 8);
        privateServerResult = text("", 14, SECONDARY);
        privateServerResult.setId(R.id.private_server_result);
        privateServerResult.setVisibility(View.GONE);
        privateServerResult.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        add(server, privateServerResult, 6);

        LinearLayout scan = card();
        add(page, scan, 12);
        add(scan, section("Area chat"), 0);
        region = new Spinner(this, Spinner.MODE_DROPDOWN);
        region.setMinimumHeight(dp(48));
        region.setContentDescription("Area chat yang dipantau");
        ArrayAdapter<String> regions = new ArrayAdapter<>(this, android.R.layout.simple_spinner_item, REGIONS);
        regions.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        region.setAdapter(regions);
        region.setSelection(Math.max(0, Math.min(2, settings.getInt(DetectorSettings.REGION, 0))));
        add(scan, region, 4);
        common = new CheckBox(this);
        common.setText(R.string.include_common_events);
        common.setTextColor(TEXT);
        common.setTextSize(15);
        common.setMinHeight(dp(48));
        common.setChecked(settings.getBoolean(DetectorSettings.COMMON, false));
        add(scan, common, 0);

        LinearLayout discord = card();
        add(page, discord, 12);
        add(discord, section("Discord"), 0);
        add(discord, text("Webhook opsional. Kosongkan untuk melihat hasil di aplikasi saja.", 14, SECONDARY), 6);
        webhook = formField("URL webhook Discord");
        webhook.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_PASSWORD);
        webhook.setText(settings.getString(DetectorSettings.WEBHOOK, ""));
        add(discord, webhook, 6);
        discordUser = formField("ID Pengguna Discord (opsional)");
        discordUser.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS);
        discordUser.setText(settings.getString(DetectorSettings.DISCORD_USER_ID, ""));
        add(discord, discordUser, 8);
        add(discord, text("Salin ID pengguna dari Discord. Ping dikirim untuk event prioritas.", 14, SECONDARY), 4);
        Button savePing = button("Simpan ping", 0xff303846, false);
        savePing.setOnClickListener(view -> saveDiscordPing());
        add(discord, savePing, 8);
        testDiscord = button("Uji Discord", 0xff303846, false);
        testDiscord.setOnClickListener(view -> testWebhook());
        add(discord, testDiscord, 8);
        webhookResult = text("", 14, SECONDARY);
        webhookResult.setVisibility(View.GONE);
        webhookResult.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        add(discord, webhookResult, 8);

        page.requestFocus();
        refreshStatus();
    }

    @Override
    protected void onStart() {
        super.onStart();
        settings.registerOnSharedPreferenceChangeListener(onChange);
        refreshStatus();
    }

    @Override
    protected void onStop() {
        settings.unregisterOnSharedPreferenceChangeListener(onChange);
        super.onStop();
    }

    private void toggleCapture() {
        if (WatchService.isRunning()) {
            stopService(new Intent(this, WatchService.class));
            return;
        }
        try {
            DiscordWebhook.parse(webhook.getText().toString());
        } catch (IllegalArgumentException e) {
            webhook.setError("Masukkan URL webhook HTTPS Discord yang valid.");
            webhook.requestFocus();
            return;
        }
        String privateServerCode = readPrivateServerCode();
        String userId = readDiscordUserId();
        if (privateServerCode == null || userId == null) return;
        settings.edit()
            .putString(DetectorSettings.WEBHOOK, webhook.getText().toString().trim())
            .putString(DetectorSettings.PRIVATE_SERVER, privateServerCode)
            .putString(DetectorSettings.DISCORD_USER_ID, userId)
            .putInt(DetectorSettings.REGION, region.getSelectedItemPosition())
            .putBoolean(DetectorSettings.COMMON, common.isChecked())
            .apply();
        if (Build.VERSION.SDK_INT >= 33 &&
                checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED &&
                !settings.getBoolean("asked_notifications", false)) {
            settings.edit().putBoolean("asked_notifications", true).apply();
            requestPermissions(new String[] { Manifest.permission.POST_NOTIFICATIONS }, NOTIFICATIONS_REQUEST);
        } else {
            askForCapture();
        }
    }

    private String readPrivateServerCode() {
        try {
            return DiscordWebhook.normalizePrivateServerCode(privateServer.getText().toString());
        } catch (IllegalArgumentException error) {
            privateServer.setError("Gunakan kode dengan huruf, angka, tanda hubung, atau garis bawah.");
            privateServer.requestFocus();
            return null;
        }
    }

    private void savePrivateServerCode() {
        String code = readPrivateServerCode();
        if (code == null) return;
        settings.edit().putString(DetectorSettings.PRIVATE_SERVER, code).apply();
        privateServer.setText(code);
        privateServerResult.setText(code.isEmpty() ? "Kode dihapus dari alert berikutnya." : "Kode tersimpan untuk alert berikutnya.");
        privateServerResult.setVisibility(View.VISIBLE);
        getSystemService(android.view.inputmethod.InputMethodManager.class)
            .hideSoftInputFromWindow(privateServer.getWindowToken(), 0);
    }

    private String readDiscordUserId() {
        try {
            return DiscordWebhook.normalizeDiscordUserId(discordUser.getText().toString());
        } catch (IllegalArgumentException error) {
            discordUser.setError("Tempel ID numerik Discord atau mention seperti <@123…>.");
            discordUser.requestFocus();
            return null;
        }
    }

    private void saveDiscordPing() {
        String id = readDiscordUserId();
        if (id == null) return;
        settings.edit().putString(DetectorSettings.DISCORD_USER_ID, id).apply();
        discordUser.setText(id);
        showWebhookResult(id.isEmpty() ? "Ping akun Discord dinonaktifkan." : "Ping event prioritas tersimpan.");
        getSystemService(android.view.inputmethod.InputMethodManager.class)
            .hideSoftInputFromWindow(discordUser.getWindowToken(), 0);
    }

    @Override
    public void onRequestPermissionsResult(int request, String[] permissions, int[] results) {
        super.onRequestPermissionsResult(request, permissions, results);
        if (request == NOTIFICATIONS_REQUEST) askForCapture();
    }

    private void askForCapture() {
        MediaProjectionManager manager = getSystemService(MediaProjectionManager.class);
        if (manager == null) {
            settings.edit().putString(DetectorSettings.STATUS, "Perangkat tidak mendukung capture layar.").apply();
            return;
        }
        startActivityForResult(manager.createScreenCaptureIntent(), CAPTURE_REQUEST);
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent permission) {
        super.onActivityResult(requestCode, resultCode, permission);
        if (requestCode != CAPTURE_REQUEST) return;
        if (resultCode != RESULT_OK || permission == null) {
            settings.edit().putString(DetectorSettings.STATUS, "Izin capture belum diberikan.").apply();
            return;
        }
        Intent service = new Intent(this, WatchService.class)
            .putExtra(WatchService.EXTRA_RESULT, resultCode)
            .putExtra(WatchService.EXTRA_PERMISSION, permission);
        preparing = true;
        refreshStatus();
        try {
            startForegroundService(service);
        } catch (RuntimeException error) {
            preparing = false;
            settings.edit().putString(DetectorSettings.STATUS, "Capture gagal dimulai. Coba berikan izin lagi.").apply();
        }
    }

    private void testWebhook() {
        URI url;
        try {
            url = DiscordWebhook.parse(webhook.getText().toString());
            if (url == null) throw new IllegalArgumentException();
        } catch (IllegalArgumentException error) {
            webhook.setError("Masukkan URL webhook HTTPS Discord yang valid.");
            webhook.requestFocus();
            return;
        }
        String privateServerCode = readPrivateServerCode();
        String userId = readDiscordUserId();
        if (privateServerCode == null || userId == null) return;
        settings.edit()
            .putString(DetectorSettings.WEBHOOK, webhook.getText().toString().trim())
            .putString(DetectorSettings.PRIVATE_SERVER, privateServerCode)
            .putString(DetectorSettings.DISCORD_USER_ID, userId)
            .apply();
        testing = true;
        refreshStatus();
        showWebhookResult("Mengirim pesan uji…");
        new Thread(() -> {
            String result;
            try {
                DiscordWebhook.sendTest(url, privateServerCode, userId);
                result = "Pesan uji berhasil dikirim.";
            } catch (Exception error) {
                result = DiscordWebhook.explainFailure(error);
            }
            final String outcome = result;
            runOnUiThread(() -> {
                if (isDestroyed()) return;
                testing = false;
                refreshStatus();
                showWebhookResult(outcome);
            });
        }, "CloverWebhookTest").start();
    }

    private EditText formField(String hint) {
        EditText input = new EditText(this);
        input.setHint(hint);
        input.setContentDescription(hint);
        input.setTextColor(TEXT);
        input.setHintTextColor(SECONDARY);
        input.setTextSize(15);
        input.setSingleLine(true);
        input.setImportantForAutofill(View.IMPORTANT_FOR_AUTOFILL_NO);
        input.setMinHeight(dp(48));
        return input;
    }

    private void showWebhookResult(String message) {
        webhookResult.setVisibility(View.VISIBLE);
        webhookResult.setText(message);
    }

    private void refreshStatus() {
        boolean running = WatchService.isRunning();
        String saved = settings.getString(DetectorSettings.STATUS, "Siap memindai.");
        if (!running && saved.startsWith("Aktif")) saved = "Pemantauan berhenti. Tekan Mulai untuk melanjutkan.";
        status.setText(preparing ? "Memulai capture…" : saved);
        String latest = settings.getString(DetectorSettings.LAST_EVENT, "");
        lastEvent.setText(latest.isEmpty() ? "Belum ada event baru." : "Terakhir · " + latest);
        String delivery = settings.getString(DetectorSettings.DELIVERY, "");
        deliveryStatus.setVisibility(delivery.isEmpty() ? View.GONE : View.VISIBLE);
        deliveryStatus.setText(delivery);
        startStop.setText(running ? "Hentikan pemantauan" : "Mulai pemantauan");
        startStop.setEnabled(!preparing);
        webhook.setEnabled(!running && !preparing && !testing);
        testDiscord.setEnabled(!preparing && !testing);
        region.setEnabled(!running && !preparing);
        common.setEnabled(!running && !preparing);
    }

    private LinearLayout column() {
        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        return layout;
    }

    private LinearLayout card() {
        LinearLayout card = column();
        card.setBackground(round(PANEL, 14));
        card.setPadding(dp(16), dp(14), dp(16), dp(14));
        return card;
    }

    private TextView section(String label) {
        TextView view = text(label, 18, TEXT);
        view.setTypeface(null, Typeface.BOLD);
        return view;
    }

    private TextView text(String label, int size, int color) {
        TextView view = new TextView(this);
        view.setText(label);
        view.setTextSize(size);
        view.setTextColor(color);
        return view;
    }

    private Button button(String label, int color, boolean primary) {
        Button button = new Button(this);
        button.setAllCaps(false);
        button.setTextSize(16);
        button.setText(label);
        button.setMinHeight(dp(48));
        button.setTextColor(Color.WHITE);
        button.setBackgroundTintList(new ColorStateList(
            new int[][] {{-android.R.attr.state_enabled}, {}},
            new int[] {0xff40454e, color}));
        if (primary) button.setTypeface(null, Typeface.BOLD);
        return button;
    }

    private GradientDrawable round(int color, int radius) {
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(color);
        bg.setCornerRadius(dp(radius));
        return bg;
    }

    private void add(LinearLayout parent, View view, int top) {
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(-1, -2);
        params.topMargin = dp(top);
        parent.addView(view, params);
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }
}

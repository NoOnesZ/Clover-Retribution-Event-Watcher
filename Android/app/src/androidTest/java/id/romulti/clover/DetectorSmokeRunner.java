package id.romulti.clover;

import android.app.Activity;
import android.app.Instrumentation;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.os.Bundle;

import com.google.android.gms.tasks.Tasks;
import com.google.mlkit.vision.common.InputImage;
import com.google.mlkit.vision.text.Text;
import com.google.mlkit.vision.text.TextRecognition;
import com.google.mlkit.vision.text.TextRecognizer;
import com.google.mlkit.vision.text.latin.TextRecognizerOptions;

import org.json.JSONObject;

import java.util.Arrays;
import java.util.concurrent.TimeUnit;

public final class DetectorSmokeRunner extends Instrumentation {
    public static void main(String[] args) throws Exception {
        EventReplay.verify();
        verifyPrivateServerField();
        verifyDiscordPing();
        System.out.println("Android event replay and Discord ping/private server payloads: PASS");
    }

    @Override
    public void onCreate(Bundle arguments) {
        super.onCreate(arguments);
        start();
    }

    @Override
    public void onStart() {
        Bundle result = new Bundle();
        Bitmap image = Bitmap.createBitmap(1100, 240, Bitmap.Config.ARGB_8888);
        TextRecognizer recognizer = null;
        try {
            Canvas canvas = new Canvas(image);
            canvas.drawColor(0xff191925);
            Paint ink = new Paint(Paint.ANTI_ALIAS_FLAG);
            ink.setTextSize(42);
            ink.setColor(Color.rgb(223, 180, 255));
            canvas.drawText("Something feels off...", 24, 72, ink);
            ink.setColor(Color.rgb(255, 225, 132));
            canvas.drawText("A strange energy feels toward the kingdom", 24, 152, ink);

            recognizer = TextRecognition.getClient(TextRecognizerOptions.DEFAULT_OPTIONS);
            Text text = Tasks.await(recognizer.process(InputImage.fromBitmap(image, 0)), 25, TimeUnit.SECONDS);
            EventMatcher.Frame frame = EventMatcher.scan(Arrays.asList(text.getText().split("\\r?\\n")), false);
            boolean devil = frame.events.stream().anyMatch(event -> event.rule.id.equals("devil_rift"));
            boolean spirit = frame.events.stream().anyMatch(event -> event.rule.id.equals("spirit_rift"));
            if (!devil || !spirit) throw new AssertionError("Android OCR did not recognize both rifts");
            verifyPrivateServerField();
            verifyDiscordPing();
            EventReplay.verify();

            result.putString("stream", "\nAndroid OCR, event replay, private server and Discord ping payloads: PASS\n");
            finish(Activity.RESULT_OK, result);
        } catch (Exception | AssertionError failure) {
            result.putString("stream", "\nAndroid OCR fixture FAILED: " + failure + "\n");
            finish(Activity.RESULT_CANCELED, result);
        } finally {
            if (recognizer != null) recognizer.close();
            image.recycle();
        }
    }

    private static void verifyPrivateServerField() throws Exception {
        JSONObject oldEvent = DiscordWebhook.buildPayload("Devil Rift", "Something feels off...", 0,
            "server-old", "");
        JSONObject newEvent = DiscordWebhook.buildPayload("Devil Rift", "Something feels off...", 0,
            " server-new ", "");
        JSONObject blank = DiscordWebhook.buildPayload("Devil Rift", "Something feels off...", 0, "", "");
        JSONObject oldField = oldEvent.getJSONArray("embeds").getJSONObject(0)
            .getJSONArray("fields").getJSONObject(0);
        JSONObject newField = newEvent.getJSONArray("embeds").getJSONObject(0)
            .getJSONArray("fields").getJSONObject(0);
        if (!oldField.getString("value").equals("server-old") ||
                !newField.getString("name").equals("Private server") ||
                !newField.getString("value").equals("server-new") ||
                blank.getJSONArray("embeds").getJSONObject(0).has("fields")) {
            throw new AssertionError("Webhook did not reflect the selected private server.");
        }
    }

    private static void verifyDiscordPing() throws Exception {
        String user = "123456789012345678";
        EventMatcher.Detection rare = EventMatcher.scan(Arrays.asList("Something feels off..."), true).events.get(0);
        EventMatcher.Detection common = EventMatcher.scan(
            Arrays.asList("[Event] Mana fluttering can be heard across the grasslands."), true).events.get(0);
        JSONObject priority = DiscordWebhook.buildEventPayload(rare, "server-demo", "<@!" + user + ">");
        JSONObject normal = DiscordWebhook.buildEventPayload(common, "server-demo", user);
        JSONObject noPing = DiscordWebhook.buildEventPayload(rare, "server-demo", "");
        if (!priority.getString("content").equals("<@" + user + ">") ||
                !priority.getJSONObject("allowed_mentions").getJSONArray("users").getString(0).equals(user) ||
                priority.getJSONObject("allowed_mentions").getJSONArray("parse").length() != 0 ||
                normal.has("content") || noPing.has("content")) {
            throw new AssertionError("Discord ping was not restricted to its configured user and priority.");
        }
        try {
            DiscordWebhook.normalizeDiscordUserId("@everyone");
            throw new AssertionError("Group mentions must be rejected.");
        } catch (IllegalArgumentException expected) {
            // Only numeric Discord user IDs are allowed.
        }
    }
}

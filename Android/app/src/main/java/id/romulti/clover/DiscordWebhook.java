package id.romulti.clover;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.IOException;
import java.io.OutputStream;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.time.Instant;
import java.util.Locale;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

import javax.net.ssl.HttpsURLConnection;

final class DiscordWebhook {
    private static final Pattern WEBHOOK_PATH = Pattern.compile(
        "/api/(?:v[0-9]+/)?webhooks/[0-9]{5,25}/[A-Za-z0-9_-]+");
    private static final Pattern PRIVATE_SERVER_CODE = Pattern.compile("[A-Za-z0-9_-]{0,128}");
    private static final Pattern USER_MENTION = Pattern.compile("<@!?([1-9][0-9]{14,19})>");
    private static final Pattern USER_ID = Pattern.compile("[1-9][0-9]{14,19}");

    static URI parse(String raw) {
        if (raw.trim().isEmpty()) return null;
        URI uri = URI.create(raw.trim());
        String host = uri.getHost() == null ? "" : uri.getHost().toLowerCase(Locale.ROOT);
        boolean discordHost = host.equals("discord.com") || host.equals("discordapp.com");
        if (!"https".equalsIgnoreCase(uri.getScheme()) || !discordHost
                || uri.getUserInfo() != null || uri.getPort() != -1 || uri.getRawQuery() != null
                || uri.getFragment() != null || !WEBHOOK_PATH.matcher(uri.getRawPath()).matches()) {
            throw new IllegalArgumentException("Invalid Discord webhook");
        }
        return uri;
    }

    static String normalizePrivateServerCode(String raw) {
        String code = raw.trim();
        if (!PRIVATE_SERVER_CODE.matcher(code).matches()) {
            throw new IllegalArgumentException("Invalid private server code");
        }
        return code;
    }

    static String normalizeDiscordUserId(String raw) {
        String value = raw.trim();
        Matcher mention = USER_MENTION.matcher(value);
        if (mention.matches()) value = mention.group(1);
        if (!value.isEmpty() && !USER_ID.matcher(value).matches()) {
            throw new IllegalArgumentException("Invalid Discord user ID");
        }
        return value;
    }

    static void sendEvent(URI uri, EventMatcher.Detection event, String privateServerCode, String userId) throws Exception {
        post(uri, buildEventPayload(event, privateServerCode, userId));
    }

    static void sendTest(URI uri, String privateServerCode, String userId) throws Exception {
        post(uri, buildPayload("Clover Event Detector", "Webhook Android terhubung.", 0x3B82F6, privateServerCode, userId));
    }

    static JSONObject buildEventPayload(EventMatcher.Detection event, String privateServerCode, String userId) throws Exception {
        String description = event.rule.isCatchAll() ? event.text : "";
        return buildPayload(event.rule.title, description, event.rule.color, privateServerCode,
            event.rule.priority ? userId : "");
    }

    static JSONObject buildPayload(String title, String message, int color, String privateServerCode, String userId) throws Exception {
        String description = message == null ? "" : message.trim();
        if (description.length() > 450) description = description.substring(0, 450);
        JSONObject embed = new JSONObject()
            .put("title", title)
            .put("color", color)
            .put("timestamp", Instant.now().toString())
            .put("footer", new JSONObject().put("text", "Clover Event Detector · Android"));
        if (!description.isEmpty()) {
            embed.put("description", description);
        }
        String code = normalizePrivateServerCode(privateServerCode);
        if (!code.isEmpty()) {
            JSONObject field = new JSONObject().put("name", "Private server").put("value", code).put("inline", true);
            embed.put("fields", new JSONArray().put(field));
        }
        JSONObject allowedMentions = new JSONObject().put("parse", new JSONArray());
        JSONObject payload = new JSONObject()
            .put("username", "Clover Event Detector")
            .put("allowed_mentions", allowedMentions)
            .put("embeds", new JSONArray().put(embed));
        String pingId = normalizeDiscordUserId(userId);
        if (!pingId.isEmpty()) {
            payload.put("content", "<@" + pingId + ">");
            allowedMentions.put("users", new JSONArray().put(pingId));
        }
        return payload;
    }

    private static void post(URI uri, JSONObject payload) throws Exception {
        byte[] bytes = payload.toString().getBytes(StandardCharsets.UTF_8);

        HttpsURLConnection connection = (HttpsURLConnection) URI.create(uri.toASCIIString() + "?wait=true")
            .toURL().openConnection();
        try {
            connection.setInstanceFollowRedirects(false);
            connection.setConnectTimeout(7000);
            connection.setReadTimeout(7000);
            connection.setDoOutput(true);
            connection.setRequestMethod("POST");
            connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
            connection.setFixedLengthStreamingMode(bytes.length);
            try (OutputStream output = connection.getOutputStream()) {
                output.write(bytes);
            }
            int code = connection.getResponseCode();
            if (code < 200 || code >= 300) throw new HttpFailure(code);
        } finally {
            connection.disconnect();
        }
    }

    static String explainFailure(Exception error) {
        if (error instanceof HttpFailure) {
            return "Discord menolak webhook (HTTP " + ((HttpFailure) error).code + ").";
        }
        return "Discord tidak bisa dihubungi. Periksa koneksi.";
    }

    private static final class HttpFailure extends IOException {
        final int code;

        HttpFailure(int code) {
            this.code = code;
        }
    }

    private DiscordWebhook() {}
}

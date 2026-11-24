package id.romulti.clover;

import java.util.Arrays;
import java.util.Collections;
import java.util.List;

public final class DetectorChecks {
    private static EventMatcher.Frame scan(String... lines) {
        return EventMatcher.scan(Arrays.asList(lines), false);
    }

    private static void expectIds(EventMatcher.Frame frame, String... ids) {
        List<EventMatcher.Detection> detections = frame.events;
        if (detections.size() != ids.length)
            throw new AssertionError("Unexpected match count: " + detections.size());
        for (int i = 0; i < ids.length; i++) {
            if (!detections.get(i).rule.id.equals(ids[i]))
                throw new AssertionError("Expected " + ids[i] + ", got " + detections.get(i).rule.id);
        }
    }

    public static void main(String[] args) {
        expectIds(scan("[Tips] Some spells have hidden requirements", "Something feels", "off...",
            "A strange energy feels toward the kingdom"), "devil_rift", "spirit_rift");
        expectIds(scan("Event] A rift shrouded in sinister mana tears open - the",
            "Shadowhand stands watch, guarding what lies beyond."), "guarded_rift");
        expectIds(scan("[Event] The Bandit Lord vanishes, leaving only whispers", "in his wake.",
            "[Event] Khamsin has been defeated!", "[Event] The Devil Rift closes.", "Player123: Something feels off..."));
        expectIds(scan("[Event] Mana fluttering can be heard across the grasslands"));
        expectIds(EventMatcher.scan(Collections.singletonList("[Event] A warrior arrives with a fierce challenge in his eyes."),
            true), "warlord");
        expectIds(scan("[Event] A brand new eclipse descends upon the clover realm."), "unknown_event");

        EventHistory session = new EventHistory();
        EventMatcher.Frame oldChat = scan("[Event] The sands shift, Khamsin emerges from the dunes.");
        if (!session.observe(oldChat, 0).isEmpty() || !session.observe(oldChat, 3000).isEmpty()
                || !session.observe(scan(), 6000).isEmpty() || !session.observe(oldChat, 9000).isEmpty()) {
            throw new AssertionError("Baseline or temporarily faded chat emitted an old event.");
        }
        EventMatcher.Frame fresh = scan("[Event] The sands shift, Khamsin emerges from the dunes.", "Something feels off...");
        List<EventMatcher.Detection> alerts = session.observe(fresh, 12000);
        if (alerts.size() != 1 || !alerts.get(0).rule.id.equals("devil_rift")
                || !session.observe(fresh, 15000).isEmpty()) {
            throw new AssertionError("A new rift should alert once.");
        }

        verifyOcrTextCore();
        System.out.println("Android detector checks passed.");
    }

    private static void verifyOcrTextCore() {
        if (OcrTextCore.computeUpscaleFactor(360, 150) != 4
                || OcrTextCore.computeUpscaleFactor(700, 300) != 3
                || OcrTextCore.computeUpscaleFactor(1050, 420) != 2) {
            throw new AssertionError("Unexpected adaptive low-res upscale factor.");
        }

        // Uniform orange wall must wipe cleanly to pure white (0xFFFFFFFF), while outlined purple text becomes dark ink.
        int w = 15, h = 15;
        int[] orangeWall = new int[w * h];
        Arrays.fill(orangeWall, 0xFFCC7030);
        int[] wallOut = OcrTextCore.isolateOutlinedChatPixels(orangeWall, w, h);
        for (int px : wallOut) {
            if (px != 0xFFFFFFFF) throw new AssertionError("Uniform orange wall was not wiped clean.");
        }

        int[] outlinedText = new int[w * h];
        Arrays.fill(outlinedText, 0xFF887766);
        // 1px dark outline ring around bright purple letter core at center (7,7)
        for (int y = 5; y <= 9; y++) {
            for (int x = 5; x <= 9; x++) {
                outlinedText[y * w + x] = 0xFF1E1E22;
            }
        }
        outlinedText[7 * w + 7] = 0xFFD068FF;
        int[] textOut = OcrTextCore.isolateOutlinedChatPixels(outlinedText, w, h);
        int centerVal = textOut[7 * w + 7] & 0xFF;
        if (centerVal >= 120) {
            throw new AssertionError("Outlined purple text core was not isolated to dark ink: " + centerVal);
        }

        // Dedicated purple Devil Rift isolator: rejects uniform purple floor & cyan [Tips], isolates outlined purple text
        int pw = 24, ph = 24;
        int[] uniformPurple = new int[pw * ph];
        Arrays.fill(uniformPurple, 0xFF9C4BF5);
        if (OcrTextCore.isolatePurpleEventPixels(uniformPurple, pw, ph) != null) {
            throw new AssertionError("Uniform neon purple floor must not trigger purple event pass.");
        }
        int[] outlinedPurpleBlock = new int[pw * ph];
        Arrays.fill(outlinedPurpleBlock, 0xFF1A1A20);
        for (int y = 6; y < 14; y++) {
            for (int x = 4; x < 16; x++) {
                outlinedPurpleBlock[y * pw + x] = 0xFF9C4BF5;
            }
        }
        // Add cyan [Tips] pixels below: must be wiped to pure white
        for (int y = 17; y < 21; y++) {
            for (int x = 4; x < 16; x++) {
                outlinedPurpleBlock[y * pw + x] = 0xFF4CC4F0;
            }
        }
        int[] purpleOut = OcrTextCore.isolatePurpleEventPixels(outlinedPurpleBlock, pw, ph);
        if (purpleOut == null || (purpleOut[7 * pw + 5] & 0xFF) >= 120 || purpleOut[18 * pw + 8] != 0xFFFFFFFF) {
            throw new AssertionError("Purple Devil Rift isolator failed to keep purple strokes or wipe cyan [Tips].");
        }

        // Priority Accent (Yellow/Green/Red) isolator: rejects uniform yellow wall & purple/cyan-only crops,
        // keeps outlined Yellow (Spirit Rift), Green (Wind Wisp), Red (Blood Wisp), wipes purple & cyan to white.
        int[] uniformYellow = new int[pw * ph];
        Arrays.fill(uniformYellow, 0xFFE5D850);
        if (OcrTextCore.isolatePriorityAccentPixels(uniformYellow, pw, ph) != null) {
            throw new AssertionError("Uniform yellow wall must not trigger Priority Accent pass.");
        }
        if (OcrTextCore.isolatePriorityAccentPixels(outlinedPurpleBlock, pw, ph) != null) {
            throw new AssertionError("Purple + cyan only crop must not trigger Priority Accent pass.");
        }
        int aw = 28, ah = 28;
        int[] accentCrop = new int[aw * ah];
        Arrays.fill(accentCrop, 0xFF1A1A20);
        // Outlined Yellow row (Spirit Rift / Light Wisp)
        for (int y = 3; y <= 6; y++) for (int x = 3; x <= 14; x++) accentCrop[y * aw + x] = 0xFFE2E668;
        // Outlined Mint-Green row (Wind Wisp)
        for (int y = 9; y <= 12; y++) for (int x = 3; x <= 14; x++) accentCrop[y * aw + x] = 0xFF5EE0A8;
        // Outlined Red row (Blood Wisp / Leak)
        for (int y = 15; y <= 18; y++) for (int x = 3; x <= 14; x++) accentCrop[y * aw + x] = 0xFFEF5555;
        // Outlined Purple row (Mammoth — must be wiped to pure white!)
        for (int y = 21; y <= 24; y++) for (int x = 3; x <= 14; x++) accentCrop[y * aw + x] = 0xFF9C4BF5;
        int[] accentOut = OcrTextCore.isolatePriorityAccentPixels(accentCrop, aw, ah);
        if (accentOut == null
                || (accentOut[4 * aw + 5] & 0xFF) >= 120
                || (accentOut[10 * aw + 5] & 0xFF) >= 120
                || (accentOut[16 * aw + 5] & 0xFF) >= 120
                || accentOut[22 * aw + 5] != 0xFFFFFFFF) {
            throw new AssertionError("Priority Accent isolator failed to isolate Yellow/Green/Red or wipe Purple.");
        }

        // Full-frame Y coordinate cross-band overlap deduplication (top band originY=0, bottom band originY=480)
        int bandScale = 2;
        int bandPad = OcrTextCore.PAD_PX;
        List<OcrTextCore.RawLine> topBandRaw = Collections.singletonList(
            new OcrTextCore.RawLine(bandPad + 10 * bandScale, bandPad + 495 * bandScale,
                bandPad + 220 * bandScale, bandPad + 509 * bandScale,
                "[Event] A strange energy is felt throughout the kingdom", Collections.<OcrTextCore.RawWord>emptyList()));
        List<OcrTextCore.RawLine> botBandRaw = Collections.singletonList(
            new OcrTextCore.RawLine(bandPad + 10 * bandScale, bandPad + 15 * bandScale,
                bandPad + 220 * bandScale, bandPad + 29 * bandScale,
                "[Event] A strange energy is felt throughout the kingdom", Collections.<OcrTextCore.RawWord>emptyList()));
        List<OcrTextCore.PositionedLine> topPos = OcrTextCore.filterAndNormalizeLines(topBandRaw, "top:p1", bandScale, bandPad, 600, 0);
        List<OcrTextCore.PositionedLine> botPos = OcrTextCore.filterAndNormalizeLines(botBandRaw, "bot:p1", bandScale, bandPad, 600, 480);
        List<OcrTextCore.PositionedLine> mergedBands = OcrTextCore.mergePasses(topPos, botPos);
        if (mergedBands.size() != 1) {
            throw new AssertionError("Cross-band overlap line was duplicated! count=" + mergedBands.size());
        }

        // Geometric HUD slicing + right-shifted HUD removal + Y-band quality merge
        int scale = 3;
        int pad = OcrTextCore.PAD_PX;
        List<OcrTextCore.RawWord> wordsWithHud = Arrays.asList(
            new OcrTextCore.RawWord(pad + 10 * scale, pad + 20 * scale, pad + 60 * scale, pad + 32 * scale, "[Event]"),
            new OcrTextCore.RawWord(pad + 64 * scale, pad + 20 * scale, pad + 120 * scale, pad + 32 * scale, "Corrupted"),
            new OcrTextCore.RawWord(pad + 124 * scale, pad + 20 * scale, pad + 170 * scale, pad + 32 * scale, "Night"),
            new OcrTextCore.RawWord(pad + 290 * scale, pad + 20 * scale, pad + 340 * scale, pad + 32 * scale, "4900$")
        );
        List<OcrTextCore.RawLine> p1Raw = Arrays.asList(
            new OcrTextCore.RawLine(pad + 10 * scale, pad + 20 * scale, pad + 340 * scale, pad + 32 * scale,
                "[Event] Corrupted Night 4900$", wordsWithHud),
            new OcrTextCore.RawLine(pad + 220 * scale, pad + 80 * scale, pad + 310 * scale, pad + 92 * scale,
                "Cooking Sheep", Collections.<OcrTextCore.RawWord>emptyList())
        );
        List<OcrTextCore.PositionedLine> p1 = OcrTextCore.filterAndNormalizeLines(p1Raw, "p1", scale, pad, 360);
        if (p1.size() != 1 || p1.get(0).text.contains("4900")) {
            throw new AssertionError("HUD filtering failed: " + (p1.isEmpty() ? "empty" : p1.get(0).text));
        }
        List<OcrTextCore.PositionedLine> p2 = Arrays.asList(
            new OcrTextCore.PositionedLine(26.0, 10.0, 170.0, 12.0, "[Event] C0rrupt!d N!ght", "p2"),
            new OcrTextCore.PositionedLine(42.0, 10.0, 210.0, 12.0, "[Leak] Something is leaking in the grasslands...", "p2")
        );
        List<OcrTextCore.PositionedLine> merged = OcrTextCore.mergePasses(p1, p2);
        if (merged.size() != 2 || !merged.get(0).text.equals("[Event] Corrupted Night")
                || !merged.get(1).text.contains("leaking")) {
            throw new AssertionError("Multi-pass Y-band merge failed: " + merged.size());
        }
    }
}

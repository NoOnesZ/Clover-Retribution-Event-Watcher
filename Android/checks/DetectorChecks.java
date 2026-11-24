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

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
        System.out.println("Android detector checks passed.");
    }
}

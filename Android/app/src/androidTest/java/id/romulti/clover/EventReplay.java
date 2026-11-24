package id.romulti.clover;

import java.util.Arrays;
import java.util.List;

final class EventReplay {
    private static final String TIP = "[Tips] Press semi-colon to open the map.";
    private static final String WARLORD = "[Event] The Warlord of the Grasslands steps forward, challenge in his eyes.";
    private static final String TYPO = "[Event] The Wariord of the Grasslands steps forward, challenge in bis eyes.";
    private static final String WISP = "[Event] Mana fluttering can be heard across the grasslands.";
    private static final String OTHER = "[Event] A silver comet passes over the eastern mountains.";

    public static void main(String[] args) {
        verify();
        System.out.println("Android OCR event replay: PASS");
    }

    static void verify() {
        verifyCatalogTypos();
        verifyStarBroadcasts();
        verifyLongStars();
        verifyMutedGiantDemon();
        verifyMutedVampireCastle();
        verifyMutedRaidsAndWerewolf();
        verifyPriorityWispsAndSpiritRift();
        verifyLowResTagsAndNoise();
        EventHistory history = new EventHistory();
        expect(history, 0, "", TIP);
        expect(history, 3, "warlord", TIP, WARLORD);
        expect(history, 6, "", TIP, TYPO);
        expect(history, 9, "mana_wisp", WISP);
        expect(history, 12, "", WISP);
        expect(history, 15, "", WISP);
        expect(history, 18, "", WISP);
        expect(history, 21, "", WISP);
        expect(history, 24, "", TYPO, WISP);
        expect(history, 27, "warlord", TYPO, WISP, WARLORD);
        expect(history, 30, "", TIP);
        expect(history, 33, "", TIP);
        expect(history, 180, "warlord", TIP, WARLORD);
        expect(history, 183, "", TIP, WARLORD, OTHER);
        expect(history, 186, "unknown_event", TIP, WARLORD, OTHER.replace("comet", "cornet"));
        expect(history, 189, "", TIP, WARLORD, OTHER);
        if (!EventMatcher.scan(Arrays.asList(TYPO + " It retreats."), true).events.isEmpty()
                || !EventMatcher.scan(Arrays.asList("Player: " + TYPO.substring(8)), true).events.isEmpty()
                || !EventMatcher.scan(Arrays.asList(TYPO), false).events.isEmpty()) {
            throw new AssertionError("Typo matching bypassed a suppression rule.");
        }
        EventHistory singleFrame = new EventHistory();
        expect(singleFrame, 0, "", TIP);
        expect(singleFrame, 3, "", TIP, OTHER);
        expect(singleFrame, 6, "", "Unlock chat");
        expect(singleFrame, 9, "", TIP, OTHER);
        expect(singleFrame, 12, "unknown_event", TIP, OTHER);
    }

    private static void verifyLongStars() {
        EventHistory history = new EventHistory();
        expect(history, 0, "", TIP);
        expect(history, 3, "star_5", "[Event]", "Over the distant horizon a sphere carrying",
            "five", "stars has appeared.", "[Tips] Other chat starts here.");
        expect(history, 6, "", "[Event] Over the distant horizon a sphere carrying five stars has appeared.");
        expect(history, 9, "star_6", "Six", "stars", "glow beyond the horizon",
            "and color the sky with an unusual light.", "Player: separate message");
        expect(history, 12, "", "Six stars glow beyond the horizon.");
        expect(history, 15, "", "[Event] A broadcast says there are", "", "seven", "stars in inventory");
        expect(history, 18, "star_7", "[Event] A bright sphere is visible in the distance through the clouds",
            "and marked with", "seven", "stars", "as it descends.");
    }

    private static void verifyStarBroadcasts() {
        EventHistory history = new EventHistory();
        expect(history, 0, "", TIP);
        expect(history, 3, "star_4", "[Event] Four stars glow, marking the corners of a journey fraught with adversaries, yet");
        expect(history, 6, "", "[Event] Four stars glow, marking the corne of a journey fraught with adversaries, yet");
        expect(history, 9, "", "[Event] Four stars gl of a journey fraught");
        expect(history, 12, "", "[Event] The corruption fades, and the night releases its grip. Order is restored, for no");
        expect(history, 15, "", "[Event] The corrupti releases its grip. Ord");
        expect(history, 18, "", "[Event] The corrupti releases its grip. Ord");
        expect(history, 21, "corrupted_night", "[Event] The air grows heavy as darkness takes hold.");
        expect(history, 24, "green_star", "[Event] A green star rises.");
        EventHistory mixed = new EventHistory();
        expect(mixed, 0, "", TIP);
        expect(mixed, 3, "star_4", "Four stars glow, marking the corners of a journey");
        expect(mixed, 6, "", "[Eveni] F0ur st4rs gl of a journey");
        expect(mixed, 9, "star_1", "A single", "star shines in the distance.");
        expect(mixed, 12, "star_7", "[Event] A sphere carrying seven stars has appeared.");
        expect(mixed, 15, "", "Player: Three stars glow in the distance.");
        expect(mixed, 18, "", "[Tips] Four stars glow in the distance.");
        expect(mixed, 21, "", "Four stars in inventory");
        expect(mixed, 24, "star_3", "[Event] In the distance, three stars shine.");
        String[] names = {"One", "Two", "Three", "Four", "Five", "Six", "Seven"};
        for (int i = 0; i < names.length; i++) {
            String suffix = i == 0 ? " star glows." : " stars glow.";
            String id = "star_" + (i + 1);
            EventHistory named = new EventHistory();
            expect(named, 0, "", TIP);
            expect(named, 3, id, "[Event] " + names[i] + suffix);
            expect(named, 6, "", (i + 1) + suffix);
        }
    }

    private static void verifyMutedGiantDemon() {
        String[] messages = {
            "[Event] The seal has shattered walks the land, and its gaze is",
            "[Event] The seal has shattere E1 walks the land, and its gaze is",
            "[Event] The sea1 has shatterd and the Giant Demon walks the land.",
            "[Event] The Devil AssauIt approaches."
        };
        for (String message : messages) {
            for (boolean includeCommon : new boolean[] {false, true}) {
                EventMatcher.Frame frame = EventMatcher.scan(Arrays.asList(message), includeCommon);
                if (!frame.readable || !frame.events.isEmpty())
                    throw new AssertionError("Giant Demon leaked into alerts: " + message);
            }
        }
    }

    private static void verifyMutedVampireCastle() {
        List<List<String>> cases = Arrays.asList(
            Arrays.asList("[Event] Stronger vampires can now be seen around the castle."),
            Arrays.asList("[Eveni] Stronqer vampires can now be seen around the castle."),
            Arrays.asList("[Event] The Elite of the vampires has been defeated."),
            Arrays.asList("[Event] The Elite of the vampires appears near the castle."),
            Arrays.asList(
                "[Event] A shadow deepens across the land as Vampire Lord Lucius awakens,",
                "bringing an ancient darkness with him."),
            Arrays.asList("[Event] A shadow deepens across the land as Vampire Lord"),
            Arrays.asList("[Event] Lucius falls, his dark reign fading into the shadows."),
            Arrays.asList("[Event] Lucius falls, his dark reign")
        );
        for (List<String> lines : cases) {
            for (boolean includeCommon : new boolean[] {false, true}) {
                EventMatcher.Frame frame = EventMatcher.scan(lines, includeCommon);
                if (!frame.readable || !frame.events.isEmpty()) {
                    throw new AssertionError("Vampire/Lucius event leaked into alerts: " + lines);
                }
            }
        }
    }

    private static void verifyMutedRaidsAndWerewolf() {
        List<List<String>> cases = Arrays.asList(
            Arrays.asList(
                "[System] You've exited a safezone.",
                "[Raid] The ember altar flares. Kaldris, the Embersworn, waits in the Ashen",
                "Crucible for up to 5 people nearby."),
            Arrays.asList("[Raid] Kaldris, the Embersworn, has been defeated. The crucible cools."),
            Arrays.asList("[Event] The ember altar flares. Kaldris, the Embersworn, waits in the Ashen Crucible."),
            Arrays.asList(
                "[Event] A fierce howl echoes as the Primal Werewolf emerges, radiating raw,",
                "untamed power."),
            Arrays.asList(
                "[Event] With a final roar, the Primal Werewolf collapses, his wild fury",
                "extinguished.")
        );
        for (List<String> lines : cases) {
            for (boolean includeCommon : new boolean[] {false, true}) {
                EventMatcher.Frame frame = EventMatcher.scan(lines, includeCommon);
                if (!frame.readable || !frame.events.isEmpty()) {
                    throw new AssertionError("Kaldris Raid or Primal Werewolf leaked into alerts: " + lines);
                }
            }
        }
    }

    private static void verifyPriorityWispsAndSpiritRift() {
        Object[][] cases = {
            { "[Event] A strange energy is felt throughout the kingdom", "spirit_rift", 0xF59E0B },
            { "[Event] A wind wisp stirs the leaves somewhere in", "wind_wisp", 0x22C55E },
            { "A wind wisp stirs the leaves somewhere in the grasslands", "wind_wisp", 0x22C55E },
            { "[Event] A blood wisp has appeared across the grasslands", "blood_wisp", 0xEF4444 },
            { "[Event] A light wisp has appeared across the grasslands", "light_wisp", 0xF59E0B }
        };
        for (Object[] sample : cases) {
            EventMatcher.Frame frame = EventMatcher.scan(Arrays.asList((String) sample[0]), false);
            if (frame.events.size() != 1) {
                throw new AssertionError("Expected 1 priority match for: " + sample[0] + ", got " + frame.events.size());
            }
            EventMatcher.Rule rule = frame.events.get(0).rule;
            if (!rule.id.equals(sample[1]) || !rule.priority || rule.color != ((Integer) sample[2]).intValue()) {
                throw new AssertionError("Priority/color mismatch for " + sample[1] + ": id=" + rule.id
                    + " priority=" + rule.priority + " color=" + Integer.toHexString(rule.color));
            }
        }
    }

    private static void verifyLowResTagsAndNoise() {
        EventMatcher.Frame typoTags = EventMatcher.scan(Arrays.asList(
            "[Eveni] The air grows heavy, and darkness takes hold as every creature is consumed by corruption.",
            "[Leek] Something is leaking in the grasslands...",
            "[Tips] You can turn shiftlock on and off with the Left Control key.",
            "[Event] zxq!mnb 2Shccp 3Culinary 9k"
        ), false);
        if (typoTags.events.size() != 2
                || !typoTags.events.get(0).rule.id.equals("corrupted_night")
                || !typoTags.events.get(1).rule.id.equals("leak_event")) {
            throw new AssertionError("Low-res tag typos or noise filter failed: " + typoTags.events.size());
        }
    }

    private static void verifyCatalogTypos() {
        String[][] cases = {
            { "devil_rift", "Somethlng feels off..." },
            { "spirit_rift", "A strange enerqy feels toward the kingdom." },
            { "khamsin", "[Event] The Sand Saqe has arrived." },
            { "void_wisp", "[Event] A Void Wi5p appears." },
            { "leak_event", "[Leak] Something is Ieaking in the grasslands." },
            { "blood_wisp", "[Event] A Blood Wi5p appears over the plains." },
            { "wind_wisp", "[Event] A Wind Wi5p stirs the leaves somewhere in the grasslands." },
            { "light_wisp", "[Event] A Light Wi5p appears over the plains." },
            { "strange_scientist", "[Event] The strange scientlst has arrived." },
            { "devil_rumors", "[Event] Someb0dy has seen a silhouette." },
            { "guarded_rift", "[Event] A guarded r1ft opens." },
            { "corrupted_night", "[Event] Corrupted Niqht is here." },
            { "green_star", "[Event] A Green Sfar rises." },
            { "divine_general", "[Event] The Divine GeneraI has arrived." },
            { "prime_apex", "[Event] The Prime Ap3x is here." },
            { "mana_wisp", "[Event] A Mana Wi5p appears." },
            { "terra", "[Event] The 8andit Lord is here." },
            { "warlord", "[Event] The Wariord of the Grasslands steps forward, challenge in bis eyes." },
            { "wolf_hunter", "[Event] Wandering Wolf Hunfer arrived." },
            { "unknown_event", "[Event] Devil Rlft and Spirit Rlft signs appear." },
        };
        for (String[] sample : cases) {
            List<EventMatcher.Detection> events = EventMatcher.scan(Arrays.asList(sample[1]), true).events;
            if (events.size() != 1 || !events.get(0).rule.id.equals(sample[0])) {
                String actual = events.isEmpty() ? "none" : events.get(0).rule.id;
                throw new AssertionError("Typo case " + sample[0] + " classified as " + actual + ": " + sample[1]);
            }
        }
    }

    private static void expect(EventHistory history, long seconds, String expected, String... lines) {
        List<EventMatcher.Detection> alerts = history.observe(EventMatcher.scan(Arrays.asList(lines), true), seconds * 1000);
        String actual = String.join(",", alerts.stream().map(alert -> alert.rule.id).toArray(String[]::new));
        if (!actual.equals(expected)) {
            throw new AssertionError("At " + seconds + "s expected [" + expected + "], got [" + actual + "]");
        }
    }
}

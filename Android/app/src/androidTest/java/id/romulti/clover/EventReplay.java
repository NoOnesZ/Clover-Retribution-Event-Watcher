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
        verifyTier3IntentAndGluedDespawns();
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

        // Timeline Memory regression (exact 08:17 -> 08:20 -> 08:21 scenario):
        // Old event flickers unreadable for 4 minutes (> 2 min window) while [Leak] arrives below it;
        // reappearing ABOVE [Leak] MUST NOT duplicate, while a real 2nd spawn at the bottom MUST alert!
        String khamsin = "[Event] The sands shift; Khamsin, the Sand Sage, emerges from the dunes.";
        String leak = "[Leak] Something is leaking in the grasslands...";
        EventHistory mem = new EventHistory();
        expect(mem, 0, "", TIP);
        expect(mem, 3, "khamsin", TIP, khamsin);
        expect(mem, 6, "", TIP);
        expect(mem, 9, "", TIP);
        expect(mem, 180, "leak_event", TIP, leak);
        expect(mem, 240, "", TIP, khamsin, leak);
        expect(mem, 246, "khamsin", TIP, khamsin, leak, khamsin);
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
        String[] verbs = {
            " star burns in the sky.",
            " stars align, forging alliances in the face of looming shadows.",
            " stars converge above the realm.",
            " stars mark the corners of a journey.",
            " stars unite across the horizon.",
            " stars awaken beyond the clouds.",
            " stars call out to destiny."
        };
        for (int i = 0; i < names.length; i++) {
            String suffix = i == 0 ? " star glows." : " stars glow.";
            String id = "star_" + (i + 1);
            EventHistory named = new EventHistory();
            expect(named, 0, "", TIP);
            expect(named, 3, id, "[Event] " + names[i] + suffix);
            expect(named, 6, "", (i + 1) + suffix);

            EventHistory poetic = new EventHistory();
            expect(poetic, 0, "", TIP);
            expect(poetic, 3, id, "[Event] " + names[i] + verbs[i]);
        }
        // Real Two Stars screenshot (100%, 75% (Cvcnt) + atizn, tag-less, glued, mid-sentence) + safety guards
        EventHistory twoStars = new EventHistory();
        expect(twoStars, 0, "", TIP);
        expect(twoStars, 3, "star_2",
            "[Event] Two stars align, forging alliances in the face of",
            "looming shadows, a bond stronger than fate.");
        expect(twoStars, 6, "",
            "(Cvcnt) Two stars atizn, forging attianccs in thc face of",
            "looming shadows, a bond stronger than fate.");
        expect(twoStars, 9, "",
            "Two stars align, forging alliances in the face of looming shadows.");
        EventHistory starEdge = new EventHistory();
        expect(starEdge, 0, "", TIP);
        expect(starEdge, 3, "star_2", "Two stars align, forging alliances in the face of looming shadows.");
        expect(starEdge, 6, "star_3", "[Event] Behold, threestars awaken in the distance!");
        expect(starEdge, 9, "star_1", "[Event] A lone star rises above the kingdom.");
        expect(starEdge, 12, "",
            "4 stars",
            "3 stars remaining",
            "Four stars in inventory",
            "[Event] In the clasp of a determined soul, the star finds its purpose, its light",
            "merging with a spirit aflame with purpose.",
            "[Event] merging with a spirit aflame with purpose.");
        expect(starEdge, 15, "", "[Event] Two stars and four stars appear together.");
        expect(starEdge, 18, "unknown_event", "[Event] Two stars and four stars appear together.");
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
                "extinguished."),
            Arrays.asList("[Event] A black-scaled giant crocodile has surfaced near the southern waters."),
            Arrays.asList("[Event] A black-backed mammoth is shaking the western ridge."),
            Arrays.asList(", [Event)'A black:backednlanunothis shakin western ridge.—")
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
            { "[Event] A light wisp has appeared across the grasslands", "light_wisp", 0xF59E0B },
            { "[Event] A radiant wisp drifts between the jungle canopy", "light_wisp", 0xF59E0B },
            { "[Event] A radiant wisp drifts across the kingdom", "light_wisp", 0xF59E0B },
            { "A radiant wisp drifts between the jungle canopy", "light_wisp", 0xF59E0B },
            { "A rad1ant w15p drifts across the grasslands", "light_wisp", 0xF59E0B },
            { "A radiantwisp drifts between the jungle canopy", "light_wisp", 0xF59E0B }
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

        // Ultra-low-res degraded Priority #1 Devil Rift OCR variants (down to 60% scale)
        String[] devilLowResVariants = {
            "[Event] Something feels off...",
            "[Eventl Something feels off...",
            "(Cvent) Something feeb off...",
            "(Cvent) Somethinz feetsoff...",
            "(Cvent) Something feets OH...",
            "(Cvent ) Somet hmc feels off..",
            "(Cvent) Somet hint off..."
        };
        for (String line : devilLowResVariants) {
            EventMatcher.Frame f = EventMatcher.scan(Arrays.asList(line), false);
            if (f.events.size() != 1 || !f.events.get(0).rule.id.equals("devil_rift") || !f.events.get(0).rule.priority) {
                throw new AssertionError("Low-res Devil Rift variant missed: " + line);
            }
        }
        // Safety: Leak / Devil Rumors / Tips must NEVER false-trigger devil_rift
        EventMatcher.Frame safeCheck = EventMatcher.scan(Arrays.asList(
            "[Leak] Something is leaking in the grasslands...",
            "[Event] Somebody has seen something they might not survive to tell, word has it they're in the Desert Village...",
            "[Tips] You can turn shiftlock on and off with the Left Control key."
        ), false);
        if (safeCheck.events.size() != 1 || !safeCheck.events.get(0).rule.id.equals("leak_event")) {
            throw new AssertionError("Devil Rift fuzzy matcher false-triggered on Leak/Rumors/Tips!");
        }
        // Fuzzy vanished / defeated / retreated / collapsed despawns must NEVER trigger even with includeCommon=true
        EventMatcher.Frame despawnCheck = EventMatcher.scan(Arrays.asList(
            "[Event] The Bandit Lord vanlshes, leaving onty whispers in",
            "h is wake.",
            "[Event] The Bandit Lord van1shed from the plains.",
            "[Event] Khamsin, the Sand Sage, has been defe4ted!",
            "[Event] Khamsin has been defeatcd!",
            "[Event] The Warlord reireats on the winds of the plains.",
            "[Event] A wind wisp vanlshes from the grasslands."
        ), true);
        if (!despawnCheck.events.isEmpty()) {
            throw new AssertionError("Fuzzy despawn line leaked into alerts! count=" + despawnCheck.events.size());
        }
    }

    private static void verifyTier3IntentAndGluedDespawns() {
        List<String> gluedDespawns = Arrays.asList(
            "[Event] the Alpha Mossback Mammoth hasfallen.",
            "[Event] The Alpha Mossback Mammoth hasfa1len.",
            "[Event] The Alpha Mossback Mammoth has fallen.",
            "[Event] Khamsin, the Sand Sage, has beendefeated.",
            "[Event] The Bandit Lord hasvanished.",
            "[Event] The Warlord has perished.",
            "[Event] The ancient beast was silenced.",
            "[Event] The Void Wisp was vanquished."
        );
        if (!EventMatcher.scan(gluedDespawns, true).events.isEmpty()) {
            throw new AssertionError("Glued/expanded despawns leaked on Android!");
        }
        String[] safeUnknownSpawns = {
            "[Event] The earth rumbles beneath the clover realm.",
            "[Event] An eerie silence falls over the clover realm.",
            "[Event] A muffled echo rises across the valley.",
            "[Event] An unsettled aura gathers across the valley.",
            "[Event] A new hero is leading the charge across the valley.",
            "[Event] Something is breaking free in the clover sky!"
        };
        for (String safeLine : safeUnknownSpawns) {
            List<EventMatcher.Detection> ev = EventMatcher.scan(Arrays.asList(safeLine), false).events;
            if (ev.size() != 1 || !ev.get(0).rule.id.equals("unknown_event")) {
                throw new AssertionError("Android valid spawn suppressed or misclassified: " + safeLine
                    + " -> " + (ev.isEmpty() ? "none" : ev.get(0).rule.id));
            }
        }
        if (EventMatcher.sameUnknownBroadcast(null, "anything") || EventMatcher.sameUnknownBroadcast("anything", null)) {
            throw new AssertionError("Android sameUnknownBroadcast null guard failed!");
        }
        String[][] intentCases = {
            { "spirit_rift", "[Event] A stranqe enerqy is felt throughout the klngdom" },
            { "spirit_rift", "A stranqe enerqy is felt throughout the klngdom" },
            { "wind_wisp", "[Event] A w1nd w15p stirs the Ieaves" },
            { "wind_wisp", "A windwisp stirs the leaves" },
            { "blood_wisp", "A bloodwisp has appeared across the grasslands" },
            { "light_wisp", "A lightwisp has appeared across the grasslands" },
            { "unknown_event", "[Event] Devil Rlft and felt throughout klngdom" }
        };
        for (String[] c : intentCases) {
            List<EventMatcher.Detection> ev = EventMatcher.scan(Arrays.asList(c[1]), false).events;
            if (ev.size() != 1 || !ev.get(0).rule.id.equals(c[0])) {
                throw new AssertionError("Android intent case expected " + c[0] + ", got "
                    + (ev.isEmpty() ? "none" : ev.get(0).rule.id) + " for: " + c[1]);
            }
        }
        // Ignored mossback + garbled mammoth must yield 0 events
        if (!EventMatcher.scan(Arrays.asList(
                "[Event) A black-backed tnanunoth is shaking the western ridge.",
                "[Event] An Alpha Mossback emerges in the jungle."), true).events.isEmpty()) {
            throw new AssertionError("Android Jungle Mossback/Mammoth leaked!");
        }
        if (!EventMatcher.scan(Arrays.asList("wind appeared over the hill", "energy in the air"), true).events.isEmpty()) {
            throw new AssertionError("Android tag-less noise false-triggered!");
        }
        if (EventMatcher.sameBroadcast(
                "a blood wisp has appeared across the grasslands",
                "a light wisp has appeared across the grasslands")
            || EventMatcher.sameBroadcast(
                "four stars glow marking the corners of a journey",
                "five stars glow marking the corners of a journey")) {
            throw new AssertionError("Android sameBroadcast collided distinct templates!");
        }
        if (!EventMatcher.sameUnknownBroadcast(
                "a silver cornet passes over the eastem mountalns",
                "a silwer comet passes ovcr the eastern mountains")) {
            throw new AssertionError("Android sameUnknownBroadcast failed on 2+ word low-res unknown variants!");
        }

        // Unknown 2-frame confirmation & dedupe with 2+ shifted low-res words via sameUnknownBroadcast
        EventHistory unkHistory = new EventHistory();
        expect(unkHistory, 0, "", "[Tips] Ready.");
        expect(unkHistory, 3, "", "[Tips] Ready.", "[Event] A silver cornet passes over the eastem mountalns.");
        expect(unkHistory, 6, "unknown_event", "[Tips] Ready.", "[Event] A silwer comet passes ovcr the eastern mountains.");
        expect(unkHistory, 9, "", "[Tips] Ready.", "[Event] A s1lver cornet passes over the eastem mountains.");

        // Blood Wisp followed immediately by Light Wisp MUST BOTH alert (zero cross-event bigram collision!)
        EventHistory wispHistory = new EventHistory();
        expect(wispHistory, 0, "", "[Tips] Ready.");
        expect(wispHistory, 3, "blood_wisp", "[Tips] Ready.", "[Event] A blood wisp has appeared across the grasslands.");
        expect(wispHistory, 6, "light_wisp", "[Tips] Ready.",
            "[Event] A blood wisp has appeared across the grasslands.",
            "[Event] A light wisp has appeared across the grasslands.");
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

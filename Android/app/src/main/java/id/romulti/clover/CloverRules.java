package id.romulti.clover;

// Rules ported from RoMulti's Windows EventCatalog.cs.
final class CloverRules {
    private CloverRules() {}

    static final EventMatcher.Rule GIANT_DEMON = new EventMatcher.Rule(
        "devil_assault", "Devil Assault (Giant Demon)", 0xEF4444, false, false,
        "giant devil|giant demon|devil assault|toward capital");

    static final EventMatcher.Rule VAMPIRE_CASTLE = new EventMatcher.Rule(
        "vampire_castle", "Vampire Castle / Lord Lucius", 0x78716C, false, false,
        "stronger vampires|stronger vampire|seen around castle|around castle|elite vampires|elite vampire|"
            + "shadow deepens|vampire lord|lord lucius|lucius awakens|lucius falls|ancient darkness|dark reign|"
            + "vampire|vampires|lucius");

    static final EventMatcher.Rule EMBER_RAID = new EventMatcher.Rule(
        "ember_raid", "Raid: Kaldris, the Embersworn", 0x78716C, false, false,
        "ember altar|kaldris|embersworn|ashen crucible|crucible cools");

    static final EventMatcher.Rule PRIMAL_WEREWOLF = new EventMatcher.Rule(
        "primal_werewolf", "Primal Werewolf", 0x78716C, false, false,
        "primal werewolf|fierce howl|howl echoes|werewolf emerges|untamed power|radiating raw|"
            + "final roar|werewolf collapses|wild fury|fury extinguished|werewolf");

    static boolean isAlwaysMuted(EventMatcher.Rule rule) {
        return rule == GIANT_DEMON || rule == VAMPIRE_CASTLE || rule == EMBER_RAID || rule == PRIMAL_WEREWOLF;
    }

    static final String[] STAR_COUNTS = {"one", "two", "three", "four", "five", "six", "seven"};
    static final EventMatcher.Rule[] STAR_EVENTS = createStarEvents();

    private static EventMatcher.Rule[] createStarEvents() {
        EventMatcher.Rule[] rules = new EventMatcher.Rule[STAR_COUNTS.length];
        for (int i = 0; i < rules.length; i++) {
            int count = i + 1;
            rules[i] = new EventMatcher.Rule("star_" + count, count + (count == 1 ? " Star" : " Stars"),
                0xEAB308, true, false, "");
        }
        return rules;
    }

    static final EventMatcher.Rule[] NAMED = {
        new EventMatcher.Rule("devil_rift", "Devil Rift", 0x9333EA, true, true,
            "something feels off|something feels|something off|devil rift|devil portal"),
        new EventMatcher.Rule("spirit_rift", "Spirit Rift", 0xF59E0B, true, true,
            "strange energy|felt throughout kingdom|energy throughout kingdom|energy felt kingdom|energy toward kingdom|energy kingdom|spirit rift|spirit portal"),
        new EventMatcher.Rule("wind_wisp", "Wind Wisp", 0x22C55E, true, true,
            "wind wisp|stirs leaves|wind stirs|wind appeared"),
        new EventMatcher.Rule("blood_wisp", "Blood Wisp", 0xEF4444, true, true,
            "blood wisp|blood appeared"),
        new EventMatcher.Rule("light_wisp", "Light Wisp", 0xF59E0B, true, true,
            "light wisp|light appeared"),
        new EventMatcher.Rule("spirit_wisp", "Spirit Wisp", 0x38BDF8, true, true,
            "spirit wisp"),
        new EventMatcher.Rule("khamsin", "Khamsin: The Sand Sage", 0xA855F7, true, false,
            "khamsin|sands shift|sand sage|emerges dunes"),
        new EventMatcher.Rule("void_wisp", "Void Wisp", 0x6366F1, true, false,
            "void wisp|leaking hell"),
        new EventMatcher.Rule("leak_event", "Leak Event (Bug Report Item)", 0xEF4444, true, false,
            "something leaking|leaking grasslands|leak leaking|is leaking|leakingin"),
        new EventMatcher.Rule("strange_scientist", "Strange Scientist", 0x14B8A6, true, false,
            "veil shimmers|lone figure emerges|strange scientist"),
        new EventMatcher.Rule("devil_rumors", "Desert Town: Devil Rumors", 0xDC2626, false, false,
            "rumors desert village|word desert village|desert village|seen something survive|seen something rumors|somebody seen|somebody something|somebody survive|sornebody seen|sornebod seen|sornebody sornetning|sornebody something|survive tell|somebod seen|somebo seen|somebod survive|rumors desert"),
        new EventMatcher.Rule("guarded_rift", "Guarded Rift", 0x8B5CF6, true, false,
            "guarded rift|shadow hand|shadowhand|rift shrouded|sinister mana|stands watch guarding|rift incursion"),
        GIANT_DEMON,
        VAMPIRE_CASTLE,
        EMBER_RAID,
        PRIMAL_WEREWOLF,
        new EventMatcher.Rule("corrupted_night", "Corrupted Night", 0x7C3AED, true, false,
            "air grows heavy|grows heavy|darkness takes hold|consumed corruption|creature corruption|corrupted night"),
        new EventMatcher.Rule("green_star", "A Green Star (Oath Ring)", 0x22C55E, true, false,
            "green star|green light sky|oath ring"),
        new EventMatcher.Rule("divine_general", "The Divine General", 0xF97316, true, false,
            "divine general|wheel turns"),
        new EventMatcher.Rule("prime_apex", "Prime Apex", 0xEC4899, true, false,
            "prime apex"),
        new EventMatcher.Rule("mana_wisp", "Mana Wisp", 0x60A5FA, false, false,
            "mana fluttering|mana flutte|mana grasslands|mana grassland|mana jungle|mana across|mana through|fluttering heard|fluttering grasslands|fluttering jungle|fluttering|heard grassland|heard jungle|beheard grassland|beheard jungle|mana wisp"),
        new EventMatcher.Rule("terra", "Terra: The Bandit Lord", 0x78716C, false, false,
            "whispers rise|bandit lord|bandit lords|band lord|lord emerg|whispers bandit|terra bandit|terra timeston"),
        new EventMatcher.Rule("warlord", "Grasslands Warlord", 0x78716C, false, false,
            "grasslands warlord|grassland warlord|spear menace|warlord|challenge his eyes"),
        new EventMatcher.Rule("wolf_hunter", "Wolf Hunter Craftsman", 0x78716C, false, false,
            "wolf hunter|craftsman")
    };

    static final EventMatcher.Rule UNKNOWN = new EventMatcher.Rule(
        "unknown_event", "Other [Event] Broadcast", 0x3B82F6, true, false, "");

    static final String[] DESPAWN = {
        "vanish", "eanish", "despawn", "disappear", "dissipat", "subsid",
        "fade", "fading", "defeat", "slain", "was killed", "has died",
        "has fallen", "falls to", "leaving only", "leaving ont", "vowing revenge", "misteriously",
        "mysteriously", "retreat", "depart", "banish", "cease", "collaps",
        "dissolv", "reced", "sinks back", "returns to the", "returned to", "left the",
        "has ended", "event ended", "has closed", "portal closed", "rift closed", "closes",
        "no longer", "calm returns", "peace returns", "overcome", "extinguished"
    };

    static final String[] TIP_PHRASES = {
        "teaming up", "award all the", "dealt 10", "deal 0 damage",
        "server events may happen", "happen every 15", "hidden requirements", "before you can learn",
        "chance to learn", "boot icon", "fast travel", "compass at the top",
        "top of your screen", "open the map", "semi colon", "open the menu",
        "grimoire equipped", "cast spells", "level up your magic", "gain spells",
        "unsheathe", "equipped weapon", "open the journal", "drop rates",
        "bring out your grimoire", "entered a safezone", "exited a safezone", "server initialized",
        "joined the experience", "turn shiftlock", "left control key"
    };
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace RoMulti
{
    public enum EventAlertLevel
    {
        Ping,
        Normal,
        Ignore
    }

    public sealed class EventRule
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Category { get; set; }
        public string Hint { get; set; }
        public int EmbedColor { get; set; }
        public EventAlertLevel DefaultLevel { get; set; }
        public bool IsCatchAll { get; set; }
        // Each inner array is an AND-group of tokens; matching ANY inner array triggers the rule
        public string[][] TokenGroups { get; set; }
    }

    public sealed class MatchedEvent
    {
        public EventRule Rule { get; set; }
        public EventAlertLevel EffectiveLevel { get; set; }
        public string RawLine { get; set; }
        public string DedupeKey { get; set; }
        public int TimelineIndex { get; set; }
        public List<int> SourceLineIndices { get; set; }

        public MatchedEvent()
        {
            SourceLineIndices = new List<int>();
        }
    }

    public sealed class RoMultiConfig
    {
        public string WebhookUrl { get; set; }
        public string PingText { get; set; }
        public string PrivateServerCode { get; set; }
        public int ScanIntervalSeconds { get; set; }
        public bool AttachScreenshot { get; set; }
        public bool AlertOnMapChange { get; set; }
        public bool BackgroundChatHover { get; set; }
        public bool WakeChatOnAntiAfk { get; set; }
        public bool WatchMainToo { get; set; }
        public bool AutoPeekEnabled { get; set; }
        public int AutoPeekIntervalSeconds { get; set; }
        public bool AntiAfkEnabled { get; set; }
        public bool ResetCharOnAntiAfk { get; set; }
        public bool AntiAfkMainWhenIdle { get; set; }
        public int AntiAfkIntervalMinutes { get; set; }
        public Dictionary<string, string> EventLevels { get; set; }

        public RoMultiConfig()
        {
            WebhookUrl = "";
            PingText = "@everyone";
            PrivateServerCode = "";
            ScanIntervalSeconds = 3;
            AttachScreenshot = true;
            AlertOnMapChange = true;
            BackgroundChatHover = true;
            WakeChatOnAntiAfk = true;
            WatchMainToo = true;
            AutoPeekEnabled = true;
            AutoPeekIntervalSeconds = 45;
            AntiAfkEnabled = false;
            AntiAfkIntervalMinutes = 7;
            EventLevels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public static string GetDefaultPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, "romulti-config.json");
        }

        public static RoMultiConfig Load(string path = null)
        {
            string p = path ?? GetDefaultPath();
            var cfg = new RoMultiConfig();
            try
            {
                if (!File.Exists(p)) return cfg;
                string raw = File.ReadAllText(p, Encoding.UTF8);
                var ser = new JavaScriptSerializer();
                var loaded = ser.Deserialize<RoMultiConfig>(raw);
                if (loaded != null)
                {
                    cfg = loaded;
                    if (cfg.EventLevels == null)
                        cfg.EventLevels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (cfg.ScanIntervalSeconds < 2 || cfg.ScanIntervalSeconds > 60)
                        cfg.ScanIntervalSeconds = 3;
                    if (cfg.AntiAfkIntervalMinutes < 3 || cfg.AntiAfkIntervalMinutes > 18)
                        cfg.AntiAfkIntervalMinutes = 7;
                    if (cfg.AutoPeekIntervalSeconds < 15 || cfg.AutoPeekIntervalSeconds > 300)
                        cfg.AutoPeekIntervalSeconds = 45;
                    if (string.IsNullOrWhiteSpace(cfg.PingText))
                        cfg.PingText = "@everyone";
                    // Upgrade older configs that did not have new chat-wake keys yet
                    if (raw.IndexOf("BackgroundChatHover", StringComparison.OrdinalIgnoreCase) < 0)
                        cfg.BackgroundChatHover = true;
                    if (raw.IndexOf("WakeChatOnAntiAfk", StringComparison.OrdinalIgnoreCase) < 0)
                        cfg.WakeChatOnAntiAfk = true;
                    if (raw.IndexOf("WatchMainToo", StringComparison.OrdinalIgnoreCase) < 0)
                        cfg.WatchMainToo = true;
                    if (raw.IndexOf("AutoPeekEnabled", StringComparison.OrdinalIgnoreCase) < 0)
                        cfg.AutoPeekEnabled = true;
                }
            }
            catch
            {
                // Corrupt config falls back cleanly to defaults
            }
            return cfg;
        }

        public void Save(string path = null)
        {
            string destination = Path.GetFullPath(path ?? GetDefaultPath());
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var ser = new JavaScriptSerializer();
            try
            {
                File.WriteAllText(temporary, ser.Serialize(this), Encoding.UTF8);
                if (File.Exists(destination))
                    File.Replace(temporary, destination, null);
                else
                    File.Move(temporary, destination);
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        public EventAlertLevel GetEffectiveLevel(EventRule rule)
        {
            string val;
            if (EventLevels != null && EventLevels.TryGetValue(rule.Id, out val))
            {
                EventAlertLevel parsed;
                if (Enum.TryParse<EventAlertLevel>(val, true, out parsed))
                    return parsed;
            }
            return rule.DefaultLevel;
        }

        public void SetLevel(string ruleId, EventAlertLevel level)
        {
            if (EventLevels == null)
                EventLevels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            EventLevels[ruleId] = level.ToString();
        }
    }

    public static class EventCatalog
    {
        private static readonly string[] StarCounts = { "one", "two", "three", "four", "five", "six", "seven" };
        private static readonly Regex GiantSeal = new Regex(
            @"^(?:event )?(?:the )?se[a4][l1i] (?:ha[s5] )?[s5]ha[t7l1]{2}[e3]r[a-z0-9]{0,5}(?: |$)",
            RegexOptions.Compiled);
        private static readonly Regex DevilRiftGluedFeelsOff = new Regex(
            @"\bf[e3c][e3c][ltib1dr][s5z]?(?:[o0][f1t]{1,2}|oh)\b",
            RegexOptions.Compiled);
        private static readonly Regex DevilRiftFuzzyFeels = new Regex(
            @"\bf[e3c][e3c][ltib1d][s5z]?\b",
            RegexOptions.Compiled);
        private static readonly Regex DevilRiftSplitSomething = new Regex(
            @"\b(?:s[o0]m[e3]|s[o0]rn[e3]|s[o0]rr[e3])t\s*h[a-z0-9]{1,5}\b",
            RegexOptions.Compiled);
        private static readonly Regex DevilRiftOffWord = new Regex(
            @"\b(?:[o0][f1t]{2}|oh)\b",
            RegexOptions.Compiled);
        private static readonly Regex StarPrefixRegex = new Regex(
            @"^(?:(?:[ectfk][vw][ec](?:nt|ni|nf|nl|n7|m|rn|nto|nt0)|event[o0]?|even[i1f]|the|a)\s+)+",
            RegexOptions.Compiled);
        private static readonly Regex StarCountPairRegex = new Regex(
            @"\b([a-z0-9]{1,7})\s+st[a4][rl1][s5]?\b",
            RegexOptions.Compiled);
        private static readonly Regex StarGluedPairRegex = new Regex(
            @"\b(one|two|three|four|five|six|seven|single|lone|[1-7])st[a4][rl1][s5]?\b",
            RegexOptions.Compiled);
        private static readonly Regex StarUiRejectRegex = new Regex(
            @"\b(?:inventory|remaining|left|needed|collected|total|out of)\b",
            RegexOptions.Compiled);
        private static readonly EventRule[] StarRules = Enumerable.Range(1, StarCounts.Length).Select(count => new EventRule
        {
            Id = "star_" + count,
            DisplayName = count + (count == 1 ? " Star (Dragon Ball)" : " Stars (Dragon Ball)"),
            Category = "Stars",
            Hint = "One/Two/Three/Four/Five/Six/Seven Star(s) broadcast",
            EmbedColor = 0xEAB308,
            DefaultLevel = EventAlertLevel.Normal
        }).ToArray();

        public static readonly List<EventRule> Rules = new List<EventRule>
        {
            // --- PRIORITY #1 & #2 (User's Top Targets) ---
            new EventRule
            {
                Id = "devil_rift",
                DisplayName = "Devil Rift",
                Category = "Rift (Priority)",
                Hint = "Something feels off... (Purple)",
                EmbedColor = 0x9333EA,
                DefaultLevel = EventAlertLevel.Ping,
                TokenGroups = new[]
                {
                    new[] { "something", "feels", "off" },
                    new[] { "something", "feels" },
                    new[] { "something", "off" },
                    new[] { "devil", "rift" },
                    new[] { "devil", "portal" }
                }
            },
            new EventRule
            {
                Id = "spirit_rift",
                DisplayName = "Spirit Rift",
                Category = "Rift (Priority)",
                Hint = "A strange energy is felt throughout the kingdom (Yellow/Gold)",
                EmbedColor = 0xF59E0B,
                DefaultLevel = EventAlertLevel.Ping,
                TokenGroups = new[]
                {
                    new[] { "strange", "energy" },
                    new[] { "felt", "throughout", "kingdom" },
                    new[] { "energy", "throughout", "kingdom" },
                    new[] { "energy", "felt", "kingdom" },
                    new[] { "energy", "toward", "kingdom" },
                    new[] { "energy", "kingdom" },
                    new[] { "spirit", "rift" },
                    new[] { "spirit", "portal" }
                }
            },
            new EventRule
            {
                Id = "wind_wisp",
                DisplayName = "Wind Wisp",
                Category = "Rare Wisp (Priority)",
                Hint = "A wind wisp stirs the leaves somewhere in... (Green)",
                EmbedColor = 0x22C55E,
                DefaultLevel = EventAlertLevel.Ping,
                TokenGroups = new[]
                {
                    new[] { "wind", "wisp" },
                    new[] { "stirs", "leaves" },
                    new[] { "wind", "stirs" },
                    new[] { "wind", "appeared" }
                }
            },
            new EventRule
            {
                Id = "blood_wisp",
                DisplayName = "Blood Wisp",
                Category = "Rare Wisp (Priority)",
                Hint = "A blood wisp has appeared across the grasslands (Red)",
                EmbedColor = 0xEF4444,
                DefaultLevel = EventAlertLevel.Ping,
                TokenGroups = new[]
                {
                    new[] { "blood", "wisp" },
                    new[] { "blood", "appeared" }
                }
            },
            new EventRule
            {
                Id = "light_wisp",
                DisplayName = "Radiant Wisp (Light Wisp)",
                Category = "Rare Wisp (Priority)",
                Hint = "A radiant wisp drifts between the jungle canopy (Yellow/Gold)",
                EmbedColor = 0xF59E0B,
                DefaultLevel = EventAlertLevel.Ping,
                TokenGroups = new[]
                {
                    new[] { "radiant", "wisp" },
                    new[] { "radiant", "drifts" },
                    new[] { "radiant", "appeared" },
                    new[] { "drifts", "canopy" },
                    new[] { "light", "wisp" },
                    new[] { "light", "appeared" }
                }
            },
            new EventRule
            {
                Id = "spirit_wisp",
                DisplayName = "Spirit Wisp",
                Category = "Rare Wisp (Priority)",
                Hint = "Generic Spirit Wisp broadcast",
                EmbedColor = 0x38BDF8,
                DefaultLevel = EventAlertLevel.Ping,
                TokenGroups = new[]
                {
                    new[] { "spirit", "wisp" }
                }
            },

            // --- Rare / Important Events (Default: Normal Notification) ---
            new EventRule
            {
                Id = "khamsin",
                DisplayName = "Khamsin: The Sand Sage",
                Category = "Desert Boss",
                Hint = "The sands shift; Khamsin, the Sand Sage, emerges from the dunes.",
                EmbedColor = 0xA855F7,
                DefaultLevel = EventAlertLevel.Normal,
                TokenGroups = new[]
                {
                    new[] { "khamsin" },
                    new[] { "sands", "shift" },
                    new[] { "sand", "sage" },
                    new[] { "emerges", "dunes" }
                }
            },
            new EventRule
            {
                Id = "void_wisp",
                DisplayName = "Void Wisp",
                Category = "Ultra Rare Wisp",
                Hint = "Void Wisp (Clover Kingdom & Jungle)",
                EmbedColor = 0x6366F1,
                DefaultLevel = EventAlertLevel.Normal,
                TokenGroups = new[]
                {
                    new[] { "void", "wisp" },
                    new[] { "leaking", "hell" }
                }
            },
            new EventRule
            {
                Id = "leak_event",
                DisplayName = "Leak Event (Bug Report Item)",
                Category = "Special Event",
                Hint = "[Leak] Something is leaking in the grasslands...",
                EmbedColor = 0xEF4444,
                DefaultLevel = EventAlertLevel.Normal,
                TokenGroups = new[]
                {
                    new[] { "something", "leaking" },
                    new[] { "something", "leakin" },
                    new[] { "somethin", "leakin" },
                    new[] { "leaking", "grasslands" },
                    new[] { "leakin", "grasslands" },
                    new[] { "leak", "leaking" },
                    new[] { "leak", "leakin" },
                    new[] { "is", "leaking" },
                    new[] { "leakingin" },
                    new[] { "isleakin" }
                }
            },
            new EventRule
            {
                Id = "strange_scientist",
                DisplayName = "Strange Scientist",
                Category = "Rare NPC",
                Hint = "The veil shimmers and a lone figure emerges.",
                EmbedColor = 0x14B8A6,
                DefaultLevel = EventAlertLevel.Normal,
                TokenGroups = new[]
                {
                    new[] { "veil", "shimmers" },
                    new[] { "lone", "figure", "emerges" },
                    new[] { "strange", "scientist" }
                }
            },
            new EventRule
            {
                Id = "devil_rumors",
                DisplayName = "Desert Town: Devil Rumors",
                Category = "Desert Event",
                Hint = "Somebody has seen something they might not survive to tell, word has it they're in the Desert Village...",
                EmbedColor = 0xDC2626,
                DefaultLevel = EventAlertLevel.Ignore,
                TokenGroups = new[]
                {
                    new[] { "rumors", "desert", "village" },
                    new[] { "word", "desert", "village" },
                    new[] { "desert", "village" },
                    new[] { "seen", "something", "survive" },
                    new[] { "seen", "something", "rumors" },
                    new[] { "somebody", "seen" },
                    new[] { "somebody", "something" },
                    new[] { "somebody", "survive" },
                    new[] { "sornebody", "seen" },
                    new[] { "sornebod", "seen" },
                    new[] { "sornebody", "sornetning" },
                    new[] { "sornebody", "something" },
                    new[] { "survive", "tell" },
                    new[] { "somebod", "seen" },
                    new[] { "somebo", "seen" },
                    new[] { "somebod", "survive" },
                    new[] { "rumors", "desert" }
                }
            },
            new EventRule
            {
                Id = "guarded_rift",
                DisplayName = "Guarded Rift",
                Category = "Unique World Event",
                Hint = "A rift shrouded in sinister mana tears open — the Shadowhand stands watch, guarding what lies beyond.",
                EmbedColor = 0x8B5CF6,
                DefaultLevel = EventAlertLevel.Normal,
                TokenGroups = new[]
                {
                    new[] { "guarded", "rift" },
                    new[] { "shadow", "hand" },
                    new[] { "shadowhand" },
                    new[] { "rift", "shrouded" },
                    new[] { "sinister", "mana" },
                    new[] { "stands", "watch", "guarding" },
                    new[] { "rift", "incursion" }
                }
            },
            new EventRule
            {
                Id = "devil_assault",
                DisplayName = "Devil Assault (Giant Demon)",
                Category = "Unique World Event",
                Hint = "Seal shattered / Giant Demon marching toward Capital",
                EmbedColor = 0xEF4444,
                DefaultLevel = EventAlertLevel.Ignore,
                TokenGroups = new[]
                {
                    new[] { "giant", "devil" },
                    new[] { "giant", "demon" },
                    new[] { "devil", "assault" },
                    new[] { "toward", "capital" }
                }
            },
            new EventRule
            {
                Id = "corrupted_night",
                DisplayName = "Corrupted Night",
                Category = "Unique World Event",
                Hint = "The air grows heavy, and darkness takes hold as every creature is consumed by corruption.",
                EmbedColor = 0x7C3AED,
                DefaultLevel = EventAlertLevel.Normal,
                TokenGroups = new[]
                {
                    new[] { "air", "grows", "heavy" },
                    new[] { "grows", "heavy" },
                    new[] { "darkness", "takes", "hold" },
                    new[] { "consumed", "corruption" },
                    new[] { "creature", "corruption" },
                    new[] { "corrupted", "night" }
                }
            },
            new EventRule
            {
                Id = "green_star",
                DisplayName = "A Green Star (Oath Ring)",
                Category = "Ultra Rare RNG",
                Hint = "Green light crossing the sky",
                EmbedColor = 0x22C55E,
                DefaultLevel = EventAlertLevel.Normal,
                TokenGroups = new[]
                {
                    new[] { "green", "star" },
                    new[] { "green", "light", "sky" },
                    new[] { "oath", "ring" }
                }
            },
            new EventRule
            {
                Id = "divine_general",
                DisplayName = "The Divine General",
                Category = "World Boss",
                Hint = "World Boss (hourly XX:00 UTC)",
                EmbedColor = 0xF97316,
                DefaultLevel = EventAlertLevel.Normal,
                TokenGroups = new[]
                {
                    new[] { "divine", "general" },
                    new[] { "wheel", "turns" }
                }
            },
            new EventRule
            {
                Id = "prime_apex",
                DisplayName = "Prime Apex",
                Category = "World Boss",
                Hint = "World Boss (hourly XX:00 UTC)",
                EmbedColor = 0xEC4899,
                DefaultLevel = EventAlertLevel.Normal,
                TokenGroups = new[]
                {
                    new[] { "prime", "apex" }
                }
            },

            // --- Common / Low Priority (Default: Ignore to prevent spam) ---
            new EventRule
            {
                Id = "mana_wisp",
                DisplayName = "Mana Wisp",
                Category = "Grassland / Jungle (Common)",
                Hint = "Mana fluttering can be heard across the grasslands / through the jungle",
                EmbedColor = 0x60A5FA,
                DefaultLevel = EventAlertLevel.Ignore,
                TokenGroups = new[]
                {
                    new[] { "mana", "fluttering" },
                    new[] { "mana", "flutte" },
                    new[] { "mana", "grasslands" },
                    new[] { "mana", "grassland" },
                    new[] { "mana", "jungle" },
                    new[] { "mana", "across" },
                    new[] { "mana", "through" },
                    new[] { "fluttering", "heard" },
                    new[] { "fluttering", "grasslands" },
                    new[] { "fluttering", "jungle" },
                    new[] { "fluttering" },
                    new[] { "heard", "grassland" },
                    new[] { "heard", "jungle" },
                    new[] { "beheard", "grassland" },
                    new[] { "beheard", "jungle" },
                    new[] { "mana", "wisp" }
                }
            },
            new EventRule
            {
                Id = "terra",
                DisplayName = "Terra: The Bandit Lord",
                Category = "Grassland Boss",
                Hint = "Whispers rise; One of the Bandit Lords emerges from the shadows.",
                EmbedColor = 0x78716C,
                DefaultLevel = EventAlertLevel.Ignore,
                TokenGroups = new[]
                {
                    new[] { "whispers", "rise" },
                    new[] { "bandit", "lord" },
                    new[] { "bandit", "lords" },
                    new[] { "band", "lord" },
                    new[] { "lord", "emerg" },
                    new[] { "whispers", "bandit" },
                    new[] { "terra", "bandit" },
                    new[] { "terra", "timeston" }
                }
            },
            new EventRule
            {
                Id = "warlord",
                DisplayName = "Grasslands Warlord",
                Category = "Grassland Boss",
                Hint = "Spear Menace / Grasslands Warlord (challenge in his eyes)",
                EmbedColor = 0x78716C,
                DefaultLevel = EventAlertLevel.Ignore,
                TokenGroups = new[]
                {
                    new[] { "grasslands", "warlord" },
                    new[] { "grassland", "warlord" },
                    new[] { "spear", "menace" },
                    new[] { "warlord" },
                    new[] { "challenge", "his", "eyes" }
                }
            },
            new EventRule
            {
                Id = "wolf_hunter",
                DisplayName = "Wolf Hunter Craftsman",
                Category = "Grassland NPC",
                Hint = "Wandering Wolf Hunter Craftsman",
                EmbedColor = 0x78716C,
                DefaultLevel = EventAlertLevel.Ignore,
                TokenGroups = new[]
                {
                    new[] { "wolf", "hunter" },
                    new[] { "craftsman" }
                }
            },
            new EventRule
            {
                Id = "vampire_castle",
                DisplayName = "Vampire Castle / Lord Lucius",
                Category = "Castle Event (Ignored)",
                Hint = "Stronger vampires, Elite vampire, Shadow deepens, Vampire Lord Lucius",
                EmbedColor = 0x78716C,
                DefaultLevel = EventAlertLevel.Ignore,
                TokenGroups = new[]
                {
                    new[] { "stronger", "vampires" },
                    new[] { "stronger", "vampire" },
                    new[] { "seen", "around", "castle" },
                    new[] { "around", "castle" },
                    new[] { "elite", "vampires" },
                    new[] { "elite", "vampire" },
                    new[] { "shadow", "deepens" },
                    new[] { "vampire", "lord" },
                    new[] { "lord", "lucius" },
                    new[] { "lucius", "awakens" },
                    new[] { "lucius", "falls" },
                    new[] { "ancient", "darkness" },
                    new[] { "dark", "reign" },
                    new[] { "vampires" },
                    new[] { "vampire" },
                    new[] { "lucius" }
                }
            },
            new EventRule
            {
                Id = "ember_raid",
                DisplayName = "Raid: Kaldris, the Embersworn",
                Category = "Raid (Ignored)",
                Hint = "[Raid] Kaldris, the Embersworn (Ashen Crucible)",
                EmbedColor = 0x78716C,
                DefaultLevel = EventAlertLevel.Ignore,
                TokenGroups = new[]
                {
                    new[] { "ember", "altar" },
                    new[] { "kaldris" },
                    new[] { "embersworn" },
                    new[] { "ashen", "crucible" },
                    new[] { "crucible", "cools" }
                }
            },
            new EventRule
            {
                Id = "primal_werewolf",
                DisplayName = "Primal Werewolf",
                Category = "Castle Event (Ignored)",
                Hint = "A fierce howl echoes as the Primal Werewolf emerges / collapses",
                EmbedColor = 0x78716C,
                DefaultLevel = EventAlertLevel.Ignore,
                TokenGroups = new[]
                {
                    new[] { "primal", "werewolf" },
                    new[] { "fierce", "howl" },
                    new[] { "howl", "echoes" },
                    new[] { "werewolf", "emerges" },
                    new[] { "untamed", "power" },
                    new[] { "radiating", "raw" },
                    new[] { "final", "roar" },
                    new[] { "werewolf", "collapses" },
                    new[] { "wild", "fury" },
                    new[] { "fury", "extinguished" },
                    new[] { "werewolf" }
                }
            },
            new EventRule
            {
                Id = "jungle_bosses",
                DisplayName = "Jungle Bosses (Crocodile / Mammoth)",
                Category = "Jungle Boss (Ignored)",
                Hint = "Black-scaled giant crocodile (southern waters) & Black-backed mammoth (western ridge)",
                EmbedColor = 0x78716C,
                DefaultLevel = EventAlertLevel.Ignore,
                TokenGroups = new[]
                {
                    new[] { "crocodile" },
                    new[] { "black", "scaled" },
                    new[] { "surfaced", "southern" },
                    new[] { "southern", "waters" },
                    new[] { "mammoth" },
                    new[] { "mossback" },
                    new[] { "alpha", "mossback" },
                    new[] { "black", "backed" },
                    new[] { "shaking", "western" },
                    new[] { "shakin", "western" },
                    new[] { "western", "ridge" }
                }
            },

            // --- Catch-All for any unrecognized [Event] broadcast ---
            new EventRule
            {
                Id = "unknown_event",
                DisplayName = "Other [Event] Broadcast",
                Category = "Catch-All",
                Hint = "Any unrecognized [Event] chat line",
                EmbedColor = 0x3B82F6,
                DefaultLevel = EventAlertLevel.Normal,
                IsCatchAll = true,
                TokenGroups = new string[0][]
            }
        };

        static EventCatalog()
        {
            Rules.InsertRange(Rules.Count - 1, StarRules);
        }

        private static readonly Regex NonAlphaNumRegex = new Regex(@"[^a-z0-9\s]", RegexOptions.Compiled);
        private static readonly Regex MultiSpaceRegex = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex EventTagRegex = new Regex(@"(^|\s)(?:[\[\(\.:',;\|]\s*(?:[ectfk][vw][ec](?:nt|ni|nf|nl|n7|m|rn)|l[e3][a4o]k|leek)|(?:e[vw]e(?:nt|ni|nf|nl|n7|m|rn)|l[e3][a4o]k|leek))([o0l]?\s*[\]\)\|':]|[o0l](?=\s)|(?=\s+[A-Za-z]))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ExplicitLeakTagRegex = new Regex(@"^\s*(?:[\[\(\.:',;\|]\s*(?:l[e3][a4o]k|leek)(?:[o0l]?\s*[\]\)\|':]|(?=\s+[A-Za-z]))|(?:l[e3][a4o]k|leek)[o0l]?\s*[\]\)\|'])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NonEventPrefixRegex = new Regex(@"^\s*[\[\(\.:',;\|]?\s*(tips?|system|clover\s+system|raid)(\s*[\]\)\|':]|(?=\s+[A-Za-z]))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AnyChatTagStartRegex = new Regex(@"^\s*(?:[\[\(\.:',;\|]\s*(?:[ectfk][vw][ec](?:nt|ni|nf|nl|n7|m|rn)|l[e3][a4o]k|leek|tips?|system|clover\s+system|raid|server|global)|(?:e[vw]e(?:nt|ni|nf|nl|n7|m|rn)|l[e3][a4o]k|leek|tips?|system|clover\s+system|raid|server|global))([o0l]?\s*[\]\)\|':]|[o0l](?=\s)|(?=\s+[A-Za-z]))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex InlineSecondTagRegex = new Regex(@"\s[\[\(\.:',;\|]\s*(event[o0]?|leak|tips?|system|clover\s+system|raid)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PlayerChatPrefixRegex = new Regex(@"^\s*(\[[^\]]+\]\s*)?[A-Za-z0-9_]{2,24}\s*:\s+\S", RegexOptions.Compiled);
        private static readonly Regex HotbarSlotGluedRegex = new Regex(@"(^|\s)[1-9][A-Z][a-z]{2,}", RegexOptions.Compiled);

        // Known Clover Retribution [Tips] / [System] phrases so they are NEVER misread as events
        // even if the leading "[Tips]" tag gets clipped by bright game effects behind chat.
        private static readonly string[] NonEventTipPhrases = new[]
        {
            "teaming up", "award all the", "dealt 10", "deal 0 damage", "server events may happen",
            "happen every 15", "hidden requirements", "before you can learn", "chance to learn",
            "boot icon", "fast travel", "compass at the top", "top of your screen",
            "open the map", "semi colon", "open the menu", "grimoire equipped", "cast spells",
            "level up your magic", "gain spells",
            "unsheathe", "equipped weapon", "open the journal", "drop rates",
            "bring out your grimoire", "entered a safezone", "exited a safezone", "server initialized",
            "joined the experience", "turn shiftlock", "left control key"
        };

        // Known HUD / hotbar / chat chrome markers that must NEVER attach to event text.
        private static readonly string[] HudNoiseMarkers = new[]
        {
            "unlock chat", "to chat click", "cooking sheep", "cooking 2", "fury flamb",
            "culinary", "sheep stampede", "dark magic", "adrenaline", "magic spins",
            "race spins", "trait spins", "next reward", "encyclopedia", "server age", "main menu"
        };

        // Known complete ending words of Clover Retribution broadcasts that have NO trailing period.
        private static readonly string[] CompleteBroadcastEndings = new[]
        {
            "grasslands", "grassland", "jungle", "dunes", "shadows", "village",
            "beyond", "kingdom", "eyes", "plains", "wake", "sky", "capital"
        };

        // Stems & phrases covering BOTH present tense ("vanishes", "fades", "retreats", "leaving only", "vowing revenge")
        // and past tense ("vanished", "defeated", "despawned", "closed", "misteriously disappeared")
        // so despawn/death messages never trigger spawn alerts!
        private static readonly string[] DespawnIndicators = new[]
        {
            "vanish", "eanish", "vanished", "despawn", "disappear", "dissipat", "subsid", "fade", "fading",
            "defeat", "defeated", "slain", "was killed", "has died", "has fallen", "fallen", "falls to",
            "perished", "silenced", "vanquished", "crumbles", "crumbled", "calms", "has settled", "is settled", "settled down",
            "leaving only", "leaving on", "leaving ont", "in his wake", "his wake", "h is wake",
            "in its wake", "its wake", "whispers in", "beneath the dunes",
            "vowing revenge", "vowing", "revenge", "misteriously", "mysteriously",
            "retreat", "depart", "banish", "cease", "collaps", "dissolv",
            "reced", "sinks back", "returns to the", "returned to", "left the",
            "has ended", "event ended", "has closed", "portal closed", "rift closed", "closes",
            "no longer", "calm returns", "peace returns", "overcome", "extinguished", "has fled", "was fled", "fled the", "expired",
            "in the clasp", "clasp of", "determined soul", "finds its purpose", "star finds",
            "merging with a spirit", "spirit aflame", "aflame with purpose", "claimed the star", "star has been claimed"
        };

        private static readonly string[] GluedDespawnCompounds = new[]
        {
            "hasfallen", "wasfallen", "beendefeated", "wasdefeated", "hasvanished", "wasvanished",
            "hasdespawned", "hasdisappeared", "hassubsided", "hasended", "hasclosed",
            "wasslain", "beenslain", "hasperished", "fadesaway", "fadinginto",
            "leavingonly", "leavingont", "inhiswake", "initswake", "vowingrevenge",
            "cruciblecools", "orderisrestored", "releasesitsgrip", "darkreign",
            "intheclasp", "determinedsoul", "findsitspurpose", "starfinds",
            "mergingwithaspirit", "spiritaflame", "aflamewithpurpose"
        };

        private static readonly string[] FuzzyDespawnWords = new[]
        {
            "vanish", "vanishes", "vanished", "vanishing",
            "defeat", "defeats", "defeated", "defeating",
            "despawn", "despawns", "despawned",
            "disappear", "disappears", "disappeared",
            "retreat", "retreats", "retreated",
            "collapse", "collapses", "collapsed",
            "dissipate", "dissipates", "dissipated",
            "subside", "subsides", "subsided",
            "banish", "banished",
            "fallen", "perished", "vanquished",
            "extinguished", "revenge"
        };

        public static bool IsDespawnOrEndMessage(string normalizedLine)
        {
            if (string.IsNullOrEmpty(normalizedLine)) return false;
            if ((normalizedLine.Contains("corrupt") || normalizedLine.Contains("night")) &&
                (normalizedLine.Contains("releases its grip") || normalizedLine.Contains("order is restored")))
                return true;
            foreach (string indicator in DespawnIndicators)
            {
                if (normalizedLine.IndexOf(indicator, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            string compact = IntentSkeleton(normalizedLine).Replace(" ", "");
            for (int i = 0; i < GluedDespawnCompounds.Length; i++)
            {
                if (compact.IndexOf(GluedDespawnCompounds[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            string[] words = normalizedLine.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                string w = words[i];
                if (w.Length < 5) continue;
                for (int j = 0; j < FuzzyDespawnWords.Length; j++)
                {
                    if (NearOcrWord(w, FuzzyDespawnWords[j]))
                        return true;
                }
            }
            return false;
        }

        private static bool IsNonEventTipOrSystem(string raw, string norm)
        {
            if (!string.IsNullOrEmpty(raw) && NonEventPrefixRegex.IsMatch(raw))
                return true;
            if (string.IsNullOrEmpty(norm)) return false;
            foreach (string phrase in NonEventTipPhrases)
            {
                if (norm.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static bool IsHudOrHotbarLine(string raw, string norm)
        {
            if (string.IsNullOrEmpty(raw)) return true;
            if (raw.IndexOf('$') >= 0 && !EventTagRegex.IsMatch(raw))
                return true;
            if (!EventTagRegex.IsMatch(raw) && HotbarSlotGluedRegex.IsMatch(raw))
                return true;
            if (!string.IsNullOrEmpty(norm))
            {
                foreach (string marker in HudNoiseMarkers)
                {
                    if (norm.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            return false;
        }

        private static bool IsPlayerChatLine(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            if (EventTagRegex.IsMatch(raw) || NonEventPrefixRegex.IsMatch(raw)) return false;
            return PlayerChatPrefixRegex.IsMatch(raw);
        }

        private static bool EndsWithCompleteBroadcastPhrase(string norm)
        {
            if (string.IsNullOrEmpty(norm)) return false;
            foreach (string ending in CompleteBroadcastEndings)
            {
                if (norm.EndsWith(" " + ending, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(norm, ending, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public static string NormalizeForMatch(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return string.Empty;
            string s = line.ToLowerInvariant();
            s = NonAlphaNumRegex.Replace(s, " ");
            s = MultiSpaceRegex.Replace(s, " ").Trim();
            return s;
        }

        public static List<MatchedEvent> MatchLines(IEnumerable<string> ocrLines, RoMultiConfig config)
        {
            List<string> unusedTimeline;
            return MatchInternal(ocrLines, config, true, out unusedTimeline);
        }

        public static List<MatchedEvent> MatchAllOccurrences(IEnumerable<string> ocrLines, RoMultiConfig config)
        {
            List<string> unusedTimeline;
            return MatchInternal(ocrLines, config, false, out unusedTimeline);
        }

        public static List<MatchedEvent> MatchAllOccurrences(IEnumerable<string> ocrLines, RoMultiConfig config, out List<string> stitchedTimeline)
        {
            return MatchInternal(ocrLines, config, false, out stitchedTimeline);
        }

        private static bool EndsWithTerminalPunctuation(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.TrimEnd();
            if (t.Length == 0) return false;
            char last = t[t.Length - 1];
            return last == '.' || last == '!' || last == '?';
        }

        private static bool HasHighWordOverlap(string normA, string normB)
        {
            if (string.IsNullOrEmpty(normA) || string.IsNullOrEmpty(normB)) return false;
            var wordsA = new HashSet<string>(normA.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
            var wordsB = new HashSet<string>(normB.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
            int minCount = Math.Min(wordsA.Count, wordsB.Count);
            if (minCount == 0) return false;
            int overlap = 0;
            foreach (string w in wordsA)
            {
                if (wordsB.Contains(w)) overlap++;
            }
            return ((double)overlap / minCount) >= 0.5;
        }

        private static string CleanEventDisplayText(string rawLine, EventRule rule)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) return string.Empty;
            string cleaned = rawLine.Trim();

            // 1. Strip any inline second chat tag (e.g. "[Tips ...", "[,Tips...", "[Event ...") merged on the right
            var inlineTag = InlineSecondTagRegex.Match(cleaned, Math.Min(6, cleaned.Length));
            if (inlineTag.Success && inlineTag.Index >= 8)
            {
                cleaned = cleaned.Substring(0, inlineTag.Index).Trim();
            }

            // 2. Strip any trailing hotbar / HUD / tip fragment accidentally captured on the same line
            string[] cutMarkers = new[]
            {
                "Cooking ", "2Shccp", "3Culinary", "Sheep's Fury", "Culinary Call", "Sheep Stampede",
                "Dark Magic", "Unlock chat", "To chat click", "'Teaming up", "Teaming up",
                "Server Events", "Adrenaline"
            };
            int earliestCut = -1;
            foreach (string marker in cutMarkers)
            {
                int pos = cleaned.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (pos >= 10 && (earliestCut < 0 || pos < earliestCut))
                    earliestCut = pos;
            }
            var hotbarSlot = HotbarSlotGluedRegex.Match(cleaned, Math.Min(10, cleaned.Length));
            if (hotbarSlot.Success && hotbarSlot.Index >= 10 && (earliestCut < 0 || hotbarSlot.Index < earliestCut))
                earliestCut = hotbarSlot.Index;

            if (earliestCut >= 10)
            {
                cleaned = cleaned.Substring(0, earliestCut).Trim();
            }

            // 3. For named single-sentence broadcasts, trim any extra trailing junk after the sentence-ending punctuation
            //    as long as the sentence before that punctuation already cleanly matches the same rule!
            if (rule != null && !rule.IsCatchAll)
            {
                for (int i = 10; i < cleaned.Length - 2; i++)
                {
                    char c = cleaned[i];
                    if (c != '.' && c != '!' && c != '?') continue;

                    // Advance across ellipsis ("...")
                    int endIdx = i;
                    while (endIdx + 1 < cleaned.Length && cleaned[endIdx + 1] == '.')
                        endIdx++;

                    if (endIdx + 1 < cleaned.Length && cleaned[endIdx + 1] == ' ')
                    {
                        string prefix = cleaned.Substring(0, endIdx + 1).Trim();
                        string prefixNorm = NormalizeForMatch(prefix);
                        if (!string.IsNullOrEmpty(prefixNorm) && MatchesRule(prefixNorm, rule))
                        {
                            return prefix;
                        }
                    }
                    i = endIdx;
                }
            }

            return cleaned;
        }

        private static bool IsReadableCatchAllEvent(string cleanedLine, string afterTag, string norm)
        {
            if (string.IsNullOrWhiteSpace(afterTag) || afterTag.Length < 22)
                return false;
            if (IsNonEventTipOrSystem(cleanedLine, norm) || IsHudOrHotbarLine(cleanedLine, norm))
                return false;

            // Reject obvious OCR garbage symbols & non-ASCII accented noise (ä, ö, ü, », •) from colored walls
            int nonBasic = 0;
            for (int i = 0; i < afterTag.Length; i++)
            {
                char c = afterTag[i];
                if (c == '\u00BB' || c == '\u2022' || c == '+' || c == '~' || c == '^' || c == '{' || c == '}' || c == '|')
                    return false;
                if (c > 127 && c != '\u2014' && c != '\u2013' && c != '\u2018' && c != '\u2019')
                    return false;
                bool isBasic = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                               c == ' ' || c == '.' || c == ',' || c == ';' || c == '\'' || c == '-' || c == '!' || c == '?' || c == '\u2014';
                if (!isBasic) nonBasic++;
            }
            if (((double)nonBasic / afterTag.Length) > 0.10)
                return false;

            string[] tokens = afterTag.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 3) return false;

            int cleanWords = 0;
            int totalTokens = 0;
            foreach (string tok in tokens)
            {
                string trimmed = tok.Trim('[', ']', '(', ')', '.', ',', ';', ':', '!', '?', '\'', '"', '-', '\u2014');
                if (trimmed.Length == 0) continue;
                totalTokens++;

                if (trimmed.Length == 1 && (trimmed == "a" || trimmed == "A" || trimmed == "I"))
                {
                    cleanWords++;
                    continue;
                }
                if (trimmed.Length < 2) continue;

                bool ok = true;
                bool hasVowel = false;
                int innerUpper = 0;
                for (int i = 0; i < trimmed.Length; i++)
                {
                    char c = trimmed[i];
                    if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                    {
                        char lc = char.ToLowerInvariant(c);
                        if (lc == 'a' || lc == 'e' || lc == 'i' || lc == 'o' || lc == 'u' || lc == 'y')
                            hasVowel = true;
                        if (i > 0 && c >= 'A' && c <= 'Z')
                            innerUpper++;
                    }
                    else if (c != '\'' && c != '-')
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok && hasVowel && innerUpper == 0)
                    cleanWords++;
            }

            if (totalTokens < 4) return false;
            return cleanWords >= 4 && ((double)cleanWords / totalTokens) >= 0.60;
        }

        private static List<MatchedEvent> MatchInternal(
            IEnumerable<string> ocrLines,
            RoMultiConfig config,
            bool deduplicatePerRule,
            out List<string> stitchedTimeline)
        {
            var results = new List<MatchedEvent>();
            stitchedTimeline = new List<string>();
            if (ocrLines == null) return results;

            // Step 1: Stitch at most ONE wrapped continuation line onto a preceding [Event]/[Leak]/[Tips]/[System] line
            // while NEVER gluing hotbar/HUD text, [Tips]/[System] lines, player chat, or a second separate event!
            var stitchedMessages = new List<string>();
            var stitchedContinuations = new List<int>();
            var stitchedSourceIndices = new List<List<int>>();

            int origIdx = -1;
            foreach (string rawInput in ocrLines)
            {
                origIdx++;
                if (string.IsNullOrWhiteSpace(rawInput)) continue;
                string line = rawInput.Trim();
                string lineNorm = NormalizeForMatch(line);
                bool startsNewTag = AnyChatTagStartRegex.IsMatch(line) || EventTagRegex.IsMatch(line);
                bool isTipOrHudOrPlayer = IsNonEventTipOrSystem(line, lineNorm) || IsHudOrHotbarLine(line, lineNorm) || IsPlayerChatLine(line);

                if (!startsNewTag && !isTipOrHudOrPlayer && stitchedMessages.Count > 0)
                {
                    int prevIdx = stitchedMessages.Count - 1;
                    string prev = stitchedMessages[prevIdx];
                    string prevNorm = NormalizeForMatch(prev);
                    bool prevIsNonEvent = NonEventPrefixRegex.IsMatch(prev);
                    int maxCont = prevIsNonEvent ? 2 : 1; // Event broadcasts wrap at most 1 continuation line

                    if ((AnyChatTagStartRegex.IsMatch(prev) || EventTagRegex.IsMatch(prev)) &&
                        stitchedContinuations[prevIdx] < maxCont &&
                        prev.Length < 105 &&
                        !EndsWithTerminalPunctuation(prev) &&
                        !EndsWithCompleteBroadcastPhrase(prevNorm))
                    {
                        EventRule lineRule = string.IsNullOrEmpty(lineNorm) ? null : FindNamedRule(lineNorm, tagged: false);
                        bool canStitch = true;

                        if (lineRule != null)
                        {
                            if (prevIsNonEvent)
                            {
                                // Never attach a real event (e.g. Devil Rift) onto a [Tips]/[System] line!
                                canStitch = false;
                            }
                            else
                            {
                                EventRule prevRule = string.IsNullOrEmpty(prevNorm)
                                    ? null
                                    : FindNamedRule(prevNorm, tagged: EventTagRegex.IsMatch(prev));
                                if (prevRule != null && (prevRule.Id != lineRule.Id || HasHighWordOverlap(prevNorm, lineNorm)))
                                {
                                    // Either a different event, or a repeated copy of the same event with dropped [Event] tag
                                    canStitch = false;
                                }
                            }
                        }

                        if (canStitch)
                        {
                            stitchedMessages[prevIdx] = prev + " " + line;
                            stitchedContinuations[prevIdx]++;
                            stitchedSourceIndices[prevIdx].Add(origIdx);
                            continue;
                        }
                    }
                }
                stitchedMessages.Add(line);
                stitchedContinuations.Add(0);
                stitchedSourceIndices.Add(new List<int> { origIdx });
            }

            stitchedTimeline = stitchedMessages;

            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var consumedIndices = new HashSet<int>();
            EventRule catchAllRule = Rules.FirstOrDefault(r => r.IsCatchAll);

            // Exclude [Tips], [System], HUD/hotbar, and regular Player Chat lines (preserving timeline indices for anchor tracking!)
            // Keep lines that start with [Event]/[Leak] or contain a valid named event so CleanEventDisplayText can strip any trailing HUD/Tip noise.
            for (int i = 0; i < stitchedMessages.Count; i++)
            {
                string raw = stitchedMessages[i];
                if (NonEventPrefixRegex.IsMatch(raw) || IsPlayerChatLine(raw))
                {
                    consumedIndices.Add(i);
                    continue;
                }
                if (!EventTagRegex.IsMatch(raw))
                {
                    string norm = NormalizeForMatch(raw);
                    if (IsNonEventTipOrSystem(raw, norm) || IsHudOrHotbarLine(raw, norm))
                    {
                        string preClean = CleanEventDisplayText(raw, null);
                        if (FindNamedRule(NormalizeForMatch(preClean)) == null)
                            consumedIndices.Add(i);
                    }
                }
            }

            // Step 2: Match ALL named rules FIRST (single messages, then adjacent pairs for tag-less wrapped alerts)
            for (int i = 0; i < stitchedMessages.Count; i++)
            {
                if (consumedIndices.Contains(i)) continue;
                string msg = stitchedMessages[i];
                if (msg.Length < 5) continue;

                // Clean off any inline [Tips] or hotbar fragment before checking despawn/rules
                string preClean = CleanEventDisplayText(msg, null);
                string norm = NormalizeForMatch(preClean);
                if (string.IsNullOrEmpty(norm) || IsDespawnOrEndMessage(norm))
                {
                    consumedIndices.Add(i);
                    continue;
                }

                EventRule named = FindNamedRule(norm, tagged: EventTagRegex.IsMatch(msg));
                if (named == null && ExplicitLeakTagRegex.IsMatch(msg))
                    named = Rules.FirstOrDefault(r => r.Id == "leak_event");
                if (named != null)
                {
                    consumedIndices.Add(i);
                    string displayLine = CleanEventDisplayText(preClean, named);
                    string displayNorm = NormalizeForMatch(displayLine);
                    if (!IsDespawnOrEndMessage(displayNorm))
                        AddMatchedRule(results, seenKeys, deduplicatePerRule, named, displayLine, config, i, stitchedSourceIndices[i]);
                }
            }

            // Check adjacent pairs ONLY for two unconsumed tag-less wrapped lines (e.g., "Something feels" + "off...")
            for (int i = 0; i + 1 < stitchedMessages.Count; i++)
            {
                if (consumedIndices.Contains(i) || consumedIndices.Contains(i + 1)) continue;
                if (AnyChatTagStartRegex.IsMatch(stitchedMessages[i + 1])) continue;
                if (EndsWithTerminalPunctuation(stitchedMessages[i])) continue;
                string combined = CleanEventDisplayText(stitchedMessages[i] + " " + stitchedMessages[i + 1], null);
                string norm = NormalizeForMatch(combined);
                if (string.IsNullOrEmpty(norm) || IsDespawnOrEndMessage(norm)) continue;

                EventRule named = FindNamedRule(norm, tagged: EventTagRegex.IsMatch(stitchedMessages[i]));
                if (named != null)
                {
                    consumedIndices.Add(i);
                    consumedIndices.Add(i + 1);
                    string displayLine = CleanEventDisplayText(combined, named);
                    string displayNorm = NormalizeForMatch(displayLine);
                    if (!IsDespawnOrEndMessage(displayNorm))
                    {
                        var mergedSrc = new List<int>(stitchedSourceIndices[i]);
                        mergedSrc.AddRange(stitchedSourceIndices[i + 1]);
                        AddMatchedRule(results, seenKeys, deduplicatePerRule, named, displayLine, config, i + 1, mergedSrc);
                    }
                }
            }

            // Step 3: Only unconsumed, CLEAN, readable [Event]/[Leak] messages fall through to Catch-All
            // (never fires on half-clipped old lines scrolling off the very top border i == 0 of a full chat box,
            // nor on garbled OCR noise, [Tips], HUD/hotbar, or already-matched/ignored events!)
            if (catchAllRule != null)
            {
                for (int i = 0; i < stitchedMessages.Count; i++)
                {
                    if (consumedIndices.Contains(i)) continue;
                    // In a populated chat box (4+ lines), index 0 is ALWAYS the oldest message being half-clipped
                    // by the top scroll mask — never trigger Catch-All on a half-sliced exiting old line!
                    if (i == 0 && stitchedMessages.Count >= 4) continue;
                    string msg = CleanEventDisplayText(stitchedMessages[i], null);
                    if (!EventTagRegex.IsMatch(msg)) continue;

                    string norm = NormalizeForMatch(msg);
                    if (string.IsNullOrEmpty(norm) || IsDespawnOrEndMessage(norm)) continue;

                    // Extract text after [Event] / [Leak] tag
                    var tagMatch = EventTagRegex.Match(msg);
                    string afterTag = (tagMatch.Success && tagMatch.Index + tagMatch.Length < msg.Length)
                        ? msg.Substring(tagMatch.Index + tagMatch.Length).Trim()
                        : msg;

                    if (!IsReadableCatchAllEvent(msg, afterTag, norm)) continue;

                    AddMatchedRule(results, seenKeys, deduplicatePerRule, catchAllRule, msg, config, i, stitchedSourceIndices[i]);
                }
            }

            // Order strictly top-to-bottom (oldest message first, newest bottom message last!)
            results.Sort((a, b) => a.TimelineIndex.CompareTo(b.TimelineIndex));
            return results;
        }

        private static bool IsPriorityTaglessRule(EventRule rule)
        {
            return rule.Id == "devil_rift" || rule.Id == "spirit_rift" ||
                rule.Id == "wind_wisp" || rule.Id == "blood_wisp" || rule.Id == "light_wisp" || rule.Id == "spirit_wisp";
        }

        private static bool IsPriorityWispRule(EventRule rule)
        {
            return rule.Id == "wind_wisp" || rule.Id == "blood_wisp" || rule.Id == "light_wisp" || rule.Id == "spirit_wisp";
        }

        private static bool AllowsTaglessRule(EventRule rule, string normalizedLine, string[] words, string compact)
        {
            if (!IsPriorityTaglessRule(rule)) return false;
            if (rule.Id == "devil_rift") return IsDevilRiftFuzzyBroadcast(normalizedLine, false);
            if (rule.Id == "spirit_rift")
            {
                if (!StartsNearRift(words, compact)) return false;
                for (int i = 0; i < words.Length; i++)
                {
                    string w = words[i];
                    if (w == "kingdom" || w == "felt" || w == "throughout" || w == "toward" || NearOcrWord(w, "kingdom"))
                        return true;
                }
                return compact.Contains("kingdom") || compact.Contains("throughout");
            }
            if (IsPriorityWispRule(rule)) return StartsNearPriorityWisp(words, compact);
            return false;
        }

        private static EventRule FindNamedRule(string normalizedLine, bool tagged = false)
        {
            if (GiantSeal.IsMatch(normalizedLine))
                return Rules.First(rule => rule.Id == "devil_assault");
            if (IsDevilRiftFuzzyBroadcast(normalizedLine, tagged))
                return Rules.First(rule => rule.Id == "devil_rift");
            var star = FindStarRule(normalizedLine, tagged);
            if (star != null) return star;

            string[] words = normalizedLine.Split(' ');
            string compact = IntentSkeleton(normalizedLine).Replace(" ", "");

            // Tier 1: Exact token-group match (tag-less restricted strictly to guarded priority rules)
            foreach (var rule in Rules)
            {
                if (rule.IsCatchAll) continue;
                if (!tagged && !AllowsTaglessRule(rule, normalizedLine, words, compact)) continue;
                if (MatchesRule(normalizedLine, rule))
                    return rule;
            }

            if (!tagged && !StartsNearRift(words, compact) && !StartsNearPriorityWisp(words, compact))
                return null;

            // Tier 2: Unique single-word typo match (strict ambiguity short-circuit -> null)
            EventRule tier2Candidate = null;
            int tier2Count = 0;
            foreach (var rule in Rules)
            {
                if (rule.IsCatchAll) continue;
                if (!tagged && !AllowsTaglessRule(rule, normalizedLine, words, compact)) continue;
                if (!MatchesRuleTypo(words, rule)) continue;
                tier2Candidate = rule;
                tier2Count++;
                if (tier2Count >= 2) return null;
            }
            if (tier2Count == 1) return tier2Candidate;

            // Tier 3.1: Space-glued distinctive multi-word anchor match (strict ambiguity short-circuit -> null)
            EventRule gluedCandidate = null;
            int gluedCount = 0;
            foreach (var rule in Rules)
            {
                if (rule.IsCatchAll) continue;
                if (!tagged && !AllowsTaglessRule(rule, normalizedLine, words, compact)) continue;
                if (!MatchesRuleGlued(compact, rule)) continue;
                gluedCandidate = rule;
                gluedCount++;
                if (gluedCount >= 2) return null;
            }
            if (gluedCount == 1) return gluedCandidate;

            // Tier 3.2: Multi-typo token-group intent match (strict uniqueness -> null if 2+ rules qualify)
            EventRule intentCandidate = null;
            int intentCount = 0;
            foreach (var rule in Rules)
            {
                if (rule.IsCatchAll) continue;
                if (!tagged && !AllowsTaglessRule(rule, normalizedLine, words, compact)) continue;
                if (!MatchesRuleIntentMultiTypo(words, rule)) continue;
                intentCandidate = rule;
                intentCount++;
                if (intentCount >= 2) return null;
            }
            return intentCount == 1 ? intentCandidate : null;
        }

        private static bool StartsNearPriorityWisp(string[] words, string compact)
        {
            if (compact.StartsWith("windwisp") || compact.StartsWith("awindwisp") || compact.StartsWith("thewindwisp") ||
                compact.StartsWith("bloodwisp") || compact.StartsWith("abloodwisp") || compact.StartsWith("thebloodwisp") ||
                compact.StartsWith("lightwisp") || compact.StartsWith("alightwisp") || compact.StartsWith("thelightwisp") ||
                compact.StartsWith("radiantwisp") || compact.StartsWith("aradiantwisp") || compact.StartsWith("theradiantwisp") ||
                compact.StartsWith("spiritwisp") || compact.StartsWith("aspiritwisp") || compact.StartsWith("thespiritwisp"))
                return true;

            int start = (words.Length > 0 && (words[0] == "a" || words[0] == "the")) ? 1 : 0;
            if (words.Length - start < 2) return false;
            return NearOpeningBoth(words, start, "wind", "wisp") ||
                NearOpeningBoth(words, start, "blood", "wisp") ||
                NearOpeningBoth(words, start, "light", "wisp") ||
                NearOpeningBoth(words, start, "radiant", "wisp") ||
                NearOpeningBoth(words, start, "spirit", "wisp");
        }

        private static EventRule FindStarRule(string normalizedLine, bool tagged)
        {
            if (string.IsNullOrEmpty(normalizedLine)) return null;
            string body = StarPrefixRegex.Replace(normalizedLine, "").Trim();
            if (string.IsNullOrEmpty(body) || body.IndexOf("inventory", StringComparison.OrdinalIgnoreCase) >= 0)
                return null;

            string skBody = IntentSkeleton(body);
            if (!tagged)
            {
                if (StarUiRejectRegex.IsMatch(body)) return null;
                Match head = StarCountPairRegex.Match(body);
                if (!head.Success || head.Index != 0)
                    head = StarGluedPairRegex.Match(skBody);
                if (!head.Success || head.Index != 0) return null;
                string src = head.Value.IndexOf(' ') >= 0 ? body : skBody;
                string after = src.Substring(head.Index + head.Length).Trim();
                if (string.IsNullOrEmpty(after)) return null;
                EventRule firstRule = ResolveStarByCount(head.Groups[1].Value);
                if (firstRule == null) return null;
                // Reject ambiguous tag-less lines containing a second distinct star count
                EventRule allCheck = FindUniqueStarInBody(body, skBody);
                return allCheck == firstRule ? firstRule : null;
            }

            return FindUniqueStarInBody(body, skBody);
        }

        private static EventRule FindUniqueStarInBody(string body, string skBody)
        {
            EventRule candidate = null;
            foreach (Match m in StarCountPairRegex.Matches(body))
            {
                EventRule star = ResolveStarByCount(m.Groups[1].Value);
                if (star == null) continue;
                if (candidate != null && candidate != star) return null;
                candidate = star;
            }
            foreach (Match m in StarGluedPairRegex.Matches(skBody))
            {
                EventRule star = ResolveStarByCount(m.Groups[1].Value);
                if (star == null) continue;
                if (candidate != null && candidate != star) return null;
                candidate = star;
            }
            return candidate;
        }

        private static EventRule ResolveStarByCount(string rawCount)
        {
            if (string.IsNullOrEmpty(rawCount)) return null;
            if (rawCount == "single" || rawCount == "lone") return StarRules[0];
            if (rawCount.Length == 1 && rawCount[0] >= '1' && rawCount[0] <= '7')
                return StarRules[rawCount[0] - '1'];

            string sk = IntentSkeleton(rawCount);
            for (int i = 0; i < StarCounts.Length; i++)
            {
                if (rawCount == StarCounts[i] || sk == StarCounts[i])
                    return StarRules[i];
            }

            EventRule candidate = null;
            for (int i = 0; i < StarCounts.Length; i++)
            {
                string word = StarCounts[i];
                if (word.Length < 4 || !NearOcrWord(sk, word)) continue;
                if (candidate != null) return null;
                candidate = StarRules[i];
            }
            return candidate;
        }

        private static bool StartsNearRift(string[] words, string compact)
        {
            if (compact.StartsWith("somethingfeels") || compact.StartsWith("asomethingfeels") ||
                compact.StartsWith("strangeenergy") || compact.StartsWith("astrangeenergy") ||
                compact.StartsWith("mysteriousenergy") || compact.StartsWith("amysteriousenergy"))
                return true;

            int start = (words.Length > 0 && (words[0] == "a" || words[0] == "the")) ? 1 : 0;
            if (words.Length - start < 2) return false;
            return NearOpeningBoth(words, start, "something", "feels") ||
                NearOpeningBoth(words, start, "strange", "energy") ||
                NearOpeningBoth(words, start, "mysterious", "energy");
        }

        private static bool NearOpeningBoth(string[] words, int start, string first, string second)
        {
            bool firstOk = words[start] == first || NearOcrWord(words[start], first);
            bool secondOk = words[start + 1] == second || NearOcrWord(words[start + 1], second);
            return firstOk && secondOk;
        }

        private static void AddMatchedRule(
            List<MatchedEvent> results,
            HashSet<string> seenKeys,
            bool deduplicatePerRule,
            EventRule rule,
            string rawLine,
            RoMultiConfig config,
            int timelineIndex,
            List<int> sourceLineIndices)
        {
            EventAlertLevel level = config != null ? config.GetEffectiveLevel(rule) : rule.DefaultLevel;
            string dedupeKey = rule.IsCatchAll
                ? ("unknown:" + BroadcastBodyKey(rawLine))
                : rule.Id;

            if (!deduplicatePerRule || seenKeys.Add(dedupeKey))
            {
                results.Add(new MatchedEvent
                {
                    Rule = rule,
                    EffectiveLevel = level,
                    RawLine = rawLine,
                    DedupeKey = dedupeKey,
                    TimelineIndex = timelineIndex,
                    SourceLineIndices = sourceLineIndices != null ? new List<int>(sourceLineIndices) : new List<int>()
                });
            }
        }

        private static bool MatchesRule(string normalizedLine, EventRule rule)
        {
            if (rule.TokenGroups == null) return false;
            foreach (var group in rule.TokenGroups)
            {
                bool allFound = true;
                foreach (string token in group)
                {
                    if (normalizedLine.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        allFound = false;
                        break;
                    }
                }
                if (allFound) return true;
            }
            return false;
        }

        internal static string BroadcastBodyKey(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var tag = EventTagRegex.Match(text);
            if (tag.Success && tag.Index == 0) text = text.Substring(tag.Length);
            string normalized = NormalizeForMatch(text);
            return normalized.Substring(0, Math.Min(160, normalized.Length)).Trim();
        }

        internal static bool SameBroadcastText(string first, string second)
        {
            if (first == second) return first.Length > 0;
            string[] a = first.Split(' ');
            string[] b = second.Split(' ');
            int shared = Math.Min(a.Length, b.Length);
            if (shared < 5 || Math.Abs(a.Length - b.Length) > 2) return false;
            int changed = 0;
            for (int i = 0; i < shared; i++)
            {
                if (a[i] == b[i]) continue;
                if (++changed > 1 || !NearOcrWord(a[i], b[i])) return false;
            }
            return true;
        }

        internal static bool SameUnknownBroadcast(string first, string second)
        {
            if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second)) return false;
            if (SameBroadcastText(first, second)) return true;
            string a = IntentSkeleton(first.ToLowerInvariant()).Replace(" ", "");
            string b = IntentSkeleton(second.ToLowerInvariant()).Replace(" ", "");
            int minLen = Math.Min(a.Length, b.Length);
            int maxLen = Math.Max(a.Length, b.Length);
            if (minLen < 18 || (double)minLen / maxLen < 0.65) return false;
            return BigramDice(a, b) >= 0.62;
        }

        private static double BigramDice(string a, string b)
        {
            if (a.Length < 2 || b.Length < 2) return 0.0;
            var countsA = new Dictionary<string, int>(StringComparer.Ordinal);
            int totalA = a.Length - 1;
            for (int i = 0; i < totalA; i++)
            {
                string bg = a.Substring(i, 2);
                int c;
                countsA.TryGetValue(bg, out c);
                countsA[bg] = c + 1;
            }
            int intersection = 0;
            int totalB = b.Length - 1;
            for (int i = 0; i < totalB; i++)
            {
                string bg = b.Substring(i, 2);
                int c;
                if (countsA.TryGetValue(bg, out c) && c > 0)
                {
                    intersection++;
                    countsA[bg] = c - 1;
                }
            }
            return (2.0 * intersection) / (totalA + totalB);
        }

        private static bool MatchesRuleGlued(string compactLine, EventRule rule)
        {
            if (string.IsNullOrEmpty(compactLine) || rule.TokenGroups == null) return false;
            foreach (string[] group in rule.TokenGroups)
            {
                if (group.Length < 2) continue;
                string joined = IntentSkeleton(string.Join("", group));
                if (joined.Length < 8) continue;
                if (compactLine.IndexOf(joined, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static bool MatchesRuleIntentMultiTypo(string[] words, EventRule rule)
        {
            if (rule.TokenGroups == null || words == null || words.Length == 0) return false;
            string[] wordSks = new string[words.Length];
            for (int i = 0; i < words.Length; i++)
                wordSks[i] = IntentSkeleton(words[i]);

            foreach (string[] group in rule.TokenGroups)
            {
                if (group.Length < 2) continue;
                int substantialTokens = 0;
                for (int t = 0; t < group.Length; t++)
                {
                    if (IntentSkeleton(group[t]).Length >= 4) substantialTokens++;
                }
                if (group.Length == 2 && substantialTokens < 2) continue;

                bool allMatched = true;
                bool strongAnchor = false;
                for (int t = 0; t < group.Length; t++)
                {
                    string tokSk = IntentSkeleton(group[t]);
                    int maxDist = tokSk.Length >= 8 ? 2 : (tokSk.Length >= 4 ? 1 : 0);
                    int bestDist = int.MaxValue;
                    for (int wi = 0; wi < wordSks.Length; wi++)
                    {
                        string wSk = wordSks[wi];
                        if (wSk == tokSk)
                        {
                            bestDist = 0;
                            break;
                        }
                        if (tokSk.Length >= 5 && wSk.Length > tokSk.Length && wSk.IndexOf(tokSk, StringComparison.Ordinal) >= 0)
                        {
                            if (bestDist > 1) bestDist = 1;
                            continue;
                        }
                        if (maxDist >= 1 && EditDistanceAtMost(wSk, tokSk, 1))
                        {
                            if (bestDist > 1) bestDist = 1;
                        }
                        else if (maxDist >= 2 && bestDist > 2 && EditDistanceAtMost(wSk, tokSk, 2))
                        {
                            bestDist = 2;
                        }
                    }
                    if (bestDist > maxDist)
                    {
                        allMatched = false;
                        break;
                    }
                    if (tokSk.Length >= 4 && bestDist <= 1)
                        strongAnchor = true;
                }
                if (allMatched && (strongAnchor || group.Length >= 3))
                    return true;
            }
            return false;
        }

        internal static string IntentSkeleton(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string s = text.Replace("rn", "m").Replace("nn", "m").Replace("nu", "m")
                .Replace("vv", "w").Replace("cl", "d");
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '0') sb.Append('o');
                else if (c == '1') sb.Append('l');
                else if (c == '3') sb.Append('e');
                else if (c == '4') sb.Append('a');
                else if (c == '5') sb.Append('s');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static bool MatchesRuleTypo(string[] words, EventRule rule)
        {
            if (rule.TokenGroups == null) return false;
            foreach (string[] group in rule.TokenGroups)
            {
                if (group.Length < 2) continue;
                int approximate = 0;
                bool anchor = false;
                bool matched = true;
                foreach (string token in group)
                {
                    if (words.Contains(token)) anchor |= token.Length >= 4;
                    else if (!words.Any(word => NearOcrWord(word, token)) || ++approximate > 1)
                    {
                        matched = false;
                        break;
                    }
                }
                if (matched && anchor && approximate == 1) return true;
            }
            return false;
        }

        private static bool IsDevilRiftFuzzyBroadcast(string normalizedLine, bool tagged)
        {
            if (string.IsNullOrEmpty(normalizedLine)) return false;
            if (normalizedLine.Contains("leaking") || normalizedLine.Contains("somebody") ||
                normalizedLine.Contains("sornebody") || normalizedLine.Contains("survive") ||
                normalizedLine.Contains("desert") || normalizedLine.Contains("village") ||
                normalizedLine.Contains("seen") || normalizedLine.Contains("shiftlock"))
                return false;

            if (normalizedLine.Contains("something feels") || normalizedLine.Contains("feels off"))
                return true;

            string[] words = normalizedLine.Split(' ');
            int somethingIdx = -1;
            for (int i = 0; i < words.Length; i++)
            {
                if (IsNearSomething(words[i]))
                {
                    somethingIdx = i;
                    break;
                }
            }
            bool hasSplitSomething = somethingIdx < 0 && DevilRiftSplitSomething.IsMatch(normalizedLine);
            if (somethingIdx < 0 && !hasSplitSomething)
            {
                // If [Event] tag is present and "feels off" / glued "feetsoff" is clear even if "Something" clipped
                return tagged && DevilRiftGluedFeelsOff.IsMatch(normalizedLine);
            }

            if (DevilRiftGluedFeelsOff.IsMatch(normalizedLine))
                return true;
            if (DevilRiftFuzzyFeels.IsMatch(normalizedLine))
                return true;
            if (tagged && normalizedLine.Contains(" off"))
                return true;
            if (tagged && DevilRiftOffWord.IsMatch(normalizedLine) && !normalizedLine.EndsWith(" oh"))
                return true;
            return false;
        }

        private static bool IsNearSomething(string word)
        {
            if (string.IsNullOrEmpty(word) || word.Length < 7 || word.Length > 11) return false;
            if (word.StartsWith("somebod") || word.StartsWith("sornebod")) return false;
            string sk = OcrSkeleton(word).Replace("rr", "m");
            if (!sk.StartsWith("so")) return false;
            if (sk == "something" || sk == "somethin") return true;
            return EditDistanceAtMost(sk, "something", 2);
        }

        private static bool EditDistanceAtMost(string a, string b, int maxDist)
        {
            if (Math.Abs(a.Length - b.Length) > maxDist) return false;
            int n = a.Length;
            int m = b.Length;
            int[] prev = new int[m + 1];
            int[] curr = new int[m + 1];
            for (int j = 0; j <= m; j++) prev[j] = j;
            for (int i = 1; i <= n; i++)
            {
                curr[0] = i;
                int rowMin = curr[0];
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    int val = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + cost);
                    curr[j] = val;
                    if (val < rowMin) rowMin = val;
                }
                if (rowMin > maxDist) return false;
                var tmp = prev; prev = curr; curr = tmp;
            }
            return prev[m] <= maxDist;
        }

        private static bool NearOcrWord(string first, string second)
        {
            if (Math.Min(first.Length, second.Length) < 4) return false;
            string a = OcrSkeleton(first);
            string b = OcrSkeleton(second);
            if (Math.Abs(a.Length - b.Length) > 1) return false;
            int i = 0;
            int j = 0;
            int edits = 0;
            while (i < a.Length && j < b.Length)
            {
                if (a[i] == b[j])
                {
                    i++;
                    j++;
                    continue;
                }
                if (++edits > 1) return false;
                if (a.Length >= b.Length) i++;
                if (b.Length >= a.Length) j++;
            }
            return edits + a.Length - i + b.Length - j <= 1;
        }

        private static string OcrSkeleton(string word)
        {
            string s = word.Replace("rn", "m").Replace("vv", "w");
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '0') sb.Append('o');
                else if (c == '1') sb.Append('l');
                else if (c == '3') sb.Append('e');
                else if (c == '4') sb.Append('a');
                else if (c == '5') sb.Append('s');
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}

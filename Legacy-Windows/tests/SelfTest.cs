using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;

namespace RoMulti
{
    internal static class SelfTest
    {
        private const string TestEventName = "ROBLOX_singletonEvent_ROMULTI_TEST";
        private const string TestMutexName = "ROBLOX_singletonMutex_ROMULTI_TEST";

        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--dummy")
            {
                byte[] ballast = new byte[48 * 1024 * 1024];
                for (int i = 0; i < ballast.Length; i += 4096)
                    ballast[i] = (byte)(i & 0xFF);

                using (var ev = new EventWaitHandle(false, EventResetMode.ManualReset, TestEventName))
                using (var mtx = new Mutex(true, TestMutexName))
                {
                    Console.WriteLine("DUMMY_READY");
                    Console.Out.Flush();
                    Thread.Sleep(20000);
                }

                GC.KeepAlive(ballast);
                return 0;
            }

            Console.WriteLine("=== RoMulti Full Verification (Tahap 1 + Tahap 2 Event Watcher & Anti-AFK) ===");

            if (!VerifyWatcherSessionTracking() || !VerifyScheduledFocus() || !VerifyMainOnlyAntiAfk() ||
                !VerifyFrozenCapture() || !VerifyCatalogTypos() || !VerifyOcrReplay() ||
                !VerifyStarBroadcasts() || !VerifyVampireCastleIgnored() || !VerifyCompactDiscordPayload())
                return 1;

            // 1. Roblox exe discovery
            string robloxExe = RobloxHandleEngine.FindLatestRobloxPlayerExe();
            Console.WriteLine("[1] FindLatestRobloxPlayerExe: " + (robloxExe ?? "(not found)"));
            if (string.IsNullOrEmpty(robloxExe))
            {
                Console.WriteLine("FAIL: Expected to locate installed RobloxPlayerBeta.exe");
                return 1;
            }

            // 2. System memory stats
            var memStats = AfkOptimizer.GetSystemMemoryStats();
            Console.WriteLine(string.Format("[2] SystemMemoryStats: Total={0:0.00} GB, Free={1:0.00} GB, Load={2}%",
                memStats.TotalPhysicalGB, memStats.AvailablePhysicalGB, memStats.MemoryLoadPercent));
            if (memStats.TotalPhysicalGB < 1.0 || memStats.AvailablePhysicalGB <= 0.0)
            {
                Console.WriteLine("FAIL: Invalid system memory statistics.");
                return 1;
            }

            // 3. Config save/load round-trip
            string tmpCfgPath = Path.Combine(Path.GetTempPath(), "romulti-selftest-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var cfgOut = new RoMultiConfig
                {
                    WebhookUrl = "https://discord.com/api/webhooks/123/abc",
                    PingText = "<@9999>",
                    AntiAfkEnabled = false,
                    AntiAfkMainWhenIdle = true,
                    PrivateServerCode = "roundtrip-server",
                    AntiAfkIntervalMinutes = 10
                };
                cfgOut.SetLevel("khamsin", EventAlertLevel.Ping);
                cfgOut.Save(tmpCfgPath);
                cfgOut.PrivateServerCode = "roundtrip-server-updated";
                cfgOut.Save(tmpCfgPath);

                var cfgIn = RoMultiConfig.Load(tmpCfgPath);
                var khamsinRule = EventCatalog.Rules.First(r => r.Id == "khamsin");
                if (cfgIn.WebhookUrl != cfgOut.WebhookUrl || cfgIn.PingText != "<@9999>" ||
                    cfgIn.ScanIntervalSeconds != 3 || !cfgIn.BackgroundChatHover || !cfgIn.WakeChatOnAntiAfk ||
                    cfgIn.AntiAfkEnabled || !cfgIn.AntiAfkMainWhenIdle ||
                    cfgIn.PrivateServerCode != "roundtrip-server-updated" ||
                    cfgIn.AntiAfkIntervalMinutes != 10 || cfgIn.GetEffectiveLevel(khamsinRule) != EventAlertLevel.Ping)
                {
                    Console.WriteLine("FAIL: Config round-trip mismatch.");
                    return 1;
                }
                ChatOcrEngine.PokeChatHoverBackground(IntPtr.Zero);
                Console.WriteLine("[3] Config JSON Save/Load round-trip (3s scan + chat wake defaults): OK");
            }
            finally
            {
                try { if (File.Exists(tmpCfgPath)) File.Delete(tmpCfgPath); } catch { }
            }

            // 4. EventCatalog matching & priority rules (including Devil Rift purple & Spirit Rift gold)
            var defaultCfg = new RoMultiConfig();
            var sampleLines = new[]
            {
                "Something feels off...",
                "A strange energy feel toward kingdom",
                "[Event] The sands shift; Khamsin, the Sand Sage, emerges from the dunes.",
                "[Event] Mana fluttering can be heard across the grasslands",
                "[Event] A brand new eclipse descends upon the clover realm!"
            };
            var matched = EventCatalog.MatchLines(sampleLines, defaultCfg);
            Console.WriteLine("[4] EventCatalog matches: " + matched.Count);
            foreach (var m in matched)
            {
                Console.WriteLine(string.Format("    -> {0} [{1}] <- {2}", m.Rule.DisplayName, m.EffectiveLevel, m.RawLine));
            }

            bool hasDevilPing = matched.Any(m => m.Rule.Id == "devil_rift" && m.EffectiveLevel == EventAlertLevel.Ping);
            bool hasSpiritPing = matched.Any(m => m.Rule.Id == "spirit_rift" && m.EffectiveLevel == EventAlertLevel.Ping);
            bool hasKhamsinNormal = matched.Any(m => m.Rule.Id == "khamsin" && m.EffectiveLevel == EventAlertLevel.Normal);
            bool hasManaIgnored = matched.Any(m => m.Rule.Id == "mana_wisp" && m.EffectiveLevel == EventAlertLevel.Ignore);
            bool hasCatchAll = matched.Any(m => m.Rule.Id == "unknown_event" && m.EffectiveLevel == EventAlertLevel.Normal);
            if (!hasDevilPing || !hasSpiritPing || !hasKhamsinNormal || !hasManaIgnored || !hasCatchAll)
            {
                Console.WriteLine("FAIL: EventCatalog priority classification mismatch.");
                return 1;
            }

            // 4b. Verify 2-line word-wrapped messages inside narrow 520x320 chat boxes still match cleanly
            var wrappedLines = new[]
            {
                "Something feels",
                "off...",
                "A mysterious energy",
                "flows toward the kingdom"
            };
            var wrappedMatches = EventCatalog.MatchLines(wrappedLines, defaultCfg);
            if (!wrappedMatches.Any(m => m.Rule.Id == "devil_rift") || !wrappedMatches.Any(m => m.Rule.Id == "spirit_rift"))
            {
                Console.WriteLine("FAIL: Word-wrapped 2-line chat match failed.");
                return 1;
            }
            Console.WriteLine("    4b Word-wrap 2-line matching (narrow 520x320 chat): OK");

            // 4c. Verify despawn / boss defeated messages are NEVER alerted as spawns
            var despawnLines = new[]
            {
                "[Event] The Bandit Lord vanishes, leaving only whispers",
                "[Event] The Warlord retreats, vowing revenge on the winds of the plains.",
                "[Event] Somebody has misteriously disappeared from the Desert Village",
                "[Event] Somebody ha",
                "[Event] Khamsin, the Sand Sage, retreats beneath the dunes.",
                "[Event] Khamsin, the Sand Sage, has been defeated!",
                "[Event] The Devil Rift closes.",
                "[Event] A blood wisp fades away from the grasslands.",
                "[Event] The strange energy toward the kingdom subsides."
            };
            var despawnMatches = EventCatalog.MatchLines(despawnLines, defaultCfg);
            if (despawnMatches.Count != 0)
            {
                Console.WriteLine("FAIL: Expected 0 matches for despawn/death lines, got " + despawnMatches.Count);
                return 1;
            }
            Console.WriteLine("    4c Despawn / defeated filter (0 false spawn pings): OK");

            // 5. Discord 5-min anti-spam deduplication (exact + fuzzy partial-OCR Catch-All dedupe)
            var notifier = new DiscordNotifier();
            bool dup1 = notifier.IsDuplicateAndRecord(4252, "devil_rift", recordIfNew: true);
            bool dup2 = notifier.IsDuplicateAndRecord(4252, "devil_rift", recordIfNew: true);
            bool dupOtherPid = notifier.IsDuplicateAndRecord(9999, "devil_rift", recordIfNew: true);
            bool unkFull = notifier.IsDuplicateAndRecord(32252, "unknown:mysterious eclipse descends upon kingdom", recordIfNew: true);
            bool unkTrunc = notifier.IsDuplicateAndRecord(32252, "unknown:mysterious eclipse desc", recordIfNew: true);
            bool unkDiff = notifier.IsDuplicateAndRecord(32252, "unknown:a totally different rare event", recordIfNew: true);
            Console.WriteLine(string.Format("[5] Discord dedupe check: exact={0}/{1}, fuzzyTrunc={2}/{3}, distinct={4}",
                dup1, dup2, unkFull, unkTrunc, unkDiff));
            if (dup1 || !dup2 || dupOtherPid || unkFull || !unkTrunc || unkDiff)
            {
                Console.WriteLine("FAIL: Deduplication logic failed.");
                return 1;
            }

            // 5b. Verify partial OCR lines from real user screenshots map directly to named rules (no double Catch-All)
            var partialShotLines = new[]
            {
                "[Event] Whispers rise; Oneofth Band• Lord emerg",
                "[Event] Whispers rise; Oneofth",
                "[Event] A warrior arrives with a fierce challenge in his eyes.",
                "[Event]uana fluttering can be heard throughthe"
            };
            var partialMatches = EventCatalog.MatchLines(partialShotLines, defaultCfg);
            if (partialMatches.Count(m => m.Rule.Id == "terra") != 1 ||
                partialMatches.Count(m => m.Rule.Id == "warlord") != 1 ||
                partialMatches.Count(m => m.Rule.Id == "mana_wisp") != 1 ||
                partialMatches.Any(m => m.Rule.Id == "unknown_event"))
            {
                Console.WriteLine("FAIL: Partial OCR screenshot lines did not map cleanly to 1x terra, 1x warlord & 1x mana_wisp.");
                return 1;
            }
            Console.WriteLine("    5b Partial OCR screenshot regression (1x terra + 1x warlord + 1x jungle mana_wisp): OK");

            // 5c. Exact 2-line wrapped Guarded Rift + Devil Rumors + Bandit Lord despawn from real screenshot
            // Must produce EXACTLY 1x guarded_rift, 1x devil_rumors, and ZERO unknown_event duplicates!
            var wrappedChatBox = new[]
            {
                "[Event] The Bandit Lord vanishes, leaving only whispers",
                "in his wake.",
                "[Tips] Some spells have hidden requirements",
                "before you can learn them.",
                "[Event] Somebody has seen something they might not",
                "survive to tell, word has it they're in the Desert Village...",
                "[Event] A rift shrouded in sinister mana tears open - the",
                "Shadowhand stands watch, guarding what lies beyond."
            };
            var stitchedRes = EventCatalog.MatchLines(wrappedChatBox, defaultCfg);
            int grCount = stitchedRes.Count(m => m.Rule.Id == "guarded_rift");
            int drCount = stitchedRes.Count(m => m.Rule.Id == "devil_rumors");
            int unkCount = stitchedRes.Count(m => m.Rule.Id == "unknown_event");
            if (grCount != 1 || drCount != 1 || unkCount != 0 || stitchedRes.Count != 2)
            {
                Console.WriteLine(string.Format("FAIL: Wrapped chat box stitch failed (guarded={0}, rumors={1}, unknown={2}, total={3})",
                    grCount, drCount, unkCount, stitchedRes.Count));
                return 1;
            }
            Console.WriteLine("    5c 2-line Guarded Rift + Devil Rumors stitching (0 Catch-All dupes): OK");
            if (DiscordNotifier.NormalizePingText("@541205463254171649") != "<@541205463254171649>" ||
                DiscordNotifier.NormalizePingText("541205463254171649") != "<@541205463254171649>" ||
                DiscordNotifier.NormalizePingText("@everyone") != "@everyone")
            {
                Console.WriteLine("FAIL: NormalizePingText failed.");
                return 1;
            }

            // 5d. Tag-less priority event immediately following [Tips] must NEVER be stitched/swallowed!
            var tipsThenRift = new[]
            {
                "[Tips] Find the boot icon to utilize fast travel.",
                "Something feels off...",
                "[Tips] Some spells have hidden",
                "Something feels off..."
            };
            var tipsRiftMatches = EventCatalog.MatchAllOccurrences(tipsThenRift, defaultCfg);
            if (tipsRiftMatches.Count(m => m.Rule.Id == "devil_rift") != 2)
            {
                Console.WriteLine("FAIL: Tag-less Devil Rift after [Tips] was swallowed (count=" + tipsRiftMatches.Count(m => m.Rule.Id == "devil_rift") + ").");
                return 1;
            }
            Console.WriteLine("    5d Tag-less Devil Rift after [Tips] preservation (2x): OK");

            // 5e. Baseline + Occurrence-Count + Faded-Chat + Flicker + Scroll-Replacement State Machine
            var sm = new DiscordNotifier();
            DateTime t0 = new DateTime(2026, 11, 22, 14, 0, 0, DateTimeKind.Utc);
            List<string> tl;
            bool wasBase;
            int baseCnt;

            var chat2Wisps = new[]
            {
                "[Tips] Find the boot icon to utilize fast travel.",
                "[Event] Mana fluttering can be heard through the jungle",
                "[Tips] The compass at the top of your screen is your friend.",
                "[Event] Mana fluttering can be heard through the jungle",
                "[Tips] Teaming up against enemies will award all the participants as long as they've dealt 10% damage.",
                "[Tips] Some spells are not 100% chance to learn."
            };
            var occ2 = EventCatalog.MatchAllOccurrences(chat2Wisps, defaultCfg, out tl);
            // Scan 1: Startup baseline -> MUST suppress the 2 old occurrences!
            var out1 = sm.FilterNewOccurrencesForPid(28796, "job-A", tl, occ2, false, out wasBase, out baseCnt, t0);
            if (!wasBase || baseCnt != 2 || out1.Count != 0)
            {
                Console.WriteLine(string.Format("FAIL 5e-1: Expected baseline init with 2 ignored, got wasBase={0} baseCnt={1} out={2}", wasBase, baseCnt, out1.Count));
                return 1;
            }
            // Scan 2: 20 minutes later, same 2 old wisps still sitting in chat -> 0 alerts!
            var out2 = sm.FilterNewOccurrencesForPid(28796, "job-A", tl, occ2, false, out wasBase, out baseCnt, t0.AddMinutes(20));
            if (wasBase || out2.Count != 0)
            {
                Console.WriteLine("FAIL 5e-2: Old wisps re-triggered after 20 min!");
                return 1;
            }
            // Scan 3: Chat fades completely out for 4 scans, then unfades showing the same 2 old wisps -> 0 alerts!
            var emptyTl = new List<string>();
            var emptyOcc = new List<MatchedEvent>();
            for (int f = 1; f <= 4; f++)
                sm.FilterNewOccurrencesForPid(28796, "job-A", emptyTl, emptyOcc, false, out wasBase, out baseCnt, t0.AddMinutes(21).AddSeconds(f * 3));
            var outUnfade = sm.FilterNewOccurrencesForPid(28796, "job-A", tl, occ2, false, out wasBase, out baseCnt, t0.AddMinutes(22));
            if (outUnfade.Count != 0)
            {
                Console.WriteLine("FAIL 5e-3: Unfading old chat re-triggered old events!");
                return 1;
            }
            // Scan 4: Brand-new 3rd Mana Wisp spawns at the bottom! -> MUST emit 1 alert!
            var chat3Wisps = new[]
            {
                "[Tips] Find the boot icon to utilize fast travel.",
                "[Event] Mana fluttering can be heard through the jungle",
                "[Tips] The compass at the top of your screen is your friend.",
                "[Event] Mana fluttering can be heard through the jungle",
                "[Tips] Teaming up against enemies will award all the participants as long as they've dealt 10% damage.",
                "[Tips] Some spells are not 100% chance to learn.",
                "[Event] Mana fluttering can be heard through the jungle"
            };
            var occ3 = EventCatalog.MatchAllOccurrences(chat3Wisps, defaultCfg, out tl);
            var out3 = sm.FilterNewOccurrencesForPid(28796, "job-A", tl, occ3, false, out wasBase, out baseCnt, t0.AddMinutes(23));
            if (out3.Count != 1 || out3[0].Rule.Id != "mana_wisp" || out3[0].TimelineIndex != 6)
            {
                Console.WriteLine("FAIL 5e-4: New 3rd Mana Wisp at bottom was not detected! count=" + out3.Count);
                return 1;
            }
            // Scan 5: 1-frame OCR flicker (3 -> 2 -> 3) -> MUST emit 0 false alerts!
            List<string> tl2;
            var occ2Flicker = EventCatalog.MatchAllOccurrences(chat2Wisps, defaultCfg, out tl2);
            var outFlickA = sm.FilterNewOccurrencesForPid(28796, "job-A", tl2, occ2Flicker, false, out wasBase, out baseCnt, t0.AddMinutes(23).AddSeconds(3));
            var outFlickB = sm.FilterNewOccurrencesForPid(28796, "job-A", tl, occ3, false, out wasBase, out baseCnt, t0.AddMinutes(23).AddSeconds(6));
            if (outFlickA.Count != 0 || outFlickB.Count != 0)
            {
                Console.WriteLine("FAIL 5e-5: 1-frame OCR flicker caused a false alert!");
                return 1;
            }
            // Scan 6: Jungle chrome-only faded scans ("Here Global", "To chat click here...") for 6 ticks
            // MUST freeze VisibleCount (never reset to 0) AND duplicate [Tips] above/below MUST NEVER re-trigger!
            var jungleChromeOnly = new List<string> { "Here Global Friends", "To chat click here or press / key" };
            for (int c = 1; c <= 6; c++)
                sm.FilterNewOccurrencesForPid(28796, "job-A", jungleChromeOnly, emptyOcc, false, out wasBase, out baseCnt, t0.AddMinutes(24).AddSeconds(c * 3));

            var chatDuplicateTips = new[]
            {
                "[Tips] You must have your grimoire equipped to cast spells.",
                "[Event] Mana fluttering can be heard through the jungle",
                "[Event] Mana fluttering can be heard through the jungle",
                "[Event] Mana fluttering can be heard through the jungle",
                "[Tips] You must have your grimoire equipped to cast spells.",
                "[Tips] You can open the map with the ';' key."
            };
            List<string> tlDupTips;
            var occDupTips = EventCatalog.MatchAllOccurrences(chatDuplicateTips, defaultCfg, out tlDupTips);
            var outAfterChromeFade = sm.FilterNewOccurrencesForPid(28796, "job-A", tlDupTips, occDupTips, false, out wasBase, out baseCnt, t0.AddMinutes(25));
            if (outAfterChromeFade.Count != 0)
            {
                Console.WriteLine("FAIL 5e-6: Chrome-only Jungle fade or duplicate [Tips] re-triggered old events!");
                return 1;
            }

            // Scan 7: Genuine scroll-off with 5 consecutive message-bearing [Tips] scans lowers VisibleCount -> next new spawn alerts!
            var tipsOnly = new List<string>
            {
                "[Tips] Press semi-colon to open the map",
                "[Tips] Level up your magic to gain spells."
            };
            for (int s = 1; s <= 5; s++)
                sm.FilterNewOccurrencesForPid(28796, "job-A", tipsOnly, emptyOcc, false, out wasBase, out baseCnt, t0.AddMinutes(26).AddSeconds(s * 3));
            List<string> tlFreshWisp;
            var occFreshWisp = EventCatalog.MatchAllOccurrences(new[] { "[Event] Mana fluttering can be heard through the jungle" }, defaultCfg, out tlFreshWisp);
            var outFreshWisp = sm.FilterNewOccurrencesForPid(28796, "job-A", tlFreshWisp, occFreshWisp, false, out wasBase, out baseCnt, t0.AddMinutes(28));
            if (outFreshWisp.Count != 1 || outFreshWisp[0].Rule.Id != "mana_wisp")
            {
                Console.WriteLine("FAIL 5e-7: Genuine new spawn after full scroll-off was missed! count=" + outFreshWisp.Count);
                return 1;
            }

            // Scan 8: Faded MAIN window on startup (0 lines for 20s), then Devil Rift wakes chat -> MUST alert!
            sm.FilterNewOccurrencesForPid(55555, "job-B", emptyTl, emptyOcc, false, out wasBase, out baseCnt, t0);
            sm.FilterNewOccurrencesForPid(55555, "job-B", emptyTl, emptyOcc, false, out wasBase, out baseCnt, t0.AddSeconds(20));
            List<string> tlRift;
            var occRift = EventCatalog.MatchAllOccurrences(new[] { "Something feels off..." }, defaultCfg, out tlRift);
            var outFadedMainRift = sm.FilterNewOccurrencesForPid(55555, "job-B", tlRift, occRift, false, out wasBase, out baseCnt, t0.AddMinutes(5));
            if (outFadedMainRift.Count != 1 || outFadedMainRift[0].Rule.Id != "devil_rift")
            {
                Console.WriteLine("FAIL 5e-8: Devil Rift waking a previously faded MAIN chat was missed!");
                return 1;
            }
            Console.WriteLine("    5e Baseline + Multi-Occurrence + Chrome-Fade + Flicker + Scroll-Off state machine: OK");

            // 5f. Event-only purity: strip hotbar skills, block garbled purple-glow Catch-All spam & player chat!
            var hotbarAttachedMulti = new[]
            {
                "[Event] Whispers rise; One of the Bandit Lords emerges",
                "from the shadows.",
                "Cooking Sheep 2Shccp's Fury Flambe",
                "3Culinary"
            };
            var cleanTerra = EventCatalog.MatchAllOccurrences(hotbarAttachedMulti, defaultCfg);
            if (cleanTerra.Count != 1 || cleanTerra[0].Rule.Id != "terra" ||
                cleanTerra[0].RawLine.IndexOf("Cooking", StringComparison.OrdinalIgnoreCase) >= 0 ||
                cleanTerra[0].RawLine.IndexOf("Culinary", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Console.WriteLine("FAIL 5f-1: Hotbar text leaked into Terra display line: " + (cleanTerra.Count > 0 ? cleanTerra[0].RawLine : "(none)"));
                return 1;
            }

            var hotbarSingleLine = new[]
            {
                "[Event] Whispers rise; One of the Bandit Lords emerges from the shadows. Cooking Sheep 2Shccp's Fury Flambe 3Culinary"
            };
            var cleanTerraSingle = EventCatalog.MatchAllOccurrences(hotbarSingleLine, defaultCfg);
            if (cleanTerraSingle.Count != 1 || cleanTerraSingle[0].RawLine != "[Event] Whispers rise; One of the Bandit Lords emerges from the shadows.")
            {
                Console.WriteLine("FAIL 5f-2: Single-line trailing hotbar trim failed: " + (cleanTerraSingle.Count > 0 ? cleanTerraSingle[0].RawLine : "(none)"));
                return 1;
            }

            // All 5 real garbled purple-glow spam screenshots + pure garbled Catch-All + Player chat -> ZERO unknown_event!
            var garbledPurpleGlowBatch = new[]
            {
                "[Event] Band' Lord emerg rom h hadows.",
                "[Event] Mana flutte+ingcanb h ard cros h grasslands 'Teaming up agams enemies will ward all the participant as long as the 've deal 0% damage.",
                "[Event] beheard acrossth grassland [Tips 'Teaming up against enemies will award all the [Event Somebod hasseen somethingthe migh no urviv o ell,",
                "[Event] Someboqy h ds seen!smnethinp. thev .eanishes leaving ont [,TipsJESeiyeNEvents nn n \u2022 utes. Cooking 2Shccp's 3Culinary",
                "[Event] Somebody,has seenrsmnething.thev,tnwztli Events,ma Jhappenev.ei\u00BBy 0 n utes. Cooking 2Shccp's 3Culinary",
                "[Event] zxq!mnb\u00BBkjh \u2022 2Shccp 3Culinary",
                "PlayerOne: did devil rift spawn yet?",
                "[Global] NoobSlayer99: anyone see khamsin?"
            };
            var garbledRes = EventCatalog.MatchAllOccurrences(garbledPurpleGlowBatch, defaultCfg);
            bool anyUnknown = garbledRes.Any(m => m.Rule.Id == "unknown_event");
            bool anyPlayerTrigger = garbledRes.Any(m => m.Rule.Id == "devil_rift" || m.Rule.Id == "khamsin");
            bool anyHotbarOrTip = garbledRes.Any(m =>
                m.RawLine.IndexOf("Cooking", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.RawLine.IndexOf("Teaming up", StringComparison.OrdinalIgnoreCase) >= 0);
            if (anyUnknown || anyPlayerTrigger || anyHotbarOrTip)
            {
                Console.WriteLine("FAIL 5f-3: Garbled/hotbar/player-chat leaked! Results: " +
                    string.Join(" || ", garbledRes.Select(m => m.Rule.Id + ":" + m.RawLine)));
                return 1;
            }
            Console.WriteLine("    5f Event-only purity (0 hotbar, 0 garbled Catch-All spam, 0 player-chat triggers): OK");

            // 5g. Corrupted Night exact screenshot OCR regression
            var corruptedNightLines = new[]
            {
                "[Event] The air grows heavysand darkness takes hold as eve creature is consumed b Corruption."
            };
            var cnRes = EventCatalog.MatchAllOccurrences(corruptedNightLines, defaultCfg);
            if (cnRes.Count != 1 || cnRes[0].Rule.Id != "corrupted_night")
            {
                Console.WriteLine("FAIL 5g: Corrupted Night broadcast did not map to corrupted_night!");
                return 1;
            }
            Console.WriteLine("    5g Corrupted Night ('The air grows heavy... consumed by corruption'): OK");

            // 5h. Top-edge scroll-clipped Mana Wisp & arbitrary half-clipped exiting top line -> ZERO Catch-All spam!
            var topClippedChat = new[]
            {
                "[Event] Mana rturtenng can De neara across tne",
                "grasslands",
                "[Event] The Bandit Lord vanishes, leaving only whispers",
                "in his wake.",
                "[Tips] Level up your magic to gain spells.",
                "[Event] The air grows heavy, and darkness takes hold as",
                "every creature is consumed by corruption.",
                "Your friend ANDD has joined the experience.",
                "[Event] A rift shrouded in sinister mana tears open - the",
                "Shadowhand stands watch, guarding what lies beyond."
            };
            var topClippedRes = EventCatalog.MatchAllOccurrences(topClippedChat, defaultCfg);
            if (topClippedRes.Any(m => m.Rule.Id == "unknown_event") ||
                topClippedRes.Count(m => m.Rule.Id == "mana_wisp") != 1 ||
                topClippedRes.Count(m => m.Rule.Id == "corrupted_night") != 1 ||
                topClippedRes.Count(m => m.Rule.Id == "guarded_rift") != 1)
            {
                Console.WriteLine("FAIL 5h-1: Top-clipped chat failed: " + string.Join(", ", topClippedRes.Select(m => m.Rule.Id)));
                return 1;
            }
            var exitingOldTopClip = new[]
            {
                "[Event] Some half sliced old broadcast exiting top border",
                "[Tips] Level up your magic to gain spells.",
                "[Tips] Press semi-colon to open the map",
                "Your friend ANDD has joined the experience."
            };
            var exitRes = EventCatalog.MatchAllOccurrences(exitingOldTopClip, defaultCfg);
            if (exitRes.Any(m => m.Rule.Id == "unknown_event"))
            {
                Console.WriteLine("FAIL 5h-2: Half-clipped top line i==0 in full chat leaked into Catch-All!");
                return 1;
            }
            Console.WriteLine("    5h Top-edge scroll-clipped line protection (0 Catch-All leaks): OK");

            // 5i. Orange-wall garbled [Leak] + [Evento Sornebody...] -> maps cleanly to leak_event, strips [Evento, 0 Catch-All!
            var orangeWallLeak = new[]
            {
                "[Leak] Sornetning is leakingin:thQgrassl\u00E4ifdQ- [Evento Sornebody has seen sornetning,they Illight not"
            };
            var owRes = EventCatalog.MatchAllOccurrences(orangeWallLeak, defaultCfg);
            if (owRes.Count != 1 || owRes[0].Rule.Id != "leak_event" ||
                owRes[0].RawLine.IndexOf("Evento", StringComparison.OrdinalIgnoreCase) >= 0 ||
                owRes[0].RawLine.IndexOf("Sornebody", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Console.WriteLine("FAIL 5i: Orange-wall [Leak] + [Evento cleanup failed: " +
                    string.Join(" || ", owRes.Select(m => m.Rule.Id + ":" + m.RawLine)));
                return 1;
            }
            Console.WriteLine("    5i Orange-wall [Leak] + [Evento split & non-ASCII Catch-All gate: OK");

            // 6. Live WinRT OCR test on synthetic purple Devil Rift + gold Spirit Rift over bright desert sky!
            Console.WriteLine("[6] Testing native WinRT OCR (2-pass high-saturation color isolation)...");
            using (var synth = new Bitmap(460, 90, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(synth))
                {
                    // Simulate bright sky/sand background (hardest case for normal OCR!)
                    g.Clear(Color.FromArgb(205, 200, 188));
                    using (var f = new Font("Segoe UI", 12, FontStyle.Bold))
                    using (var purple = new SolidBrush(Color.FromArgb(195, 70, 255)))
                    using (var gold = new SolidBrush(Color.FromArgb(245, 185, 25)))
                    {
                        g.DrawString("Something feels off...", f, purple, 12, 12);
                        g.DrawString("strange energy feel toward kingdom", f, gold, 12, 46);
                    }
                }
                var ocrLines = ChatOcrEngine.RecognizeChatLinesAsync(synth).GetAwaiter().GetResult();
                var synthMatches = EventCatalog.MatchLines(ocrLines, defaultCfg);
                Console.WriteLine("    Synthetic bright-bg OCR lines: " + string.Join(" | ", ocrLines));
                Console.WriteLine("    Matched priority events: " + string.Join(", ", synthMatches.Select(m => m.Rule.Id + "[" + m.EffectiveLevel + "]")));
                if (!synthMatches.Any(m => m.Rule.Id == "devil_rift") || !synthMatches.Any(m => m.Rule.Id == "spirit_rift"))
                {
                    Console.WriteLine("FAIL: Native WinRT OCR failed to detect Devil Rift + Spirit Rift on bright background.");
                    return 1;
                }
            }

            // 6b. Also test user's real Khamsin + Mana Wisp screenshot if still present
            string fixtureDir = Environment.GetEnvironmentVariable("ROMULTI_OCR_FIXTURES");
            string userShot = FixturePath(fixtureDir, "khamsin-and-mana.png");
            if (File.Exists(userShot))
            {
                var realLines = ChatOcrEngine.RecognizeFromImageFileAsync(userShot).GetAwaiter().GetResult();
                var realMatches = EventCatalog.MatchLines(realLines, defaultCfg);
                Console.WriteLine("    Real screenshot OCR lines: " + string.Join(" | ", realLines));
                int khamsinCount = realMatches.Count(m => m.Rule.Id == "khamsin");
                int manaCount = realMatches.Count(m => m.Rule.Id == "mana_wisp");
                if (khamsinCount != 1 || manaCount != 1)
                {
                    Console.WriteLine("FAIL: Expected exactly 1 Khamsin and 1 Mana Wisp match (got " + khamsinCount + ", " + manaCount + ").");
                    return 1;
                }
            }

            // 6c. Test user's ACTUAL full-window Clover Retribution screenshot with bottom-left chat box!
            string fullWindowShot = FixturePath(fixtureDir, "first-kingdom-window.jpg");
            if (File.Exists(fullWindowShot))
            {
                using (var fullBmp = (Bitmap)Image.FromFile(fullWindowShot))
                using (var crop = ChatOcrEngine.CropBottomLeftChat(fullBmp))
                {
                    var bottomLines = ChatOcrEngine.RecognizeChatLinesAsync(crop).GetAwaiter().GetResult();
                    var bottomMatches = EventCatalog.MatchLines(bottomLines, defaultCfg);
                    Console.WriteLine(string.Format("    6c Real full-window bottom-left crop ({0}x{1} from {2}x{3}) -> {4} OCR line(s):",
                        crop.Width, crop.Height, fullBmp.Width, fullBmp.Height, bottomLines.Count));
                    foreach (var m in bottomMatches)
                        Console.WriteLine("       MATCH: " + m.Rule.Id + " -> " + m.RawLine);

                    bool gotLeak = bottomMatches.Any(m => m.Rule.Id == "leak_event");
                    bool gotBandit = bottomMatches.Any(m => m.Rule.Id == "terra");
                    bool gotBloodWisp = bottomMatches.Any(m => m.Rule.Id == "spirit_wisp");
                    if (!gotLeak || !gotBandit || !gotBloodWisp)
                    {
                        Console.WriteLine("    OCR LINES DUMP: " + string.Join(" | ", bottomLines));
                        Console.WriteLine(string.Format("FAIL: Bottom-left crop missed events (leak={0}, bandit={1}, bloodWisp={2})", gotLeak, gotBandit, gotBloodWisp));
                        return 1;
                    }
                }
            }

            // 6d. Test user's ACTUAL Jungle chat screenshot containing THREE separate Mana Wisp broadcasts!
            // Must preserve ALL 3 distinct physical occurrences (oldest top -> newest bottom) via Y-band OCR + MatchAllOccurrences!
            string jungleShot = FixturePath(fixtureDir, "jungle-window.png");
            if (File.Exists(jungleShot))
            {
                var jungleLines = ChatOcrEngine.RecognizeFromImageFileAsync(jungleShot).GetAwaiter().GetResult();
                var jungleAll = EventCatalog.MatchAllOccurrences(jungleLines, defaultCfg);
                int wispOccurrences = jungleAll.Count(m => m.Rule.Id == "mana_wisp");
                int unkOccurrences = jungleAll.Count(m => m.Rule.Id == "unknown_event");
                Console.WriteLine(string.Format("    6d Real Jungle chat (3x Mana Wisp): {0} OCR lines -> mana_wisp={1}, unknown={2}",
                    jungleLines.Count, wispOccurrences, unkOccurrences));
                if (wispOccurrences != 3 || unkOccurrences != 0)
                {
                    Console.WriteLine("    JUNGLE OCR DUMP: " + string.Join(" | ", jungleLines));
                    Console.WriteLine(string.Format("FAIL: Expected 3x mana_wisp and 0x unknown on Jungle screenshot, got mana_wisp={0}, unknown={1}",
                        wispOccurrences, unkOccurrences));
                    return 1;
                }
            }

            // 6e. Synthetic HUD/Hotbar geometric exclusion test (chat on left margin, hotbar & $ HUD shifted right)
            using (var hudBmp = new Bitmap(360, 140, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(hudBmp))
                {
                    g.Clear(Color.FromArgb(24, 24, 32));
                    using (var f = new Font("Segoe UI", 10, FontStyle.Bold))
                    using (var purple = new SolidBrush(Color.FromArgb(195, 110, 255)))
                    using (var gold = new SolidBrush(Color.FromArgb(245, 200, 60)))
                    using (var white = new SolidBrush(Color.White))
                    {
                        g.DrawString("[Event] Whispers rise; One of the Bandit Lords emerges", f, purple, 8, 12);
                        g.DrawString("from the shadows.", f, purple, 8, 32);
                        g.DrawString("4900 $", f, gold, 265, 32);
                        g.DrawString("Cooking Sheep  Culinary Call", f, white, 140, 105);
                    }
                }
                var hudOcr = ChatOcrEngine.RecognizeChatLinesAsync(hudBmp).GetAwaiter().GetResult();
                var hudMatches = EventCatalog.MatchAllOccurrences(hudOcr, defaultCfg);
                bool leakedHud = hudOcr.Any(l =>
                    l.IndexOf("Cooking", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    l.IndexOf("4900", StringComparison.OrdinalIgnoreCase) >= 0);
                if (leakedHud || hudMatches.Count != 1 || hudMatches[0].Rule.Id != "terra")
                {
                    Console.WriteLine("    6e OCR DUMP: " + string.Join(" | ", hudOcr));
                    Console.WriteLine("FAIL 6e: Geometric HUD/hotbar exclusion failed.");
                    return 1;
                }
                Console.WriteLine("    6e Geometric left-margin + word-gap HUD exclusion (0 hotbar / 0 $ leaked): OK");
            }

            // 6f. Test tight event text-box crop (CropEventBoxFromCapture) + stroke-isolated OCR on user's tight reference screenshot!
            string tightRefShot = FixturePath(fixtureDir, "mana-chat-crop.png");
            if (File.Exists(tightRefShot))
            {
                var tightLines = ChatOcrEngine.RecognizeFromImageFileAsync(tightRefShot).GetAwaiter().GetResult();
                var tightMatches = EventCatalog.MatchAllOccurrences(tightLines, defaultCfg);
                if (tightMatches.Count(m => m.Rule.Id == "mana_wisp") != 1 || tightMatches.Any(m => m.Rule.Id == "unknown_event"))
                {
                    Console.WriteLine("    6f OCR DUMP: " + string.Join(" | ", tightLines));
                    Console.WriteLine("FAIL 6f-1: Tight reference screenshot failed stroke-isolated OCR.");
                    return 1;
                }
                Console.WriteLine("    6f-1 Tight reference screenshot stroke-isolated OCR: OK (" + tightMatches[0].RawLine + ")");
            }
            if (File.Exists(fullWindowShot))
            {
                using (var fullBmp = (Bitmap)Image.FromFile(fullWindowShot))
                using (var rawMock = new RawDualCrop
                {
                    RegionMode = ChatRegionMode.BottomLeftKingdom,
                    BottomCrop = ChatOcrEngine.CropBottomLeftChat(fullBmp),
                    BottomPreviewOffsetY = 0
                })
                {
                    using (var msPreview = new MemoryStream())
                    {
                        rawMock.BottomCrop.Save(msPreview, ImageFormat.Png);
                        rawMock.CombinedPreviewPng = msPreview.ToArray();
                        rawMock.PreviewWidth = rawMock.BottomCrop.Width;
                        rawMock.PreviewHeight = rawMock.BottomCrop.Height;
                    }
                    var capRes = ChatOcrEngine.RecognizeFromRawCropsAsync(rawMock).GetAwaiter().GetResult();
                    var capMatches = EventCatalog.MatchAllOccurrences(capRes.OcrLines, defaultCfg);
                    var leakMatch = capMatches.FirstOrDefault(m => m.Rule.Id == "leak_event");
                    if (leakMatch == null || leakMatch.SourceLineIndices.Count == 0)
                    {
                        Console.WriteLine("FAIL 6f-2: Missing leak_event or SourceLineIndices on full-window chat box.");
                        return 1;
                    }
                    byte[] tightPng = ChatOcrEngine.CropEventBoxFromCapture(capRes, leakMatch.SourceLineIndices);
                    using (var msCheck = new MemoryStream(tightPng))
                    using (var tightBmp = new Bitmap(msCheck))
                    {
                        Console.WriteLine(string.Format("    6f-2 Tight Event Text-Box crop ({0}x{1} from {2}x{3} chat box): OK",
                            tightBmp.Width, tightBmp.Height, rawMock.PreviewWidth, rawMock.PreviewHeight));
                        if (tightBmp.Height >= rawMock.PreviewHeight)
                        {
                            Console.WriteLine("FAIL 6f-2: Tight event crop was not tighter than full chat box!");
                            return 1;
                        }
                    }
                }
            }

            // 6g. Low-resolution compact chat OCR test (small 300x110 crop with outlined 7.5pt fonts over noisy bg)
            using (var lowRes = new Bitmap(300, 110, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(lowRes))
                {
                    g.Clear(Color.FromArgb(46, 56, 72));
                    using (var f = new Font("Segoe UI", 8.0f, FontStyle.Bold))
                    using (var outline = new SolidBrush(Color.FromArgb(20, 20, 24)))
                    using (var purple = new SolidBrush(Color.FromArgb(205, 115, 255)))
                    using (var red = new SolidBrush(Color.FromArgb(255, 85, 85)))
                    {
                        string[] rows = new[]
                        {
                            "[Event] The air grows heavy, darkness takes hold.",
                            "[Leak] Something is leaking in the grasslands...",
                            "[Event] Stronger vampires can now be seen around the castle.",
                            "[Event] A shadow deepens as Vampire Lord Lucius awakens."
                        };
                        Brush[] brushes = new Brush[] { purple, red, purple, purple };
                        for (int r = 0; r < rows.Length; r++)
                        {
                            float y = 8 + r * 23;
                            g.DrawString(rows[r], f, outline, 7, y);
                            g.DrawString(rows[r], f, outline, 9, y);
                            g.DrawString(rows[r], f, outline, 8, y - 1);
                            g.DrawString(rows[r], f, outline, 8, y + 1);
                            g.DrawString(rows[r], f, brushes[r], 8, y);
                        }
                    }
                }
                var lowLines = ChatOcrEngine.RecognizeChatLinesAsync(lowRes).GetAwaiter().GetResult();
                var lowMatches = EventCatalog.MatchAllOccurrences(lowLines, defaultCfg);
                bool hasCorrupted = lowMatches.Any(m => m.Rule.Id == "corrupted_night" && m.EffectiveLevel == EventAlertLevel.Normal);
                bool hasLeak = lowMatches.Any(m => m.Rule.Id == "leak_event" && m.EffectiveLevel == EventAlertLevel.Normal);
                bool anyVampireActive = lowMatches.Any(m => m.Rule.Id == "vampire_castle" && m.EffectiveLevel != EventAlertLevel.Ignore);
                bool anyLowUnknown = lowMatches.Any(m => m.Rule.Id == "unknown_event");
                Console.WriteLine("    6g Low-res 300x110 OCR lines: " + string.Join(" | ", lowLines));
                if (!hasCorrupted || !hasLeak || anyVampireActive || anyLowUnknown)
                {
                    Console.WriteLine("FAIL 6g: Low-res OCR failed (corrupted=" + hasCorrupted + ", leak=" + hasLeak + ", unk=" + anyLowUnknown + ")");
                    return 1;
                }
                Console.WriteLine("    6g Low-res compact OCR (adaptive 4x + sharpen + Vampire/Lucius ignored): OK");
            }

            // 7. Live Roblox log PlaceId & JobId extraction
            string logsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs");
            if (Directory.Exists(logsDir))
            {
                string latestLog = Directory.GetFiles(logsDir, "*_Player_*_last.log")
                    .Where(f => f.IndexOf("CrashHandler", StringComparison.OrdinalIgnoreCase) < 0)
                    .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                    .FirstOrDefault();

                if (!string.IsNullOrEmpty(latestLog))
                {
                    var srv = RobloxLogTracker.ParseLatestServerFromLog(latestLog);
                    Console.WriteLine(string.Format("[7] Live Log Tracker ({0}): PlaceId={1} JobId={2}",
                        srv != null ? srv.LogFileName : "null",
                        srv != null ? srv.PlaceId : "-",
                        srv != null ? srv.JobId : "-"));
                    if (srv == null || !srv.HasServer)
                    {
                        Console.WriteLine("FAIL: Could not extract PlaceId/JobId from live Roblox log.");
                        return 1;
                    }
                }
            }

            // 8. Isolated dummy process tests (handle close + RAM trim + EcoQoS)
            using (var engine = new RobloxHandleEngine())
            {
                Console.WriteLine("[8] Spawning isolated dummy process (48 MB ballast + test Event & Mutex)...");
                string selfExe = Process.GetCurrentProcess().MainModule.FileName;
                var psi = new ProcessStartInfo
                {
                    FileName = selfExe,
                    Arguments = "--dummy",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (var dummy = Process.Start(psi))
                {
                    try
                    {
                        if (dummy.StandardOutput.ReadLine() != "DUMMY_READY")
                        {
                            Console.WriteLine("FAIL: Dummy process did not signal ready.");
                            return 1;
                        }

                        int dummyPid = dummy.Id;
                        var closeRes = engine.CloseHandlesMatchingTokens(dummyPid, new[] { TestEventName, TestMutexName });
                        if (closeRes.ClosedCount != 2)
                        {
                            Console.WriteLine("FAIL: Expected 2 closed handles, got " + closeRes.ClosedCount);
                            return 1;
                        }

                        double wsBefore, wsAfter;
                        string trimErr;
                        bool trimmed = AfkOptimizer.TrimWorkingSet(dummyPid, out wsBefore, out wsAfter, out trimErr);
                        Console.WriteLine(string.Format("    8a Handle close=2 | 8b TrimWorkingSet: {0:0.0} MB -> {1:0.0} MB", wsBefore, wsAfter));
                        if (!trimmed || wsBefore < 40.0 || wsAfter >= wsBefore * 0.6)
                        {
                            Console.WriteLine("FAIL: TrimWorkingSet did not significantly reduce working set.");
                            return 1;
                        }

                        string ecoErr;
                        bool ecoOn = AfkOptimizer.SetEfficiencyMode(dummyPid, true, out ecoErr);
                        dummy.Refresh();
                        bool okOn = ecoOn && dummy.PriorityClass == ProcessPriorityClass.BelowNormal;
                        bool ecoOff = AfkOptimizer.SetEfficiencyMode(dummyPid, false, out ecoErr);
                        dummy.Refresh();
                        bool okOff = ecoOff && dummy.PriorityClass == ProcessPriorityClass.Normal;
                        Console.WriteLine("    8c EcoQoS & Priority toggle: on=" + okOn + " off=" + okOff);
                        if (!okOn || !okOff)
                        {
                            Console.WriteLine("FAIL: EcoQoS / Priority toggle failed.");
                            return 1;
                        }
                    }
                    finally
                    {
                        try { if (!dummy.HasExited) dummy.Kill(); } catch { }
                    }
                }
            }

            Console.WriteLine("ALL TESTS PASSED!");
            return 0;
        }

        private static bool VerifyWatcherSessionTracking()
        {
            string log = Path.GetTempFileName();
            const string join = "[FLog::Network] ! Joining game '1234-5678' place 140627118796712\n";
            try
            {
                File.WriteAllText(log, join + "[FLog::Network] Connection lost\n");
                var session = RobloxLogTracker.ParseLatestServerFromLog(log);
                if (session == null || session.HasServer || session.DirectJoinUri != "")
                {
                    Console.WriteLine("FAIL: Discord join link remained usable after a logged disconnect.");
                    return false;
                }

                File.AppendAllText(log, "[FLog::Network] ! Joining game '2222-3333' place 14891672511\n");
                session = RobloxLogTracker.ParseLatestServerFromLog(log);
                if (session == null || !session.HasServer || session.PlaceId != "14891672511")
                {
                    Console.WriteLine("FAIL: A later join did not replace the disconnected session.");
                    return false;
                }

                Console.WriteLine("[Watcher] Disconnect clears stale join link; later join restores session: OK");
                return true;
            }
            finally
            {
                File.Delete(log);
            }
        }

        private static bool VerifyScheduledFocus()
        {
            var now = new DateTime(2026, 11, 22, 14, 0, 0, DateTimeKind.Utc);
            var overdue = now.AddMinutes(-20);
            bool busyWasInterrupted = MainWindow.CanStartScheduledFocus(now, overdue, false, true);
            bool actionsOverlapped = MainWindow.CanStartScheduledFocus(now, overdue, true, false);
            bool ranEarly = MainWindow.CanStartScheduledFocus(now, now.AddSeconds(1), false, false);
            bool idleRanOnTime = MainWindow.CanStartScheduledFocus(now, now, false, false);
            if (busyWasInterrupted || actionsOverlapped || ranEarly || !idleRanOnTime)
            {
                Console.WriteLine("FAIL: Scheduled focus must wait for its due time, user inactivity and exclusive access.");
                return false;
            }

            Console.WriteLine("[Watcher] Active user remains protected after 20 minutes overdue; focus actions cannot overlap: OK");
            return true;
        }

        private static bool VerifyMainOnlyAntiAfk()
        {
            DateTime now = new DateTime(2026, 11, 22, 14, 0, 0, DateTimeKind.Utc);
            var main = new RobloxInstanceInfo { Role = InstanceRole.Main, WindowHandle = new IntPtr(1) };
            var scout = new RobloxInstanceInfo { Role = InstanceRole.AfkSaver, WindowHandle = new IntPtr(2) };
            var windowless = new RobloxInstanceInfo { Role = InstanceRole.Main };
            var instances = new[] { scout, main, windowless };

            bool mainWhileActive = MainWindow.IsAntiAfkDue(true, now, now, 119999, AntiAfkKeeper.MainIdleMs);
            bool mainWhenIdle = MainWindow.IsAntiAfkDue(true, now, now, 120000, AntiAfkKeeper.MainIdleMs);
            bool mainBeforeDue = MainWindow.IsAntiAfkDue(true, now, now.AddSeconds(1), 120000, AntiAfkKeeper.MainIdleMs);
            bool scoutDisabled = MainWindow.IsAntiAfkDue(false, now, now, 120000, 5000);
            if (mainWhileActive || !mainWhenIdle || mainBeforeDue || scoutDisabled ||
                !MainWindow.SelectAntiAfkTargets(instances, scoutDisabled, mainWhenIdle, false).SequenceEqual(new[] { main }) ||
                !MainWindow.SelectAntiAfkTargets(instances, true, false, false).SequenceEqual(new[] { scout }) ||
                !MainWindow.SelectAntiAfkTargets(instances, true, true, false).SequenceEqual(new[] { main, scout }) ||
                MainWindow.SelectAntiAfkTargets(instances, false, false, false).Count != 0)
            {
                Console.WriteLine("FAIL: MAIN and Scout Anti-AFK must run independently, with MAIN waiting for two minutes of PC idle.");
                return false;
            }

            Console.WriteLine("[Anti-AFK] MAIN-only, Scout-only, both roles, both off and two-minute MAIN idle gate: OK");
            return true;
        }

        private static string FixturePath(string folder, string file)
        {
            return string.IsNullOrWhiteSpace(folder) ? null : Path.Combine(folder, file);
        }

        private static bool VerifyCatalogTypos()
        {
            string[,] cases = {
                { "devil_rift", "Somethlng feels off..." },
                { "spirit_rift", "A strange enerqy feels toward the kingdom." },
                { "khamsin", "[Event] The Sand Saqe has arrived." },
                { "void_wisp", "[Event] A Void Wi5p appears." },
                { "leak_event", "[Leak] Something is Ieaking in the grasslands." },
                { "spirit_wisp", "[Event] A Blood Wi5p appears over the plains." },
                { "strange_scientist", "[Event] The strange scientlst has arrived." },
                { "devil_rumors", "[Event] Someb0dy has seen a silhouette." },
                { "guarded_rift", "[Event] A guarded r1ft opens." },
                { "devil_assault", "[Event] The Devil AssauIt approaches." },
                { "devil_assault", "[Event] The seal has shattered walks the land, and its gaze is" },
                { "devil_assault", "[Event] The seal has shattere E1 walks the land, and its gaze is" },
                { "devil_assault", "[Event] The sea1 has shatterd and the Giant Demon walks the land." },
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
            var config = new RoMultiConfig();
            for (int i = 0; i < cases.GetLength(0); i++)
            {
                var matches = EventCatalog.MatchLines(new[] { cases[i, 1] }, config);
                if (matches.Count != 1 || matches[0].Rule.Id != cases[i, 0])
                {
                    Console.WriteLine("FAIL: Typo case " + cases[i, 0] + " classified as " +
                        string.Join(",", matches.Select(m => m.Rule.Id)) + ": " + cases[i, 1]);
                    return false;
                }
                if (cases[i, 0] == "devil_assault" && matches[0].EffectiveLevel != EventAlertLevel.Ignore)
                {
                    Console.WriteLine("FAIL: Giant Demon should be ignored by default.");
                    return false;
                }
            }
            Console.WriteLine("[OCR] Typo fixtures for all named catalog events and an ambiguous fallback: OK");
            return true;
        }

        private static bool VerifyOcrReplay()
        {
            const string tip = "[Tips] Press semi-colon to open the map.";
            const string warlord = "[Event] The Warlord of the Grasslands steps forward, challenge in his eyes.";
            const string typo = "[Event] The Wariord of the Grasslands steps forward, challenge in bis eyes.";
            const string wisp = "[Event] Mana fluttering can be heard across the grasslands.";
            const string other = "[Event] A silver comet passes over the eastern mountains.";
            var history = new DiscordNotifier();
            var config = new RoMultiConfig();
            DateTime start = new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);
            bool valid = Replay(history, config, start, 0, "", tip) &&
                Replay(history, config, start, 3, "warlord", tip, warlord) &&
                Replay(history, config, start, 6, "", tip, typo) &&
                Replay(history, config, start, 9, "mana_wisp", wisp) &&
                Replay(history, config, start, 12, "", wisp) &&
                Replay(history, config, start, 15, "", wisp) &&
                Replay(history, config, start, 18, "", wisp) &&
                Replay(history, config, start, 21, "", wisp) &&
                Replay(history, config, start, 24, "", typo, wisp) &&
                Replay(history, config, start, 27, "warlord", typo, wisp, warlord);
            for (int seconds = 30; valid && seconds <= 42; seconds += 3)
                valid = Replay(history, config, start, seconds, "", tip);
            valid = valid && Replay(history, config, start, 180, "warlord", tip, warlord) &&
                Replay(history, config, start, 183, "", tip, warlord, other) &&
                Replay(history, config, start, 186, "unknown_event", tip, warlord, other.Replace("comet", "cornet")) &&
                Replay(history, config, start, 189, "", tip, warlord, other) &&
                EventCatalog.MatchLines(new[] { typo + " It retreats." }, config).Count == 0 &&
                EventCatalog.MatchLines(new[] { "Player: " + typo.Substring(8) }, config).Count == 0;
            var interrupted = new DiscordNotifier();
            valid = valid && Replay(interrupted, config, start, 0, "", tip) &&
                Replay(interrupted, config, start, 3, "", tip, other) &&
                Replay(interrupted, config, start, 6, "", "Unlock chat") &&
                Replay(interrupted, config, start, 9, "", tip, other) &&
                Replay(interrupted, config, start, 12, "unknown_event", tip, other);
            if (valid) Console.WriteLine("[OCR] Typo, scroll replay, new spawn and unknown confirmation: OK");
            return valid;
        }

        private static bool Replay(DiscordNotifier history, RoMultiConfig config, DateTime start,
            int seconds, string expected, params string[] lines)
        {
            List<string> timeline;
            bool baselined;
            int baselineCount;
            var matches = EventCatalog.MatchAllOccurrences(lines, config, out timeline);
            string actual = string.Join(",", history.FilterNewOccurrencesForPid(100, "replay-job", timeline, matches,
                false, out baselined, out baselineCount, start.AddSeconds(seconds)).Select(x => x.Rule.Id));
            if (actual == expected) return true;
            Console.WriteLine("FAIL: Replay at " + seconds + "s expected [" + expected + "], got [" + actual + "].");
            return false;
        }

        private static bool VerifyStarBroadcasts()
        {
            var history = new DiscordNotifier();
            var config = new RoMultiConfig();
            DateTime start = new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);
            const string tip = "[Tips] Press semi-colon to open the map.";
            bool valid = Replay(history, config, start, 0, "", tip) &&
                Replay(history, config, start, 3, "star_4", "[Event] Four stars glow, marking the corners of a journey fraught with adversaries, yet") &&
                Replay(history, config, start, 6, "", "[Event] Four stars glow, marking the corne of a journey fraught with adversaries, yet") &&
                Replay(history, config, start, 9, "", "[Event] Four stars gl of a journey fraught") &&
                Replay(history, config, start, 12, "", "[Event] The corruption fades, and the night releases its grip. Order is restored, for no") &&
                Replay(history, config, start, 15, "", "[Event] The corrupti releases its grip. Ord") &&
                Replay(history, config, start, 18, "", "[Event] The corrupti releases its grip. Ord") &&
                Replay(history, config, start, 21, "corrupted_night", "[Event] The air grows heavy as darkness takes hold.") &&
                Replay(history, config, start, 24, "green_star", "[Event] A green star rises.");
            string[] names = { "One", "Two", "Three", "Four", "Five", "Six", "Seven" };
            for (int i = 0; valid && i < names.Length; i++)
            {
                string suffix = i == 0 ? " star glows." : " stars glow.";
                string id = "star_" + (i + 1);
                var named = new DiscordNotifier();
                valid = Replay(named, config, start, 0, "", tip) &&
                    Replay(named, config, start, 3, id, "[Event] " + names[i] + suffix) &&
                    Replay(named, config, start, 6, "", "[Event] " + (i + 1) + suffix);
            }
            if (valid) Console.WriteLine("[OCR] Stars 1-7, Four Stars OCR duplicates and Corrupted Night ending: OK");
            return valid;
        }

        private static bool VerifyVampireCastleIgnored()
        {
            var config = new RoMultiConfig();
            var vampireChat = new[]
            {
                "[Event] Stronger vampires can now be seen around the castle.",
                "[Event] The Elite of the vampires has been defeated.",
                "[Event] The Elite of the vampires appears near the castle.",
                "[Event] A shadow deepens across the land as Vampire Lord Lucius awakens,",
                "bringing an ancient darkness with him.",
                "[Event] Lucius falls, his dark reign fading into the shadows.",
                "[Event] Lucius falls, his dark reign",
                "[Tips] You can turn shiftlock on and off with the Left Control key."
            };
            var matches = EventCatalog.MatchAllOccurrences(vampireChat, config);
            if (matches.Any(m => m.EffectiveLevel != EventAlertLevel.Ignore) ||
                matches.Any(m => m.Rule.Id == "unknown_event") ||
                !matches.Any(m => m.Rule.Id == "vampire_castle"))
            {
                Console.WriteLine("FAIL: Vampire/Lucius events must all be ignored and never leak into alerts: " +
                    string.Join(", ", matches.Select(m => m.Rule.Id + ":" + m.EffectiveLevel)));
                return false;
            }
            Console.WriteLine("[OCR] Stronger vampire, Elite vampire, Shadow deepens & Lord Lucius ignored: OK");
            return true;
        }

        private static bool VerifyCompactDiscordPayload()
        {
            const string user = "123456789012345678";
            const string fixtureCode = "server-demo";
            var config = new RoMultiConfig();
            var rare = EventCatalog.MatchLines(new[] { "Something feels off..." }, config).Single();
            var regular = EventCatalog.MatchLines(new[] { "[Event] A new eclipse descends upon the clover realm!" }, config).Single();
            var instance = new RobloxInstanceInfo { ProcessId = 916, Label = "Demo Scout" };
            var server = new RobloxServerInfo { PlaceId = "14839995292", JobId = "mock-job" };
            var priority = DiscordNotifier.BuildEventPayload(user, rare, instance, server, true, " " + fixtureCode + " ");
            var embed = ((Dictionary<string, object>[])priority["embeds"]).Single();
            var fields = (List<Dictionary<string, object>>)embed["fields"];
            var allowed = (Dictionary<string, object>)priority["allowed_mentions"];
            string serverField = fields.Single(field => (string)field["name"] == "Server")["value"] as string;
            string footer = ((Dictionary<string, object>)embed["footer"])["text"] as string;
            var normal = DiscordNotifier.BuildEventPayload(user, regular, instance, null, false, "");
            var noServer = ((Dictionary<string, object>[])normal["embeds"]).Single();
            string unusedCode;
            if ((string)priority["content"] != "<@" + user + ">" || ((string[])allowed["users"]).Single() != user ||
                fields.Count != 2 || (string)fields.Single(field => (string)field["name"] == "Private server")["value"] != fixtureCode ||
                !serverField.Contains(server.PlaceId) || !serverField.Contains(server.JobId) || !serverField.Contains(server.DirectJoinUri) ||
                !footer.Contains("Demo Scout") || !footer.Contains("916") || !footer.Contains("Priority") ||
                embed.ContainsKey("description") || !embed.ContainsKey("timestamp") ||
                !noServer.ContainsKey("description") || !((string)noServer["description"]).Contains(regular.RawLine) ||
                (string)((Dictionary<string, object>)embed["image"])["url"] != "attachment://chat.png" ||
                normal.ContainsKey("content") || noServer.ContainsKey("fields") || noServer.ContainsKey("image") ||
                DiscordNotifier.TryNormalizePrivateServerCode("bad server", out unusedCode))
            {
                Console.WriteLine("FAIL: Compact Discord embed lost an event detail, kept description on registered event, or omitted description on unknown_event.");
                return false;
            }
            Console.WriteLine("[Discord] Compact event (no OCR description on registered events, description shown on Other [Event] Broadcast): OK");
            return true;
        }

        private static bool VerifyFrozenCapture()
        {
            var frame = new PassiveFrameHistory();
            var now = new DateTime(2026, 11, 22, 14, 0, 0, DateTimeKind.Utc);
            if (frame.IsUnchanged(new byte[] { 1, 2, 3 }, now) ||
                frame.IsUnchanged(new byte[] { 1, 2, 3 }, now.AddSeconds(8)) ||
                !frame.IsUnchanged(new byte[] { 1, 2, 3 }, now.AddSeconds(16)) ||
                frame.IsUnchanged(new byte[] { 1, 2, 4 }, now.AddSeconds(17)))
            {
                Console.WriteLine("FAIL: A repeated passive frame should be flagged after 15 seconds and a fresh frame should clear it.");
                return false;
            }
            Console.WriteLine("[Watcher] Frozen frame recognized; fresh pixels clear the flag: OK");
            return true;
        }
    }
}

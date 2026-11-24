using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace RoMulti
{
    public sealed class DiscordSendResult
    {
        public bool Sent { get; set; }
        public bool SkippedAsDuplicate { get; set; }
        public string Message { get; set; }
    }

    public sealed class DiscordNotifier
    {
        private sealed class TimelineAnchor
        {
            public string PrevSig = "";
            public string NextSig = "";
            public DateTime RecordedAtUtc;
        }

        private sealed class KeyTrackState
        {
            public int VisibleCount;
            public int LowerStreak;
            public int PendingCount;
            public DateTime PendingAtUtc;
            public DateTime LastSeenUtc;
            public string Body = "";
            public List<TimelineAnchor> Anchors = new List<TimelineAnchor>();

            public bool SeenRecently(DateTime now)
            {
                return LastSeenUtc != DateTime.MinValue && now - LastSeenUtc < ScrollReplayWindow;
            }

            public bool SeenWithinMemory(DateTime now)
            {
                return LastSeenUtc != DateTime.MinValue && now - LastSeenUtc < ForgetInvisibleAfter;
            }

            public void RecordAnchor(string prevSig, string nextSig, DateTime now)
            {
                Anchors.RemoveAll(a => now - a.RecordedAtUtc >= ForgetInvisibleAfter);
                for (int i = 0; i < Anchors.Count; i++)
                {
                    if (SameNeighbor(Anchors[i].PrevSig, prevSig) && SameNeighbor(Anchors[i].NextSig, nextSig))
                    {
                        Anchors[i].RecordedAtUtc = now;
                        return;
                    }
                }
                Anchors.Add(new TimelineAnchor { PrevSig = prevSig, NextSig = nextSig, RecordedAtUtc = now });
                if (Anchors.Count > 12) Anchors.RemoveAt(0);
            }
        }

        private sealed class PidWatcherState
        {
            public bool BaselineInitialized;
            public DateTime FirstSeenUtc;
            public string LastJobId;
            public Dictionary<string, KeyTrackState> Keys = new Dictionary<string, KeyTrackState>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, DateTime> SeenEventLines = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> LastScanLineSigs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static readonly HttpClient Http = CreateHttpClient();
        private readonly object _dedupeLock = new object();
        private readonly Dictionary<string, DateTime> _sentRecently = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, PidWatcherState> _pidStates = new Dictionary<int, PidWatcherState>();
        private static readonly TimeSpan DedupeCooldown = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ScrollReplayWindow = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan ForgetInvisibleAfter = TimeSpan.FromMinutes(30);
        private const string UnknownPrefix = "unknown:";
        private static readonly Regex PrivateServerPattern = new Regex(@"\A[A-Za-z0-9_-]{0,128}\z");
        private static readonly Regex SingleUserMention = new Regex(@"\A<@!?([1-9][0-9]{14,19})>\z");
        private const int LowerStreakThreshold = 5;

        public void ResetBaselines()
        {
            lock (_dedupeLock)
            {
                _pidStates.Clear();
            }
        }

        public void PruneExitedPids(HashSet<int> activePids)
        {
            if (activePids == null) return;
            lock (_dedupeLock)
            {
                var dead = new List<int>();
                foreach (int pid in _pidStates.Keys)
                {
                    if (!activePids.Contains(pid))
                        dead.Add(pid);
                }
                foreach (int pid in dead)
                    _pidStates.Remove(pid);
            }
        }

        public List<MatchedEvent> FilterNewOccurrencesForPid(
            int pid,
            string jobId,
            List<string> stitchedTimeline,
            List<MatchedEvent> visibleOccurrences,
            bool waitForAutoPeekBaseline,
            out bool initializedBaseline,
            out int baselineEventCount,
            DateTime? nowOverrideUtc = null)
        {
            initializedBaseline = false;
            baselineEventCount = 0;

            var toEmit = new List<MatchedEvent>();
            DateTime now = nowOverrideUtc.HasValue ? nowOverrideUtc.Value : DateTime.UtcNow;

            // Strictly verify ACTUAL chat messages are visible (not merely Jungle's persistent
            // "Here | Global | Friends" / "To chat click here or press / key" UI chrome!).
            bool hasReadableChat = HasReadableChatMessages(stitchedTimeline, visibleOccurrences);

            lock (_dedupeLock)
            {
                PidWatcherState pidState;
                if (!_pidStates.TryGetValue(pid, out pidState))
                {
                    pidState = new PidWatcherState
                    {
                        BaselineInitialized = false,
                        FirstSeenUtc = now,
                        LastJobId = jobId
                    };
                    _pidStates[pid] = pidState;
                }
                else if (!string.IsNullOrEmpty(jobId))
                {
                    if (!string.IsNullOrEmpty(pidState.LastJobId) &&
                        !string.Equals(pidState.LastJobId, jobId, StringComparison.OrdinalIgnoreCase))
                    {
                        // Server hopped / teleported to a brand-new Roblox server:
                        // chat starts fresh, so clear previous server's event counts & timeline memory while keeping baseline armed!
                        pidState.Keys.Clear();
                        pidState.SeenEventLines.Clear();
                        pidState.LastScanLineSigs.Clear();
                        pidState.BaselineInitialized = true;
                        pidState.LastJobId = jobId;
                    }
                    else if (string.IsNullOrEmpty(pidState.LastJobId))
                    {
                        pidState.LastJobId = jobId;
                    }
                }

                // Group visible occurrences by canonical key (fuzzy-grouping unknown:* Catch-All variants)
                var grouped = GroupOccurrencesByCanonicalKey(visibleOccurrences, pidState.Keys, now);

                var timelineSigs = BuildTimelineSignatures(stitchedTimeline);

                if (!pidState.BaselineInitialized)
                {
                    if (hasReadableChat)
                    {
                        pidState.BaselineInitialized = true;
                        foreach (var kv in grouped)
                        {
                            var state = new KeyTrackState
                            {
                                VisibleCount = kv.Value.Count,
                                LastSeenUtc = now,
                                Body = EventCatalog.BroadcastBodyKey(kv.Value[kv.Value.Count - 1].RawLine)
                            };
                            foreach (var occ in kv.Value)
                            {
                                string prevSig, nextSig;
                                GetNeighborSignatures(timelineSigs, occ.TimelineIndex, out prevSig, out nextSig);
                                state.RecordAnchor(prevSig, nextSig, now);
                            }
                            pidState.Keys[kv.Key] = state;
                        }
                        RecordTimelineMemory(pidState, stitchedTimeline, timelineSigs, now);
                        initializedBaseline = true;
                        baselineEventCount = visibleOccurrences != null ? visibleOccurrences.Count : 0;
                        return toEmit;
                    }

                    // Chat is currently faded out / empty on startup.
                    double graceSec = waitForAutoPeekBaseline ? 75.0 : 15.0;
                    if ((now - pidState.FirstSeenUtc).TotalSeconds >= graceSec)
                    {
                        pidState.BaselineInitialized = true;
                        initializedBaseline = true;
                        baselineEventCount = 0;
                    }
                    return toEmit;
                }

                // Once baseline is armed, a temporarily faded chat box (0 message lines, even if chrome remains!)
                // MUST freeze all VisibleCount values so unfading the same old chat history NEVER re-alerts!
                if (!hasReadableChat)
                {
                    foreach (var tracked in pidState.Keys.Values) tracked.PendingCount = 0;
                    return toEmit;
                }

                // Process keys currently visible in chat
                foreach (var kv in grouped)
                {
                    string key = kv.Key;
                    var list = kv.Value;
                    int curCount = list.Count;
                    var newest = list[list.Count - 1];

                    KeyTrackState tracked;
                    if (!pidState.Keys.TryGetValue(key, out tracked))
                    {
                        tracked = new KeyTrackState();
                        pidState.Keys.Add(key, tracked);
                    }
                    string body = EventCatalog.BroadcastBodyKey(newest.RawLine);
                    string newestPrevSig, newestNextSig;
                    GetNeighborSignatures(timelineSigs, newest.TimelineIndex, out newestPrevSig, out newestNextSig);
                    if (curCount > tracked.VisibleCount)
                    {
                        if (newest.Rule.IsCatchAll)
                        {
                            TimeSpan timePending = now - tracked.PendingAtUtc;
                            if (tracked.PendingCount != curCount || timePending > ScrollReplayWindow)
                            {
                                tracked.PendingCount = curCount;
                                tracked.PendingAtUtc = now;
                                continue;
                            }
                            if (timePending.TotalSeconds < 1) continue;
                        }
                        bool recentReappearance = tracked.VisibleCount == 0 && tracked.SeenRecently(now);
                        bool alreadyReportedUnderOtherName = !newest.Rule.IsCatchAll && tracked.VisibleCount == 0 &&
                            ResemblesRecent(body, pidState.Keys, now, unknown: true);
                        bool oldTimelineReplay = tracked.SeenWithinMemory(now) &&
                            IsOldTimelineReplay(tracked, pidState, stitchedTimeline, timelineSigs, newest.TimelineIndex, newestPrevSig, newestNextSig, now);
                        if (!recentReappearance && !alreadyReportedUnderOtherName && !oldTimelineReplay) toEmit.Add(newest);
                        tracked.VisibleCount = curCount;
                        tracked.LowerStreak = 0;
                    }
                    else if (curCount == tracked.VisibleCount)
                    {
                        // Exact same count still visible on screen -> reset lower streak and NEVER re-alert!
                        tracked.LowerStreak = 0;
                    }
                    else
                    {
                        // curCount < tracked.VisibleCount: require 5 consecutive message-bearing scans (~15s)
                        // before lowering so OCR blips or fade transitions NEVER cause false re-alerts!
                        tracked.LowerStreak++;
                        if (tracked.LowerStreak >= LowerStreakThreshold)
                        {
                            tracked.VisibleCount = curCount;
                            tracked.LowerStreak = 0;
                        }
                    }
                    tracked.PendingCount = 0;
                    tracked.LastSeenUtc = now;
                    tracked.Body = body;
                    foreach (var occ in list)
                    {
                        string pSig, nSig;
                        GetNeighborSignatures(timelineSigs, occ.TimelineIndex, out pSig, out nSig);
                        tracked.RecordAnchor(pSig, nSig, now);
                    }
                }

                // Handle tracked keys that are no longer visible on screen (curCount == 0)
                var forgotten = new List<string>();
                foreach (var kv in pidState.Keys)
                {
                    if (grouped.ContainsKey(kv.Key)) continue;
                    var tracked = kv.Value;
                    tracked.PendingCount = 0;
                    if (tracked.LastSeenUtc == DateTime.MinValue || now - tracked.LastSeenUtc >= ForgetInvisibleAfter)
                    {
                        forgotten.Add(kv.Key);
                        continue;
                    }
                    if (tracked.VisibleCount > 0)
                    {
                        tracked.LowerStreak++;
                        if (tracked.LowerStreak >= LowerStreakThreshold)
                        {
                            tracked.VisibleCount = 0;
                            tracked.LowerStreak = 0;
                        }
                    }
                }
                foreach (string key in forgotten) pidState.Keys.Remove(key);
                RecordTimelineMemory(pidState, stitchedTimeline, timelineSigs, now);
            }

            toEmit.Sort((a, b) => a.TimelineIndex.CompareTo(b.TimelineIndex));
            return toEmit;
        }

        internal static bool HasReadableChatMessages(List<string> rawTimeline, List<MatchedEvent> visibleOccurrences)
        {
            if (visibleOccurrences != null && visibleOccurrences.Count > 0)
                return true;
            if (rawTimeline == null || rawTimeline.Count == 0)
                return false;

            foreach (string raw in rawTimeline)
            {
                string norm = EventCatalog.NormalizeForMatch(raw);

                // Explicitly IGNORE static Roblox chat chrome ("To chat click here", "Unlock chat", "Here Global Friends")
                // because chrome stays visible AFTER messages fade out in Jungle!
                if (norm.IndexOf("to chat click", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    norm.IndexOf("unlock chat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    norm.IndexOf("here global", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                if (raw.IndexOf("[Tips", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    raw.IndexOf("[System", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    raw.IndexOf("[Event", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    raw.IndexOf("[Leak", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    raw.IndexOf("[Clover", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static Dictionary<string, List<MatchedEvent>> GroupOccurrencesByCanonicalKey(
            List<MatchedEvent> visibleOccurrences,
            Dictionary<string, KeyTrackState> existingKeys,
            DateTime now)
        {
            var grouped = new Dictionary<string, List<MatchedEvent>>(StringComparer.OrdinalIgnoreCase);
            if (visibleOccurrences == null) return grouped;

            var namedBodies = visibleOccurrences.Where(ev => ev != null && !ev.Rule.IsCatchAll)
                .Select(ev => EventCatalog.BroadcastBodyKey(ev.RawLine)).ToList();
            foreach (var ev in visibleOccurrences)
            {
                if (ev == null || string.IsNullOrEmpty(ev.DedupeKey)) continue;
                string canonical = ev.DedupeKey;

                if (canonical.StartsWith(UnknownPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string body = canonical.Substring(UnknownPrefix.Length);
                    if (namedBodies.Any(named => EventCatalog.SameBroadcastText(body, named)) ||
                        ResemblesRecent(body, existingKeys, now, unknown: false))
                        continue;
                    if (!existingKeys.ContainsKey(canonical) && !grouped.ContainsKey(canonical))
                        canonical = existingKeys.Keys.Concat(grouped.Keys).FirstOrDefault(key =>
                            key.StartsWith(UnknownPrefix, StringComparison.OrdinalIgnoreCase) &&
                            EventCatalog.SameBroadcastText(key.Substring(UnknownPrefix.Length), body)) ?? canonical;
                }

                List<MatchedEvent> list;
                if (!grouped.TryGetValue(canonical, out list))
                {
                    list = new List<MatchedEvent>();
                    grouped[canonical] = list;
                }
                list.Add(ev);
            }

            foreach (var kv in grouped)
                kv.Value.Sort((a, b) => a.TimelineIndex.CompareTo(b.TimelineIndex));

            return grouped;
        }

        private static List<string> BuildTimelineSignatures(List<string> timeline)
        {
            var sigs = new List<string>();
            if (timeline == null) return sigs;
            for (int i = 0; i < timeline.Count; i++)
            {
                string norm = EventCatalog.NormalizeForMatch(timeline[i]);
                if (norm.Length > 120) norm = norm.Substring(0, 120).Trim();
                sigs.Add(norm);
            }
            return sigs;
        }

        private static bool IsGenericTipOrSystemSig(string sig)
        {
            if (string.IsNullOrEmpty(sig)) return true;
            return sig.StartsWith("tips ", StringComparison.OrdinalIgnoreCase) ||
                sig.StartsWith("tip ", StringComparison.OrdinalIgnoreCase) ||
                sig.StartsWith("system ", StringComparison.OrdinalIgnoreCase) ||
                sig.StartsWith("clover system ", StringComparison.OrdinalIgnoreCase) ||
                sig.Contains("to chat click") ||
                sig.Contains("unlock chat") ||
                sig.Contains("here global");
        }

        private static void GetNeighborSignatures(List<string> sigs, int index, out string prevSig, out string nextSig)
        {
            prevSig = (sigs != null && index > 0 && index - 1 < sigs.Count) ? sigs[index - 1] : "";
            nextSig = (sigs != null && index >= 0 && index + 1 < sigs.Count) ? sigs[index + 1] : "";
        }

        private static bool SameNeighbor(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b);
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase) || EventCatalog.SameBroadcastText(a, b);
        }

        private static bool IsLineInMemory(string sig, Dictionary<string, DateTime> memory, DateTime now)
        {
            if (string.IsNullOrEmpty(sig) || sig.Length < 12) return false;
            DateTime seenAt;
            if (memory.TryGetValue(sig, out seenAt) && now - seenAt < ForgetInvisibleAfter)
                return true;
            foreach (var kv in memory)
            {
                if (now - kv.Value >= ForgetInvisibleAfter) continue;
                if (EventCatalog.SameBroadcastText(sig, kv.Key))
                    return true;
            }
            return false;
        }

        private static bool IsLineInSet(string sig, HashSet<string> set)
        {
            if (string.IsNullOrEmpty(sig) || sig.Length < 12 || set == null || set.Count == 0) return false;
            if (set.Contains(sig)) return true;
            foreach (string existing in set)
            {
                if (EventCatalog.SameBroadcastText(sig, existing))
                    return true;
            }
            return false;
        }

        private static bool IsOldTimelineReplay(
            KeyTrackState tracked,
            PidWatcherState pidState,
            List<string> timeline,
            List<string> timelineSigs,
            int timelineIndex,
            string prevSig,
            string nextSig,
            DateTime now)
        {
            if (timelineSigs == null || timelineIndex < 0 || timelineIndex >= timelineSigs.Count)
                return false;

            // Rule M1: An already-seen event that reappears ABOVE a previously seen chat message
            // (a non-tip event/leak line from an earlier scan, OR any line already visible on the
            // immediately preceding scan) is physically an OLD message in chat history, never a new spawn!
            for (int below = timelineIndex + 1; below < timelineSigs.Count; below++)
            {
                string belowSig = timelineSigs[below];
                if (string.IsNullOrEmpty(belowSig) || belowSig.Length < 12) continue;
                if (!IsGenericTipOrSystemSig(belowSig) && IsLineInMemory(belowSig, pidState.SeenEventLines, now))
                    return true;
                if (IsLineInSet(belowSig, pidState.LastScanLineSigs))
                    return true;
            }

            // Rule M2: Same physical chat slot remembered in Anchors within 30 minutes
            // (either both Prev & Next neighbors match, or a non-generic Prev event/chat neighbor matches).
            for (int i = 0; i < tracked.Anchors.Count; i++)
            {
                var anchor = tracked.Anchors[i];
                if (now - anchor.RecordedAtUtc >= ForgetInvisibleAfter) continue;
                bool prevMatch = !string.IsNullOrEmpty(prevSig) && !string.IsNullOrEmpty(anchor.PrevSig) &&
                    SameNeighbor(anchor.PrevSig, prevSig);
                bool nextMatch = !string.IsNullOrEmpty(nextSig) && !string.IsNullOrEmpty(anchor.NextSig) &&
                    SameNeighbor(anchor.NextSig, nextSig);
                if (prevMatch && nextMatch)
                    return true;
                if (prevMatch && !IsGenericTipOrSystemSig(prevSig) && (string.IsNullOrEmpty(anchor.NextSig) || nextMatch))
                    return true;
            }

            return false;
        }

        private static void RecordTimelineMemory(PidWatcherState pidState, List<string> timeline, List<string> sigs, DateTime now)
        {
            var expired = new List<string>();
            foreach (var kv in pidState.SeenEventLines)
            {
                if (now - kv.Value >= ForgetInvisibleAfter)
                    expired.Add(kv.Key);
            }
            foreach (string k in expired) pidState.SeenEventLines.Remove(k);

            pidState.LastScanLineSigs.Clear();
            if (sigs == null) return;
            for (int i = 0; i < sigs.Count; i++)
            {
                string sig = sigs[i];
                if (string.IsNullOrEmpty(sig) || sig.Length < 12) continue;
                pidState.LastScanLineSigs.Add(sig);
                if (!IsGenericTipOrSystemSig(sig))
                    pidState.SeenEventLines[sig] = now;
            }
        }

        private static bool ResemblesRecent(string body, Dictionary<string, KeyTrackState> existing, DateTime now, bool unknown)
        {
            return existing.Any(entry => entry.Key.StartsWith(UnknownPrefix, StringComparison.OrdinalIgnoreCase) == unknown &&
                entry.Value.SeenRecently(now) && EventCatalog.SameBroadcastText(body, entry.Value.Body));
        }

        private static HttpClient CreateHttpClient()
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
            catch { }

            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(12);
            return client;
        }

        public bool IsDuplicateAndRecord(int pid, string dedupeKey, bool recordIfNew)
        {
            string fullKey = pid + "|" + dedupeKey;
            DateTime now = DateTime.UtcNow;

            lock (_dedupeLock)
            {
                // Prune expired entries
                var expired = new List<string>();
                foreach (var kv in _sentRecently)
                {
                    if ((now - kv.Value) > DedupeCooldown)
                        expired.Add(kv.Key);
                }
                foreach (string k in expired)
                    _sentRecently.Remove(k);

                DateTime lastSent;
                if (_sentRecently.TryGetValue(fullKey, out lastSent) && (now - lastSent) <= DedupeCooldown)
                {
                    // Sliding cooldown: while the exact same old chat line is STILL sitting on screen
                    // in a quiet server, keep refreshing the timestamp so it never re-sends 5 min later!
                    _sentRecently[fullKey] = now;
                    return true;
                }

                // Fuzzy prefix/word-overlap dedupe for Catch-All ("unknown:...") events on the same PID
                // Prevents partial OCR truncations (e.g. "whispers rise oneofth" vs "whispers rise oneofth band lord")
                // from sending duplicate Discord webhooks within the cooldown!
                const string unknownPrefix = "unknown:";
                if (dedupeKey != null && dedupeKey.StartsWith(unknownPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string pidPrefix = pid + "|" + unknownPrefix;
                    string newBody = dedupeKey.Substring(unknownPrefix.Length).Trim();
                    string matchedKey = null;
                    foreach (var kv in _sentRecently)
                    {
                        if (!kv.Key.StartsWith(pidPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                        if ((now - kv.Value) > DedupeCooldown) continue;

                        string oldBody = kv.Key.Substring(pidPrefix.Length).Trim();
                        if (AreSimilarUnknownTexts(oldBody, newBody))
                        {
                            matchedKey = kv.Key;
                            break;
                        }
                    }
                    if (matchedKey != null)
                    {
                        _sentRecently[matchedKey] = now;
                        return true;
                    }
                }

                if (recordIfNew)
                {
                    _sentRecently[fullKey] = now;
                }
                return false;
            }
        }

        private static bool AreSimilarUnknownTexts(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            if (a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase))
                return true;

            // Same leading >= 10 characters (handles trailing OCR clipping/noise)
            int minLen = Math.Min(a.Length, b.Length);
            if (minLen >= 10)
            {
                int commonPrefix = 0;
                while (commonPrefix < minLen && char.ToLowerInvariant(a[commonPrefix]) == char.ToLowerInvariant(b[commonPrefix]))
                    commonPrefix++;
                if (commonPrefix >= 10)
                    return true;
            }

            // Word overlap >= 60% of the shorter message
            var wordsA = new HashSet<string>(a.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
            var wordsB = new HashSet<string>(b.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
            int minWords = Math.Min(wordsA.Count, wordsB.Count);
            if (minWords == 0) return false;
            int overlap = 0;
            foreach (string w in wordsA)
            {
                if (wordsB.Contains(w)) overlap++;
            }
            return ((double)overlap / minWords) >= 0.6;
        }

        public static string NormalizePingText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "@everyone";
            string s = raw.Trim();
            if (s.Equals("@everyone", StringComparison.OrdinalIgnoreCase) || s.Equals("@here", StringComparison.OrdinalIgnoreCase))
                return s;
            if (s.StartsWith("<@") && s.EndsWith(">"))
                return s;
            string digits = s.StartsWith("@") ? s.Substring(1) : s;
            bool allDigits = digits.Length >= 15 && digits.Length <= 22;
            for (int i = 0; allDigits && i < digits.Length; i++)
            {
                if (digits[i] < '0' || digits[i] > '9') allDigits = false;
            }
            if (allDigits)
                return "<@" + digits + ">";
            return s;
        }

        internal static bool TryNormalizePrivateServerCode(string raw, out string code)
        {
            code = (raw ?? "").Trim();
            return PrivateServerPattern.IsMatch(code);
        }

        private static void SetPayloadMention(Dictionary<string, object> payload, string mention)
        {
            var allowed = new Dictionary<string, object> { { "parse", new string[0] } };
            payload["allowed_mentions"] = allowed;
            if (string.IsNullOrWhiteSpace(mention)) return;
            payload["content"] = mention;
            if (mention.Equals("@everyone", StringComparison.OrdinalIgnoreCase) ||
                mention.Equals("@here", StringComparison.OrdinalIgnoreCase)) allowed["parse"] = new[] { "everyone" };
            var user = SingleUserMention.Match(mention);
            if (user.Success) allowed["users"] = new[] { user.Groups[1].Value };
        }

        private static Dictionary<string, object> EmbedField(string name, string value, bool inline)
        {
            return new Dictionary<string, object>
            {
                { "name", name }, { "value", value }, { "inline", inline }
            };
        }

        private static void AddPrivateServerField(List<Dictionary<string, object>> fields, string privateServerCode)
        {
            string code;
            if (TryNormalizePrivateServerCode(privateServerCode, out code) && code.Length > 0)
                fields.Add(EmbedField("Private server", code, true));
        }

        public async Task<DiscordSendResult> SendEventAlertAsync(
            string webhookUrl,
            string pingText,
            MatchedEvent ev,
            RobloxInstanceInfo inst,
            RobloxServerInfo server,
            byte[] chatScreenshotPng,
            bool skipTimeDedupe = false,
            string privateServerCode = null)
        {
            var res = new DiscordSendResult();

            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                res.Message = "Discord Webhook URL is empty.";
                return res;
            }

            if (!skipTimeDedupe && IsDuplicateAndRecord(inst.ProcessId, ev.DedupeKey, recordIfNew: false))
            {
                res.SkippedAsDuplicate = true;
                res.Message = "Duplicate event suppressed (within 5 min cooldown).";
                return res;
            }

            var payload = BuildEventPayload(pingText, ev, inst, server,
                chatScreenshotPng != null && chatScreenshotPng.Length > 0, privateServerCode);
            var sendRes = await PostPayloadAsync(webhookUrl, payload, chatScreenshotPng);
            if (sendRes.Sent && !skipTimeDedupe)
                IsDuplicateAndRecord(inst.ProcessId, ev.DedupeKey, recordIfNew: true);
            return sendRes;
        }

        internal static Dictionary<string, object> BuildEventPayload(string pingText, MatchedEvent ev,
            RobloxInstanceInfo inst, RobloxServerInfo server, bool attachImage, string privateServerCode)
        {
            bool isPing = ev.EffectiveLevel == EventAlertLevel.Ping;
            var fields = new List<Dictionary<string, object>>();
            AddPrivateServerField(fields, privateServerCode);

            if (server != null && server.HasServer)
            {
                fields.Add(EmbedField("Server", string.Format("Place ID: `{0}` · Job ID: `{1}`\nJoin URI: `{2}`",
                    server.PlaceId, server.JobId, server.DirectJoinUri), false));
            }

            var embed = new Dictionary<string, object>
            {
                { "title", (isPing ? "[PRIORITY] " : "") + ev.Rule.DisplayName },
                { "color", ev.Rule.EmbedColor },
                { "timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") },
                { "footer", new Dictionary<string, object> { { "text", string.Format(
                    "{0} · PID {1} · {2} · Clover Event Detector", Truncate(inst.Label, 100), inst.ProcessId,
                    isPing ? "Priority" : "Normal") } } }
            };

            if (ev.Rule.IsCatchAll && !string.IsNullOrWhiteSpace(ev.RawLine))
            {
                embed["description"] = Truncate(ev.RawLine, 300);
            }

            if (fields.Count > 0) embed["fields"] = fields;
            if (attachImage)
            {
                embed["image"] = new Dictionary<string, object> { { "url", "attachment://chat.png" } };
            }

            var payload = new Dictionary<string, object>
            {
                { "username", "Clover Event Detector" },
                { "embeds", new[] { embed } }
            };
            SetPayloadMention(payload, isPing ? NormalizePingText(pingText) : null);
            return payload;
        }

        public async Task<DiscordSendResult> SendMapChangeAlertAsync(
            string webhookUrl,
            string pingText,
            RobloxInstanceInfo inst,
            string oldPlaceId,
            RobloxServerInfo newServer,
            string privateServerCode = null)
        {
            var res = new DiscordSendResult();
            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                res.Message = "Discord Webhook URL is empty.";
                return res;
            }

            string newPlace = newServer != null ? newServer.PlaceId : "unknown";
            string key = "mapchange:" + oldPlaceId + "->" + newPlace;
            if (IsDuplicateAndRecord(inst.ProcessId, key, recordIfNew: false))
            {
                res.SkippedAsDuplicate = true;
                return res;
            }

            bool isCloverLibrary = (newPlace == RobloxServerInfo.CloverLibraryPlaceId);
            var embed = new Dictionary<string, object>
            {
                { "title", isCloverLibrary ? "AFK Scout Sent to Clover Library!" : "AFK Scout Teleported / Map Changed" },
                { "description", string.Format("**{0}** (PID `{1}`) moved from Place `{2}` to `{3}`{4}.",
                    inst.Label, inst.ProcessId, oldPlaceId, newPlace,
                    isCloverLibrary ? " (**Clover Library / AFK World**)" : "") },
                { "color", isCloverLibrary ? 0xEF4444 : 0xF59E0B },
                { "timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") },
                { "footer", new Dictionary<string, object> { { "text", "RoMulti Log Tracker" } } }
            };

            var fields = new List<Dictionary<string, object>>();
            AddPrivateServerField(fields, privateServerCode);
            if (fields.Count > 0) embed["fields"] = fields;
            var payload = new Dictionary<string, object>
            {
                { "username", "RoMulti Scout" },
                { "embeds", new[] { embed } }
            };
            SetPayloadMention(payload, isCloverLibrary ? NormalizePingText(pingText) : null);

            var sendRes = await PostPayloadAsync(webhookUrl, payload, null);
            if (sendRes.Sent)
            {
                IsDuplicateAndRecord(inst.ProcessId, key, recordIfNew: true);
            }
            return sendRes;
        }

        public async Task<DiscordSendResult> SendTestWebhookAsync(string webhookUrl, string pingText, string privateServerCode = null)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                return new DiscordSendResult { Sent = false, Message = "Enter a Discord Webhook URL first." };
            }

            var embed = new Dictionary<string, object>
            {
                { "title", "RoMulti Event Watcher — Test Connected" },
                { "description", "Webhook connected successfully! Priority targets armed:\n- **Devil Rift** (`Something feels off...`) — PING\n- **Spirit Rift** (`strange energy feel toward kingdom`) — PING" },
                { "color", 0x22C55E },
                { "timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") },
                { "footer", new Dictionary<string, object> { { "text", "RoMulti v2 — Clover Retribution Scout" } } }
            };

            var fields = new List<Dictionary<string, object>>();
            AddPrivateServerField(fields, privateServerCode);
            if (fields.Count > 0) embed["fields"] = fields;
            var payload = new Dictionary<string, object>
            {
                { "username", "RoMulti Scout" },
                { "embeds", new[] { embed } }
            };
            SetPayloadMention(payload, NormalizePingText(pingText));

            return await PostPayloadAsync(webhookUrl, payload, null);
        }

        private static async Task<DiscordSendResult> PostPayloadAsync(string webhookUrl, Dictionary<string, object> payload, byte[] pngAttachment)
        {
            var res = new DiscordSendResult();
            try
            {
                var ser = new JavaScriptSerializer();
                string json = ser.Serialize(payload);
                HttpResponseMessage response;

                if (pngAttachment != null && pngAttachment.Length > 0)
                {
                    using (var form = new MultipartFormDataContent())
                    {
                        var jsonContent = new StringContent(json, Encoding.UTF8, "application/json");
                        form.Add(jsonContent, "payload_json");

                        var fileContent = new ByteArrayContent(pngAttachment);
                        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                        form.Add(fileContent, "files[0]", "chat.png");

                        response = await Http.PostAsync(webhookUrl.Trim(), form);
                    }
                }
                else
                {
                    using (var body = new StringContent(json, Encoding.UTF8, "application/json"))
                    {
                        response = await Http.PostAsync(webhookUrl.Trim(), body);
                    }
                }

                using (response)
                {
                    if (response.IsSuccessStatusCode)
                    {
                        res.Sent = true;
                        res.Message = "Delivered to Discord (" + (int)response.StatusCode + ").";
                    }
                    else
                    {
                        string respBody = await response.Content.ReadAsStringAsync();
                        res.Sent = false;
                        res.Message = string.Format("Discord HTTP {0}: {1}", (int)response.StatusCode, Truncate(respBody, 160));
                    }
                }
            }
            catch (Exception ex)
            {
                res.Sent = false;
                res.Message = "Webhook request error: " + ex.Message;
            }

            return res;
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
        }
    }
}

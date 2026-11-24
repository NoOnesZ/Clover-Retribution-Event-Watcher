package id.romulti.clover;

import java.util.ArrayList;
import java.util.Collections;
import java.util.HashSet;
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;

final class EventHistory {
    private static final long STARTUP_GRACE_MS = 15000;
    private static final long SCROLL_REPLAY_MS = 120000;
    private static final long FORGET_AFTER_MS = 1800000;
    private final Map<String, Track> visible = new LinkedHashMap<>();
    private final Map<String, Long> seenEventLines = new LinkedHashMap<>();
    private final Set<String> lastScanLineSigs = new HashSet<>();
    private long firstFrame = -1;
    private boolean armed;

    List<EventMatcher.Detection> observe(EventMatcher.Frame frame, long now) {
        if (firstFrame < 0) firstFrame = now;
        if (!armed && !frame.readable && now - firstFrame < STARTUP_GRACE_MS)
            return Collections.emptyList();

        Map<String, List<EventMatcher.Detection>> current = new LinkedHashMap<>();
        for (EventMatcher.Detection event : frame.events) {
            if (event.rule == CloverRules.UNKNOWN && resemblesNamed(event, frame, now)) continue;
            String key = event.rule == CloverRules.UNKNOWN ? canonicalUnknownKey(event, current) : event.key;
            current.computeIfAbsent(key, ignored -> new ArrayList<>()).add(event);
        }

        if (!armed) {
            armed = true;
            for (Map.Entry<String, List<EventMatcher.Detection>> entry : current.entrySet()) {
                List<EventMatcher.Detection> events = entry.getValue();
                Track track = new Track();
                track.record(events.size(), events.get(events.size() - 1).bodyKey, now);
                for (EventMatcher.Detection occ : events) {
                    track.recordAnchor(
                        neighborSig(frame.timeline, occ.timelineIndex - 1),
                        neighborSig(frame.timeline, occ.timelineIndex + 1), now);
                }
                visible.put(entry.getKey(), track);
            }
            if (frame.readable) recordTimelineMemory(frame.timeline, now);
            return Collections.emptyList();
        }
        if (!frame.readable) {
            for (Track track : visible.values()) track.pendingCount = 0;
            return Collections.emptyList();
        }

        List<EventMatcher.Detection> alerts = new ArrayList<>();
        for (Map.Entry<String, List<EventMatcher.Detection>> entry : current.entrySet()) {
            List<EventMatcher.Detection> occurrences = entry.getValue();
            Track track = visible.computeIfAbsent(entry.getKey(), key -> new Track());
            int count = occurrences.size();
            EventMatcher.Detection newest = occurrences.get(count - 1);
            String prevSig = neighborSig(frame.timeline, newest.timelineIndex - 1);
            String nextSig = neighborSig(frame.timeline, newest.timelineIndex + 1);
            if (count > track.count) {
                if (newest.rule == CloverRules.UNKNOWN &&
                        (track.pendingCount != count || now - track.pendingAt > SCROLL_REPLAY_MS)) {
                    track.pendingCount = count;
                    track.pendingAt = now;
                    continue;
                }
                boolean recentReappearance = track.count == 0 && track.recent(now);
                boolean alreadyReportedUnderOtherName = newest.rule != CloverRules.UNKNOWN &&
                    track.count == 0 && resemblesRecent(newest, now, true);
                boolean oldTimelineReplay = track.withinMemory(now)
                    && isOldTimelineReplay(track, frame.timeline, newest.timelineIndex, prevSig, nextSig, now);
                if (!recentReappearance && !alreadyReportedUnderOtherName && !oldTimelineReplay) {
                    alerts.add(newest);
                }
            }
            track.pendingCount = 0;
            track.record(count, newest.bodyKey, now);
            for (EventMatcher.Detection occ : occurrences) {
                track.recordAnchor(
                    neighborSig(frame.timeline, occ.timelineIndex - 1),
                    neighborSig(frame.timeline, occ.timelineIndex + 1), now);
            }
        }

        Iterator<Map.Entry<String, Track>> previous = visible.entrySet().iterator();
        while (previous.hasNext()) {
            Map.Entry<String, Track> entry = previous.next();
            if (current.containsKey(entry.getKey())) continue;
            Track track = entry.getValue();
            track.pendingCount = 0;
            if (track.lastSeen < 0 || now - track.lastSeen >= FORGET_AFTER_MS) previous.remove();
            else track.update(0);
        }
        recordTimelineMemory(frame.timeline, now);
        return alerts;
    }

    private boolean isOldTimelineReplay(
            Track track, List<String> timeline, int timelineIndex, String prevSig, String nextSig, long now) {
        if (timeline == null || timelineIndex < 0 || timelineIndex >= timeline.size()) return false;

        // Rule M1: An already-seen event reappearing ABOVE an already-seen chat message
        // (a non-tip event/leak from an earlier scan, OR any line already present on the immediately
        // preceding scan) is physically an OLD chat history row, never a brand-new spawn.
        for (int below = timelineIndex + 1; below < timeline.size(); below++) {
            String belowSig = timeline.get(below);
            if (belowSig == null || belowSig.length() < 12) continue;
            if (!isGenericTipOrSystem(belowSig) && isInSeenEventLines(belowSig, now)) return true;
            if (isInLastScan(belowSig)) return true;
        }

        // Rule M2: Exact remembered timeline slot within 30 minutes (both Prev+Next match,
        // or a non-generic Prev event/chat neighbor matches).
        for (Anchor anchor : track.anchors) {
            if (now - anchor.recordedAt >= FORGET_AFTER_MS) continue;
            boolean prevMatch = !prevSig.isEmpty() && !anchor.prevSig.isEmpty()
                && sameNeighbor(anchor.prevSig, prevSig);
            boolean nextMatch = !nextSig.isEmpty() && !anchor.nextSig.isEmpty()
                && sameNeighbor(anchor.nextSig, nextSig);
            if (prevMatch && nextMatch) return true;
            if (prevMatch && !isGenericTipOrSystem(prevSig) && (anchor.nextSig.isEmpty() || nextMatch)) {
                return true;
            }
        }
        return false;
    }

    private void recordTimelineMemory(List<String> timeline, long now) {
        Iterator<Map.Entry<String, Long>> it = seenEventLines.entrySet().iterator();
        while (it.hasNext()) {
            if (now - it.next().getValue() >= FORGET_AFTER_MS) it.remove();
        }
        lastScanLineSigs.clear();
        if (timeline == null) return;
        for (String sig : timeline) {
            if (sig == null || sig.length() < 12) continue;
            lastScanLineSigs.add(sig);
            if (!isGenericTipOrSystem(sig)) {
                seenEventLines.put(sig, now);
            }
        }
    }

    private boolean isInSeenEventLines(String sig, long now) {
        Long seenAt = seenEventLines.get(sig);
        if (seenAt != null && now - seenAt < FORGET_AFTER_MS) return true;
        for (Map.Entry<String, Long> entry : seenEventLines.entrySet()) {
            if (now - entry.getValue() >= FORGET_AFTER_MS) continue;
            if (EventMatcher.sameBroadcast(sig, entry.getKey())) return true;
        }
        return false;
    }

    private boolean isInLastScan(String sig) {
        if (lastScanLineSigs.contains(sig)) return true;
        for (String prev : lastScanLineSigs) {
            if (EventMatcher.sameBroadcast(sig, prev)) return true;
        }
        return false;
    }

    private static String neighborSig(List<String> timeline, int index) {
        if (timeline == null || index < 0 || index >= timeline.size()) return "";
        String s = timeline.get(index);
        return s == null ? "" : s;
    }

    private static boolean sameNeighbor(String a, String b) {
        if (a.isEmpty() || b.isEmpty()) return a.isEmpty() && b.isEmpty();
        return a.equals(b) || EventMatcher.sameBroadcast(a, b);
    }

    private static boolean isGenericTipOrSystem(String sig) {
        return sig.isEmpty()
            || sig.startsWith("tips ") || sig.startsWith("tip ")
            || sig.startsWith("system ") || sig.startsWith("clover system ")
            || sig.contains("to chat click") || sig.contains("unlock chat")
            || sig.contains("here global");
    }

    private boolean resemblesNamed(EventMatcher.Detection event, EventMatcher.Frame frame, long now) {
        for (EventMatcher.Detection other : frame.events) {
            if (other.rule != CloverRules.UNKNOWN && EventMatcher.sameBroadcast(event.bodyKey, other.bodyKey))
                return true;
        }
        return resemblesRecent(event, now, false);
    }

    private boolean resemblesRecent(EventMatcher.Detection event, long now, boolean unknown) {
        for (Map.Entry<String, Track> entry : visible.entrySet()) {
            Track track = entry.getValue();
            if (isUnknownKey(entry.getKey()) == unknown && track.recent(now) &&
                    EventMatcher.sameBroadcast(event.bodyKey, track.bodyKey)) return true;
        }
        return false;
    }

    private String canonicalUnknownKey(EventMatcher.Detection event, Map<String, List<EventMatcher.Detection>> current) {
        if (visible.containsKey(event.key) || current.containsKey(event.key)) return event.key;
        for (String key : visible.keySet()) {
            if (isUnknownKey(key) && matchesUnknownKey(event, key)) return key;
        }
        for (String key : current.keySet()) {
            if (isUnknownKey(key) && matchesUnknownKey(event, key)) return key;
        }
        return event.key;
    }

    private static boolean matchesUnknownKey(EventMatcher.Detection event, String key) {
        return EventMatcher.sameBroadcast(event.bodyKey, key.substring(CloverRules.UNKNOWN.id.length() + 1));
    }

    private static boolean isUnknownKey(String key) {
        return key.startsWith(CloverRules.UNKNOWN.id + ":");
    }

    private static final class Anchor {
        final String prevSig;
        final String nextSig;
        long recordedAt;

        Anchor(String prevSig, String nextSig, long recordedAt) {
            this.prevSig = prevSig;
            this.nextSig = nextSig;
            this.recordedAt = recordedAt;
        }
    }

    private static final class Track {
        int count;
        int lowerFrames;
        int pendingCount;
        long pendingAt;
        long lastSeen = -1;
        String bodyKey = "";
        final List<Anchor> anchors = new ArrayList<>();

        boolean recent(long now) {
            return lastSeen >= 0 && now - lastSeen < SCROLL_REPLAY_MS;
        }

        boolean withinMemory(long now) {
            return lastSeen >= 0 && now - lastSeen < FORGET_AFTER_MS;
        }

        void recordAnchor(String prevSig, String nextSig, long now) {
            Iterator<Anchor> it = anchors.iterator();
            while (it.hasNext()) {
                if (now - it.next().recordedAt >= FORGET_AFTER_MS) it.remove();
            }
            for (Anchor a : anchors) {
                if (sameNeighbor(a.prevSig, prevSig) && sameNeighbor(a.nextSig, nextSig)) {
                    a.recordedAt = now;
                    return;
                }
            }
            anchors.add(new Anchor(prevSig, nextSig, now));
            if (anchors.size() > 12) anchors.remove(0);
        }

        void record(int count, String bodyKey, long now) {
            this.bodyKey = bodyKey;
            lastSeen = now;
            update(count);
        }

        void update(int current) {
            if (current >= count || ++lowerFrames >= 2) {
                count = current;
                lowerFrames = 0;
            }
        }
    }
}

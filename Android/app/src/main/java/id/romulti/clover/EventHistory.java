package id.romulti.clover;

import java.util.ArrayList;
import java.util.Collections;
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

final class EventHistory {
    private static final long STARTUP_GRACE_MS = 15000;
    private static final long SCROLL_REPLAY_MS = 120000;
    private static final long FORGET_AFTER_MS = 1800000;
    private final Map<String, Track> visible = new LinkedHashMap<>();
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
                visible.put(entry.getKey(), track);
            }
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
                if (!recentReappearance && !alreadyReportedUnderOtherName) alerts.add(newest);
            }
            track.pendingCount = 0;
            track.record(count, newest.bodyKey, now);
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
        return alerts;
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

    private static final class Track {
        int count;
        int lowerFrames;
        int pendingCount;
        long pendingAt;
        long lastSeen = -1;
        String bodyKey = "";

        boolean recent(long now) {
            return lastSeen >= 0 && now - lastSeen < SCROLL_REPLAY_MS;
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

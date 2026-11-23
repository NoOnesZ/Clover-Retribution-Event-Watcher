package id.romulti.clover;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

final class EventMatcher {
    private static final Pattern EVENT = Pattern.compile(
        "^\\s*(?:[\\[(]\\s*(?:event|leak)(?:\\s*[\\]):|]|\\b)|(?:event|leak)\\s*[\\]):|])\\s*",
        Pattern.CASE_INSENSITIVE);
    private static final Pattern OTHER_TAG = Pattern.compile(
        "^\\s*[\\[(]?\\s*(?:tips?|system|clover system|server|global)\\s*[\\]):|]",
        Pattern.CASE_INSENSITIVE);
    private static final Pattern PLAYER = Pattern.compile("^\\s*(?:\\[[^]]+\\]\\s*)?[\\w]{2,24}:\\s+\\S");
    private static final Pattern NON_WORD = Pattern.compile("[^a-z0-9]+");
    private static final Pattern STAR_PREFIX = Pattern.compile("^(?:(?:event[o0]?|even[i1f]|the|a) )+");
    private static final Pattern STAR_COUNT = Pattern.compile("\\b([a-z0-9]{1,7}) st[a4][rl1][s5]?\\b");
    private static final Pattern STAR_ACTION = Pattern.compile(
        "^(?:(?:sphere|orb) )?(?:has |have |now |begins to )?(?:gl|glo|glow(?:s|ing)?|shin(?:e[sd]?|ing)|"
            + "gleam(?:s|ing)?|appear(?:s|ed)?|emerge(?:s|d)?|descend(?:s|ed)?|materializ(?:es|ed)|spawn(?:s|ed)?)(?: |$)");
    private static final Pattern SPHERE = Pattern.compile("(?:^| )(?:sphere|orb)(?: |$)");
    private static final int MAX_STAR_LINES = 5;

    static Frame scan(List<String> lines, boolean includeCommon) {
        Frame frame = new Frame();
        for (int index = 0; index < lines.size(); index++) {
            String line = lines.get(index).trim();
            if (line.isEmpty()) continue;
            if (OTHER_TAG.matcher(line).find()) {
                frame.readable = true;
                continue;
            }
            Matcher tag = EVENT.matcher(line);
            boolean tagged = tag.find();
            if (!tagged && PLAYER.matcher(line).find()) continue;
            String body = tagged ? line.substring(tag.end()).trim() : line;
            if (tagged && !body.isEmpty()) frame.readable = true;
            String normalized = normalize(body);
            boolean starOpening = startsStar(normalized);
            Rule star = null;
            if (tagged || starOpening) {
                String joined = body;
                int last = index;
                star = findStarRule(normalized, tagged);
                while (last + 1 < lines.size() && last - index + 1 < MAX_STAR_LINES && !endsSentence(joined)) {
                    String next = lines.get(last + 1).trim();
                    if (!isContinuation(next)) break;
                    joined = joined.isEmpty() ? next : joined + " " + next;
                    last++;
                    if (star == null) star = findStarRule(normalize(joined), tagged);
                }
                if (star != null) {
                    body = joined;
                    normalized = normalize(body);
                    index = last;
                }
            }

            if (normalized.isEmpty() || (!tagged && star == null && !startsRift(normalized))) continue;

            if (star == null && index + 1 < lines.size() && !endsSentence(body)) {
                String next = lines.get(index + 1).trim();
                if (isContinuation(next)) {
                    body += " " + next;
                    normalized = normalize(body);
                    index++;
                }
            }

            if (containsAny(normalized, CloverRules.DESPAWN) || isCorruptedNightEnd(normalized) ||
                containsAny(normalized, CloverRules.TIP_PHRASES)) continue;
            Rule rule = star != null ? star : findRule(normalized, tagged);
            if (rule == null && tagged && normalized.split(" ").length >= 4) {
                rule = CloverRules.UNKNOWN;
            }
            if (rule == null) continue;
            frame.readable = true;
            if (rule.enabledByDefault || includeCommon) {
                String key = rule == CloverRules.UNKNOWN ? rule.id + ":" + bodyKey(body) : rule.id;
                frame.events.add(new Detection(rule, body, key));
            }
        }
        return frame;
    }

    private static boolean isContinuation(String line) {
        if (line.isEmpty() || EVENT.matcher(line).find() || OTHER_TAG.matcher(line).find() ||
                PLAYER.matcher(line).find()) return false;
        String normalized = normalize(line);
        return !startsRift(normalized) && findStarRule(normalized, false) == null;
    }

    private static Rule findRule(String line, boolean tagged) {
        for (Rule rule : CloverRules.NAMED) {
            if ((!tagged && !rule.priority) || !rule.matches(line)) continue;
            if (!tagged && rule.id.equals("devil_rift") && !line.contains("feels off")) continue;
            if (!tagged && rule.id.equals("spirit_rift") && !line.contains("kingdom")) continue;
            return rule;
        }
        if (!tagged && !startsRift(line)) return null;
        String[] words = line.split(" ");
        Rule candidate = null;
        for (Rule rule : CloverRules.NAMED) {
            if ((!tagged && !rule.priority) || !rule.matchesTypo(words)) continue;
            if (candidate != null) return null;
            candidate = rule;
        }
        return candidate;
    }

    private static boolean startsStar(String line) {
        String body = STAR_PREFIX.matcher(line).replaceFirst("");
        Matcher match = STAR_COUNT.matcher(body);
        return (match.lookingAt() && starByCount(match.group(1)) != null) || starByCount(body) != null;
    }

    private static Rule findStarRule(String line, boolean tagged) {
        String body = STAR_PREFIX.matcher(line).replaceFirst("");
        Matcher match = STAR_COUNT.matcher(body);
        if (!tagged) {
            return match.lookingAt() && STAR_ACTION.matcher(body.substring(match.end()).trim()).find()
                ? starByCount(match.group(1)) : null;
        }

        Rule candidate = null;
        boolean hasSphere = SPHERE.matcher(body).find();
        while (match.find()) {
            if (match.start() != 0 && !hasSphere && !STAR_ACTION.matcher(body.substring(match.end()).trim()).find()) continue;
            Rule star = starByCount(match.group(1));
            if (star == null) continue;
            if (candidate != null && candidate != star) return null;
            candidate = star;
        }
        return candidate;
    }

    private static Rule starByCount(String count) {
        if (count.equals("single") || count.equals("lone")) return CloverRules.STAR_EVENTS[0];
        if (count.length() == 1 && count.charAt(0) >= '1' && count.charAt(0) <= '7')
            return CloverRules.STAR_EVENTS[count.charAt(0) - '1'];
        for (int i = 0; i < CloverRules.STAR_COUNTS.length; i++) {
            if (count.equals(CloverRules.STAR_COUNTS[i])) return CloverRules.STAR_EVENTS[i];
        }
        Rule candidate = null;
        for (int i = 0; i < CloverRules.STAR_COUNTS.length; i++) {
            String word = CloverRules.STAR_COUNTS[i];
            if (!nearWord(count, word)) continue;
            if (candidate != null) return null;
            candidate = CloverRules.STAR_EVENTS[i];
        }
        return candidate;
    }

    private static boolean isCorruptedNightEnd(String line) {
        return (line.contains("corrupt") || line.contains("night")) &&
            (line.contains("releases its grip") || line.contains("order is restored"));
    }

    private static boolean startsRift(String line) {
        String[] words = line.split(" ");
        int start = words[0].equals("a") ? 1 : 0;
        if (words.length - start < 2) return false;
        return nearOpening(words, start, "something", "feels")
            || nearOpening(words, start, "strange", "energy")
            || nearOpening(words, start, "mysterious", "energy");
    }

    private static boolean nearOpening(String[] words, int start, String first, String second) {
        return (words[start].equals(first) && nearWord(words[start + 1], second))
            || (words[start + 1].equals(second) && nearWord(words[start], first));
    }

    private static boolean endsSentence(String s) {
        return s.endsWith(".") || s.endsWith("!") || s.endsWith("?");
    }

    private static String normalize(String s) {
        return NON_WORD.matcher(s.toLowerCase(Locale.ROOT)).replaceAll(" ").trim();
    }

    static String bodyKey(String text) {
        String clean = normalize(text);
        return clean.substring(0, Math.min(160, clean.length())).trim();
    }

    static boolean sameBroadcast(String first, String second) {
        if (first.equals(second)) return !first.isEmpty();
        String[] a = first.split(" ");
        String[] b = second.split(" ");
        int shared = Math.min(a.length, b.length);
        if (shared < 5 || Math.abs(a.length - b.length) > 2) return false;
        int changed = 0;
        for (int i = 0; i < shared; i++) {
            if (a[i].equals(b[i])) continue;
            if (++changed > 1 || !nearWord(a[i], b[i])) return false;
        }
        return true;
    }

    private static boolean nearWord(String first, String second) {
        if (Math.min(first.length(), second.length()) < 4) return false;
        String a = first.replace("rn", "m");
        String b = second.replace("rn", "m");
        if (Math.abs(a.length() - b.length()) > 1) return false;
        int i = 0;
        int j = 0;
        int edits = 0;
        while (i < a.length() && j < b.length()) {
            if (a.charAt(i) == b.charAt(j)) {
                i++;
                j++;
                continue;
            }
            if (++edits > 1) return false;
            if (a.length() >= b.length()) i++;
            if (b.length() >= a.length()) j++;
        }
        return edits + a.length() - i + b.length() - j <= 1;
    }

    private static boolean containsAny(String s, String[] values) {
        for (String value : values) {
            if (s.contains(value)) return true;
        }
        return false;
    }

    static final class Rule {
        final String id;
        final String title;
        final int color;
        final boolean enabledByDefault;
        final boolean priority;
        private final String[][] tokenGroups;

        Rule(String id, String title, int color, boolean enabled, boolean priority, String patterns) {
            this.id = id;
            this.title = title;
            this.color = color;
            this.enabledByDefault = enabled;
            this.priority = priority;
            String[] groups = patterns.isEmpty() ? new String[0] : patterns.split("\\|");
            tokenGroups = new String[groups.length][];
            for (int i = 0; i < groups.length; i++) tokenGroups[i] = groups[i].split(" ");
        }

        boolean matches(String line) {
            for (String[] group : tokenGroups) {
                int i = 0;
                while (i < group.length && line.contains(group[i])) i++;
                if (i == group.length) return true;
            }
            return false;
        }

        boolean matchesTypo(String[] words) {
            for (String[] group : tokenGroups) {
                if (group.length < 2) continue;
                int approximate = 0;
                boolean anchor = false;
                boolean matched = true;
                for (String token : group) {
                    boolean exact = false;
                    boolean near = false;
                    for (String word : words) {
                        if (word.equals(token)) {
                            exact = true;
                            break;
                        }
                        near |= nearWord(word, token);
                    }
                    if (exact) anchor |= token.length() >= 4;
                    else if (!near || ++approximate > 1) {
                        matched = false;
                        break;
                    }
                }
                if (matched && anchor && approximate == 1) return true;
            }
            return false;
        }
    }

    static final class Detection {
        final Rule rule;
        final String text;
        final String key;
        final String bodyKey;

        Detection(Rule rule, String text, String key) {
            this.rule = rule;
            this.text = text;
            this.key = key;
            this.bodyKey = EventMatcher.bodyKey(text);
        }
    }

    static final class Frame {
        final List<Detection> events = new ArrayList<>();
        boolean readable;
    }

    private EventMatcher() {}
}

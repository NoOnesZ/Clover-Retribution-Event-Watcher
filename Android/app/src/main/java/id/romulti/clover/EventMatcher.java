package id.romulti.clover;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

final class EventMatcher {
    private static final Pattern EVENT = Pattern.compile(
        "^\\s*[,.'\";:_-]*\\s*(?:[\\[(]\\s*(?:[ectfk][vw]e(?:nt|ni|nf|nl|n7|m|rn|nto|nt0)|l[e3][a4o]k|leek)(?:[o0l]?\\s*[\\]):|']|l?\\b)"
            + "|(?:event|eveni|evenf|evento|leak|leek)\\s*[\\]):|'])\\s*",
        Pattern.CASE_INSENSITIVE);
    private static final Pattern OTHER_TAG = Pattern.compile(
        "^\\s*[\\[(]?\\s*(?:tips?|system|clover system|raid|server|global)\\s*[\\]):|]",
        Pattern.CASE_INSENSITIVE);
    private static final Pattern PLAYER = Pattern.compile("^\\s*(?:\\[[^]]+\\]\\s*)?[\\w]{2,24}:\\s+\\S");
    private static final Pattern NON_WORD = Pattern.compile("[^a-z0-9]+");
    private static final Pattern STAR_PREFIX = Pattern.compile("^(?:(?:event[o0]?|even[i1f]|the|a) )+");
    private static final Pattern STAR_COUNT = Pattern.compile("\\b([a-z0-9]{1,7}) st[a4][rl1][s5]?\\b");
    private static final Pattern STAR_ACTION = Pattern.compile(
        "^(?:(?:sphere|orb) )?(?:has |have |now |begins to )?(?:gl|glo|glow(?:s|ing)?|shin(?:e[sd]?|ing)|"
            + "gleam(?:s|ing)?|appear(?:s|ed)?|emerge(?:s|d)?|descend(?:s|ed)?|materializ(?:es|ed)|spawn(?:s|ed)?)(?: |$)");
    private static final Pattern SPHERE = Pattern.compile("(?:^| )(?:sphere|orb)(?: |$)");
    private static final Pattern GIANT_SEAL = Pattern.compile(
        "^(?:the )?se[a4][l1i] (?:ha[s5] )?[s5]ha[t7l1]{2}[e3]r[a-z0-9]{0,5}(?: |$)");
    private static final Pattern DEVIL_GLUED_FEELS_OFF = Pattern.compile(
        "\\bf[e3c][e3c][ltib1dr][s5z]?(?:[o0][f1t]{1,2}|oh)\\b");
    private static final Pattern DEVIL_FUZZY_FEELS = Pattern.compile(
        "\\bf[e3c][e3c][ltib1d][s5z]?\\b");
    private static final Pattern DEVIL_SPLIT_SOMETHING = Pattern.compile(
        "\\b(?:s[o0]m[e3]|s[o0]rn[e3]|s[o0]rr[e3])t\\s*h[a-z0-9]{1,5}\\b");
    private static final Pattern DEVIL_OFF_WORD = Pattern.compile(
        "\\b(?:[o0][f1t]{2}|oh)\\b");
    private static final int MAX_STAR_LINES = 5;

    static Frame scan(List<String> lines, boolean includeCommon) {
        Frame frame = new Frame();
        for (int index = 0; index < lines.size(); index++) {
            String line = lines.get(index).trim();
            if (line.isEmpty()) continue;
            if (OTHER_TAG.matcher(line).find()) {
                frame.readable = true;
                frame.timeline.add(timelineSig(line));
                continue;
            }
            Matcher tag = EVENT.matcher(line);
            boolean tagged = tag.find();
            if (!tagged && PLAYER.matcher(line).find()) {
                frame.timeline.add(timelineSig(line));
                continue;
            }
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

            if (normalized.isEmpty() || (!tagged && star == null && !startsRift(normalized) && !startsPriorityWisp(normalized) && !isDevilRiftFuzzyBroadcast(normalized, false))) {
                if (normalized.length() >= 8) frame.timeline.add(timelineSig(line));
                continue;
            }

            if (star == null && index + 1 < lines.size() && !endsSentence(body)) {
                String next = lines.get(index + 1).trim();
                if (isContinuation(next)) {
                    body += " " + next;
                    normalized = normalize(body);
                    index++;
                }
            }

            int timelineIndex = frame.timeline.size();
            frame.timeline.add(timelineSig(tagged ? "[Event] " + body : body));

            if (isDespawnOrEnd(normalized) || containsAny(normalized, CloverRules.TIP_PHRASES)) continue;
            Rule rule = star != null ? star : findRule(normalized, tagged);
            if (rule == null && tagged && isReadableUnknown(body)) {
                rule = CloverRules.UNKNOWN;
            }
            if (rule == null) continue;
            frame.readable = true;
            if (!CloverRules.isAlwaysMuted(rule) && (rule.enabledByDefault || includeCommon)) {
                String key = rule == CloverRules.UNKNOWN ? rule.id + ":" + bodyKey(body) : rule.id;
                frame.events.add(new Detection(rule, body, key, timelineIndex));
            }
        }
        return frame;
    }

    private static boolean isContinuation(String line) {
        if (line.isEmpty() || EVENT.matcher(line).find() || OTHER_TAG.matcher(line).find() ||
                PLAYER.matcher(line).find()) return false;
        String normalized = normalize(line);
        return !startsRift(normalized) && !startsPriorityWisp(normalized) && findStarRule(normalized, false) == null;
    }

    private static Rule findRule(String line, boolean tagged) {
        if (tagged && GIANT_SEAL.matcher(line).find()) return CloverRules.GIANT_DEMON;
        if (isDevilRiftFuzzyBroadcast(line, tagged)) return CloverRules.NAMED[0];
        String[] words = line.split(" ");
        String compact = intentSkeleton(line).replace(" ", "");

        // Tier 1: Exact token-group match
        for (Rule rule : CloverRules.NAMED) {
            if (!tagged && !allowsTaglessRule(rule, line, words, compact)) continue;
            if (rule.matches(line)) return rule;
        }
        if (!tagged && !startsRift(line) && !startsPriorityWisp(line)) return null;

        // Tier 2: Unique single-word typo match (ambiguity short-circuits to null!)
        Rule tier2Candidate = null;
        int tier2Count = 0;
        for (Rule rule : CloverRules.NAMED) {
            if (!tagged && !allowsTaglessRule(rule, line, words, compact)) continue;
            if (!rule.matchesTypo(words)) continue;
            tier2Candidate = rule;
            if (++tier2Count >= 2) return null;
        }
        if (tier2Count == 1) return tier2Candidate;

        // Tier 3.1: Space-glued distinctive multi-word anchor match (ambiguity short-circuits to null!)
        Rule gluedCandidate = null;
        int gluedCount = 0;
        for (Rule rule : CloverRules.NAMED) {
            if (!tagged && !allowsTaglessRule(rule, line, words, compact)) continue;
            if (!rule.matchesGlued(compact)) continue;
            gluedCandidate = rule;
            if (++gluedCount >= 2) return null;
        }
        if (gluedCount == 1) return gluedCandidate;

        // Tier 3.2: Multi-typo token-group intent match (strict uniqueness -> null if 2+ rules qualify)
        Rule intentCandidate = null;
        int intentCount = 0;
        for (Rule rule : CloverRules.NAMED) {
            if (!tagged && !allowsTaglessRule(rule, line, words, compact)) continue;
            if (!rule.matchesIntentMultiTypo(words)) continue;
            intentCandidate = rule;
            if (++intentCount >= 2) return null;
        }
        return intentCount == 1 ? intentCandidate : null;
    }

    private static boolean allowsTaglessRule(Rule rule, String line, String[] words, String compact) {
        if (!rule.priority) return false;
        if (rule.id.equals("devil_rift")) return isDevilRiftFuzzyBroadcast(line, false);
        if (rule.id.equals("spirit_rift")) {
            if (!startsRift(line)) return false;
            for (String w : words) {
                if (w.equals("kingdom") || w.equals("felt") || w.equals("throughout")
                        || w.equals("toward") || nearWord(w, "kingdom")) {
                    return true;
                }
            }
            return compact.contains("kingdom") || compact.contains("throughout");
        }
        if (isPriorityWispRule(rule)) return startsPriorityWisp(line);
        return false;
    }

    private static boolean isPriorityWispRule(Rule rule) {
        return rule.id.equals("wind_wisp") || rule.id.equals("blood_wisp")
            || rule.id.equals("light_wisp") || rule.id.equals("spirit_wisp");
    }

    private static boolean startsPriorityWisp(String line) {
        String compact = intentSkeleton(line).replace(" ", "");
        if (compact.startsWith("windwisp") || compact.startsWith("awindwisp") || compact.startsWith("thewindwisp")
                || compact.startsWith("bloodwisp") || compact.startsWith("abloodwisp") || compact.startsWith("thebloodwisp")
                || compact.startsWith("lightwisp") || compact.startsWith("alightwisp") || compact.startsWith("thelightwisp")
                || compact.startsWith("spiritwisp") || compact.startsWith("aspiritwisp") || compact.startsWith("thespiritwisp")) {
            return true;
        }
        String[] words = line.split(" ");
        int start = (words.length > 0 && (words[0].equals("a") || words[0].equals("the"))) ? 1 : 0;
        if (words.length - start < 2) return false;
        return nearOpeningBoth(words, start, "wind", "wisp")
            || nearOpeningBoth(words, start, "blood", "wisp")
            || nearOpeningBoth(words, start, "light", "wisp")
            || nearOpeningBoth(words, start, "spirit", "wisp");
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

    private static boolean isDespawnOrEnd(String line) {
        if (containsAny(line, CloverRules.DESPAWN) || isCorruptedNightEnd(line)) return true;
        String compact = intentSkeleton(line).replace(" ", "");
        if (containsAny(compact, CloverRules.GLUED_DESPAWN_COMPOUNDS)) return true;
        String[] words = line.split(" ");
        for (String w : words) {
            if (w.length() < 5) continue;
            for (String despawn : CloverRules.FUZZY_DESPAWN_WORDS) {
                if (nearWord(w, despawn)) return true;
            }
        }
        return false;
    }

    private static boolean isCorruptedNightEnd(String line) {
        return (line.contains("corrupt") || line.contains("night")) &&
            (line.contains("releases its grip") || line.contains("order is restored"));
    }

    private static boolean startsRift(String line) {
        String compact = intentSkeleton(line).replace(" ", "");
        if (compact.startsWith("somethingfeels") || compact.startsWith("asomethingfeels")
                || compact.startsWith("strangeenergy") || compact.startsWith("astrangeenergy")
                || compact.startsWith("mysteriousenergy") || compact.startsWith("amysteriousenergy")) {
            return true;
        }
        String[] words = line.split(" ");
        int start = (words.length > 0 && (words[0].equals("a") || words[0].equals("the"))) ? 1 : 0;
        if (words.length - start < 2) return false;
        return nearOpeningBoth(words, start, "something", "feels")
            || nearOpeningBoth(words, start, "strange", "energy")
            || nearOpeningBoth(words, start, "mysterious", "energy");
    }

    private static boolean nearOpeningBoth(String[] words, int start, String first, String second) {
        boolean firstOk = words[start].equals(first) || nearWord(words[start], first);
        boolean secondOk = words[start + 1].equals(second) || nearWord(words[start + 1], second);
        return firstOk && secondOk;
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

    static boolean sameUnknownBroadcast(String first, String second) {
        if (first == null || second == null || first.isEmpty() || second.isEmpty()) return false;
        if (sameBroadcast(first, second)) return true;
        String a = intentSkeleton(first.toLowerCase(Locale.ROOT)).replace(" ", "");
        String b = intentSkeleton(second.toLowerCase(Locale.ROOT)).replace(" ", "");
        int minLen = Math.min(a.length(), b.length());
        int maxLen = Math.max(a.length(), b.length());
        if (minLen < 18 || ((double) minLen / maxLen) < 0.65) return false;
        return bigramDice(a, b) >= 0.62;
    }

    private static double bigramDice(String a, String b) {
        if (a.length() < 2 || b.length() < 2) return 0.0;
        java.util.HashMap<String, Integer> countsA = new java.util.HashMap<>();
        int totalA = a.length() - 1;
        for (int i = 0; i < totalA; i++) {
            String bg = a.substring(i, i + 2);
            Integer c = countsA.get(bg);
            countsA.put(bg, c == null ? 1 : c + 1);
        }
        int intersection = 0;
        int totalB = b.length() - 1;
        for (int i = 0; i < totalB; i++) {
            String bg = b.substring(i, i + 2);
            Integer c = countsA.get(bg);
            if (c != null && c > 0) {
                intersection++;
                countsA.put(bg, c - 1);
            }
        }
        return (2.0 * intersection) / (totalA + totalB);
    }

    static String intentSkeleton(String word) {
        if (word == null || word.isEmpty()) return "";
        String s = word.replace("rn", "m").replace("nn", "m").replace("nu", "m")
            .replace("vv", "w").replace("cl", "d");
        StringBuilder sb = new StringBuilder(s.length());
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            switch (c) {
                case '0': sb.append('o'); break;
                case '1': sb.append('l'); break;
                case '3': sb.append('e'); break;
                case '4': sb.append('a'); break;
                case '5': sb.append('s'); break;
                default: sb.append(c); break;
            }
        }
        return sb.toString();
    }

    private static boolean isDevilRiftFuzzyBroadcast(String line, boolean tagged) {
        if (line == null || line.isEmpty()) return false;
        if (line.contains("leaking") || line.contains("somebody") || line.contains("sornebody")
                || line.contains("survive") || line.contains("desert") || line.contains("village")
                || line.contains("seen") || line.contains("shiftlock")) {
            return false;
        }
        if (line.contains("something feels") || line.contains("feels off")) {
            return true;
        }
        String[] words = line.split(" ");
        boolean hasSomething = false;
        for (String w : words) {
            if (isNearSomething(w)) {
                hasSomething = true;
                break;
            }
        }
        boolean hasSplitSomething = !hasSomething && DEVIL_SPLIT_SOMETHING.matcher(line).find();
        if (!hasSomething && !hasSplitSomething) {
            return tagged && DEVIL_GLUED_FEELS_OFF.matcher(line).find();
        }
        if (DEVIL_GLUED_FEELS_OFF.matcher(line).find()) return true;
        if (DEVIL_FUZZY_FEELS.matcher(line).find()) return true;
        if (tagged && line.contains(" off")) return true;
        return tagged && DEVIL_OFF_WORD.matcher(line).find() && !line.endsWith(" oh");
    }

    private static boolean isNearSomething(String word) {
        if (word == null || word.length() < 7 || word.length() > 11) return false;
        if (word.startsWith("somebod") || word.startsWith("sornebod")) return false;
        String sk = ocrSkeleton(word).replace("rr", "m");
        if (!sk.startsWith("so")) return false;
        if (sk.equals("something") || sk.equals("somethin")) return true;
        return editDistanceAtMost(sk, "something", 2);
    }

    private static boolean editDistanceAtMost(String a, String b, int maxDist) {
        if (Math.abs(a.length() - b.length()) > maxDist) return false;
        int n = a.length();
        int m = b.length();
        int[] prev = new int[m + 1];
        int[] curr = new int[m + 1];
        for (int j = 0; j <= m; j++) prev[j] = j;
        for (int i = 1; i <= n; i++) {
            curr[0] = i;
            int rowMin = curr[0];
            for (int j = 1; j <= m; j++) {
                int cost = a.charAt(i - 1) == b.charAt(j - 1) ? 0 : 1;
                int val = Math.min(Math.min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + cost);
                curr[j] = val;
                if (val < rowMin) rowMin = val;
            }
            if (rowMin > maxDist) return false;
            int[] tmp = prev; prev = curr; curr = tmp;
        }
        return prev[m] <= maxDist;
    }

    private static boolean nearWord(String first, String second) {
        if (Math.min(first.length(), second.length()) < 4) return false;
        String a = ocrSkeleton(first);
        String b = ocrSkeleton(second);
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

    private static String ocrSkeleton(String word) {
        String s = word.replace("rn", "m").replace("vv", "w");
        StringBuilder sb = new StringBuilder(s.length());
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            switch (c) {
                case '0': sb.append('o'); break;
                case '1': sb.append('l'); break;
                case '3': sb.append('e'); break;
                case '4': sb.append('a'); break;
                case '5': sb.append('s'); break;
                default: sb.append(c); break;
            }
        }
        return sb.toString();
    }

    private static boolean isReadableUnknown(String body) {
        if (body == null || body.trim().length() < 16) return false;
        String[] tokens = body.trim().split("\\s+");
        int total = 0;
        int clean = 0;
        for (String tok : tokens) {
            String trimmed = tok.replaceAll("^[^A-Za-z0-9]+|[^A-Za-z0-9]+$", "");
            if (trimmed.isEmpty()) continue;
            total++;
            if (trimmed.length() == 1 && (trimmed.equalsIgnoreCase("a") || trimmed.equals("I"))) {
                clean++;
                continue;
            }
            if (trimmed.length() < 2) continue;
            boolean ok = true;
            boolean hasVowel = false;
            int innerUpper = 0;
            for (int i = 0; i < trimmed.length(); i++) {
                char c = trimmed.charAt(i);
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) {
                    char lc = Character.toLowerCase(c);
                    if (lc == 'a' || lc == 'e' || lc == 'i' || lc == 'o' || lc == 'u' || lc == 'y') {
                        hasVowel = true;
                    }
                    if (i > 0 && c >= 'A' && c <= 'Z') innerUpper++;
                } else if (c != '\'' && c != '-') {
                    ok = false;
                    break;
                }
            }
            if (ok && hasVowel && innerUpper == 0) clean++;
        }
        return total >= 4 && clean >= 4 && ((double) clean / total) >= 0.55;
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

        boolean isCatchAll() {
            return "unknown_event".equals(id);
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

        boolean matchesGlued(String compactLine) {
            if (compactLine == null || compactLine.isEmpty()) return false;
            for (String[] group : tokenGroups) {
                if (group.length < 2) continue;
                StringBuilder sb = new StringBuilder();
                for (String token : group) sb.append(token);
                String joined = intentSkeleton(sb.toString());
                if (joined.length() < 8) continue;
                if (compactLine.contains(joined)) return true;
            }
            return false;
        }

        boolean matchesIntentMultiTypo(String[] words) {
            if (words == null || words.length == 0) return false;
            String[] wordSks = new String[words.length];
            for (int i = 0; i < words.length; i++) wordSks[i] = intentSkeleton(words[i]);
            for (String[] group : tokenGroups) {
                if (group.length < 2) continue;
                int substantialTokens = 0;
                for (String token : group) {
                    if (intentSkeleton(token).length() >= 4) substantialTokens++;
                }
                if (group.length == 2 && substantialTokens < 2) continue;

                boolean allMatched = true;
                boolean strongAnchor = false;
                for (String token : group) {
                    String tokSk = intentSkeleton(token);
                    int maxDist = tokSk.length() >= 8 ? 2 : (tokSk.length() >= 4 ? 1 : 0);
                    int bestDist = Integer.MAX_VALUE;
                    for (String wSk : wordSks) {
                        if (wSk.equals(tokSk)) {
                            bestDist = 0;
                            break;
                        }
                        if (tokSk.length() >= 5 && wSk.length() > tokSk.length() && wSk.contains(tokSk)) {
                            if (bestDist > 1) bestDist = 1;
                            continue;
                        }
                        if (maxDist >= 1 && editDistanceAtMost(wSk, tokSk, 1)) {
                            if (bestDist > 1) bestDist = 1;
                        } else if (maxDist >= 2 && bestDist > 2 && editDistanceAtMost(wSk, tokSk, 2)) {
                            bestDist = 2;
                        }
                    }
                    if (bestDist > maxDist) {
                        allMatched = false;
                        break;
                    }
                    if (tokSk.length() >= 4 && bestDist <= 1) strongAnchor = true;
                }
                if (allMatched && (strongAnchor || group.length >= 3)) return true;
            }
            return false;
        }
    }

    static String timelineSig(String text) {
        String clean = normalize(text);
        return clean.length() > 120 ? clean.substring(0, 120).trim() : clean;
    }

    static final class Detection {
        final Rule rule;
        final String text;
        final String key;
        final String bodyKey;
        final int timelineIndex;

        Detection(Rule rule, String text, String key, int timelineIndex) {
            this.rule = rule;
            this.text = text;
            this.key = key;
            this.bodyKey = EventMatcher.bodyKey(text);
            this.timelineIndex = timelineIndex;
        }
    }

    static final class Frame {
        final List<Detection> events = new ArrayList<>();
        final List<String> timeline = new ArrayList<>();
        boolean readable;
    }

    private EventMatcher() {}
}

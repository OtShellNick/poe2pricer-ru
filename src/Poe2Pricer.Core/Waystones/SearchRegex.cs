using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Poe2Pricer.Core.Market;

namespace Poe2Pricer.Core.Waystones;

// The text for the game's stash search box, built from a waystone profile, plus the building
// blocks the tablet search reuses. The game splits the text into terms at spaces (quotes keep
// spaces inside a term); each term is a case-insensitive regex that must match one of the
// item's lines, a leading "!" turns it into "matches none". Every pattern here is chosen by
// trying short candidates and checking them against the whole pool of line templates, so
// that a pattern never picks up a line of another mod.
// Written anew on 21.10.2026 from a description of the behaviour alone (clean room; record in
// the project's docs/clean-room). Change it from that description and the tests only: no code
// from other search-text tools, whose licences do not fit this one, and no earlier version of
// this file.
public static partial class SearchRegex
{
    public const int Limit = 250;

    public static readonly string[] Labels =
    [
        "Waystone (Tier #)", "Revives Available: #", "Item Rarity: +#%", "Pack Size: +#%", "Monster Rarity: +#%",
        "Monster Effectiveness: +#%", "Waystone Drop Chance: +#%", "Corrupted",
        "Can be used in a Map Device, allowing you to enter a Map. Waystones can only be used once.",
    ];

    public static IReadOnlyList<string> Pool { get; } = [.. Labels, .. WaystoneMods.All.Select(m => m.Text)];

    // The German client writes every line in German, and the stash search matches what it
    // shows: the same labels in the same order, from the game files.
    public static readonly string[] LabelsDe =
    [
        "Wegstein (Level #)", "Wiederbelebungen verfügbar: #", "Gegenstandsseltenheit: +#%", "Monstergruppengröße: +#%",
        "Monsterseltenheit: +#%", "Monster-Schlagkraft: +#%", "Chance auf fallen gelassene Wegsteine: +#%", "Verderbt",
        "Kann in einem Kartenapparat verwendet werden und gewährt dir Zutritt zu einer Karte. Wegsteine können nur einmal verwendet werden.",
    ];

    public static IReadOnlyList<string> PoolDe { get; } = [.. LabelsDe, .. WaystoneMods.All.Select(m => m.De ?? m.Text)];

    // What one language of the client brings: its labels, its pool, a mod's template in it.
    private sealed record Wording(string[] Labels, IReadOnlyList<string> Pool, Func<WaystoneMod, string> Text);

    private static readonly Wording English = new(Labels, Pool, m => m.Text), German = new(LabelsDe, PoolDe, m => m.De ?? m.Text);

    private static Wording In(string language) => language == GameLanguage.German ? German : English;

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // ---- Search text from a profile -------------------------------------------------------

    public static string Build(WaystoneProfile profile) => Build(profile, GameLanguage.English);

    // language: the client's (GameLanguage); the lines are matched in the words it shows.
    public static string Build(WaystoneProfile profile, string language)
    {
        var w = In(language);
        var terms = new List<string>();
        // The tier always: it names the item class, without it a profile's mod terms match tablet
        // lines in a mixed tab too (review before 1.2.0). A minimum above the maximum counts as
        // the other way round.
        int low = profile.TierMin ?? 1, high = profile.TierMax ?? 16;
        terms.Add(Quote(NumberAnchor(w.Labels[0], w.Pool) + Range(Math.Min(low, high), Math.Max(low, high)) + @"\)"));
        if (profile.RevivesMin is not null || profile.RevivesMax is not null)
            terms.Add(Quote(NumberAnchor(w.Labels[1], w.Pool) + Range(profile.RevivesMin ?? 0, profile.RevivesMax ?? 9) + "$"));
        foreach (var (label, minimum) in Summary(w.Labels, profile.ItemRarity, profile.PackSize, profile.MonsterRarity, profile.Effectiveness, profile.DropChance))
            if (minimum is { } n)
                terms.Add(Quote(Fragment(label, w.Pool) + ".*" + AtLeast(n) + "%"));
        // The whole line: "corrupted" stands in other text too.
        string corrupted = "^" + w.Labels[7].ToLowerInvariant() + "$";
        if (profile.Corrupted == CorruptedChoice.Yes)
            terms.Add(corrupted);
        else if (profile.Corrupted == CorruptedChoice.No)
            terms.Add("!" + corrupted);

        terms.AddRange(profile.Groups.SearchTerms((r, withMinimum) => r.Mod is { } mod ? ModPattern(w.Text(mod), mod.Max, withMinimum ? r.Min : null, w.Pool) : null));
        return string.Join(" ", terms);
    }

    private static (string Label, int? Value)[] Summary(string[] labels, int? itemRarity, int? packSize, int? monsterRarity, int? effectiveness, int? dropChance) =>
        [(labels[2], itemRarity), (labels[3], packSize), (labels[4], monsterRarity), (labels[5], effectiveness), (labels[6], dropChance)];

    // Fragment of the label, then the character in front of the number. A range, unlike
    // AtLeast, can match the tail of a larger number ("[2-5]" in "12"), so the number needs a
    // non-digit right before it.
    private static string NumberAnchor(string label, IReadOnlyList<string> pool)
    {
        int hash = label.IndexOf('#');
        string before = hash > 0 && !char.IsDigit(label[hash - 1]) ? Escape(label[hash - 1].ToString()) : "";
        return Fragment(label, pool) + ".*" + before;
    }

    // ---- Number classes -------------------------------------------------------------------

    // Numbers >= n with as many digits as n, written digit position by digit position from the
    // last one upwards; trailing zeros of n make the lowest branch a whole block of free
    // digits at once. Longer numbers come from "\d{k+1,}" unless max rules them out. Without
    // an anchor this never matches part of a smaller number: every branch needs k digits, and
    // a number below n has at most k, so only itself could match, and it does not.
    public static string AtLeast(int n, int? max = null)
    {
        if (n <= 0)
            return max is { } small && small < 10 ? @"\d" : @"\d+";
        string s = n.ToString(CultureInfo.InvariantCulture);
        int k = s.Length;
        int zeros = s.Length - s.TrimEnd('0').Length;
        int last = k - 1 - zeros;
        var alternatives = new List<string> { s[..last] + Class(s[last] - '0', 9) + Digits(zeros) };
        for (int i = last - 1; i >= 0; i--)
            if (s[i] != '9')
                alternatives.Add(s[..i] + Class(s[i] - '0' + 1, 9) + Digits(k - 1 - i));
        if (max is not { } m || m >= Pow10(k))
            alternatives.Add(@"\d{" + (k + 1) + ",}");
        return Alternation(alternatives);
    }

    // Numbers low..high, split by digit count; within one digit count the shared leading
    // digits are fixed and the rest is cut into a lower edge, a middle block and an upper edge.
    public static string Range(int low, int high)
    {
        low = Math.Max(0, low);
        high = Math.Max(low, high);
        var alternatives = new List<string>();
        int lowDigits = low.ToString(CultureInfo.InvariantCulture).Length, highDigits = high.ToString(CultureInfo.InvariantCulture).Length;
        for (int d = lowDigits; d <= highDigits; d++)
        {
            int from = Math.Max(low, d == 1 ? 0 : Pow10(d - 1)), to = Math.Min(high, Pow10(d) - 1);
            SameLength(from.ToString(CultureInfo.InvariantCulture), to.ToString(CultureInfo.InvariantCulture), "", alternatives);
        }
        return Alternation(alternatives);
    }

    private static void SameLength(string a, string b, string prefix, List<string> into)
    {
        int common = 0;
        while (common < a.Length && a[common] == b[common])
            common++;
        if (common == a.Length)
        {
            into.Add(prefix + a);
            return;
        }
        prefix += a[..common];
        a = a[common..];
        b = b[common..];
        int rest = a.Length - 1;
        int from = a[0] - '0', to = b[0] - '0';
        bool lowerEdge = a[1..].Any(c => c != '0'), upperEdge = b[1..].Any(c => c != '9');
        if (lowerEdge)
        {
            SameLength(a, a[0] + new string('9', rest), prefix, into);
            from++;
        }
        if (upperEdge)
            to--;
        if (from <= to)
            into.Add(prefix + Class(from, to) + Digits(rest));
        if (upperEdge)
            SameLength(b[0] + new string('0', rest), b, prefix, into);
    }

    private static string Class(int low, int high) =>
        low == high ? low.ToString(CultureInfo.InvariantCulture) : low == 0 && high == 9 ? @"\d" : $"[{low}-{high}]";

    private static string Digits(int count) => count switch { 0 => "", 1 => @"\d", 2 => @"\d\d", _ => @"\d{" + count + "}" };

    private static string Alternation(List<string> alternatives) =>
        alternatives.Count == 1 ? alternatives[0] : "(" + string.Join("|", alternatives) + ")";

    private static int Pow10(int e)
    {
        int p = 1;
        for (int i = 0; i < e; i++)
            p *= 10;
        return p;
    }

    // ---- Mod patterns ---------------------------------------------------------------------

    public static string ModPattern(WaystoneMod mod, int? minimum) => ModPattern(mod.Text, mod.Max, minimum, Pool);

    public static string ModPattern(string text, int? max, int? minimum, IReadOnlyList<string> pool)
    {
        if (minimum is null || !text.Contains('#'))
            return Fragment(text, pool);
        return Cache.GetOrAdd((text, max, minimum, pool), key =>
        {
            var lines = LinesOf(key.Pool);
            string number = AtLeast(key.Minimum!.Value, key.Max);
            bool Works(string p) => Verify(p, key.Text, key.Minimum, key.Max, lines);
            return Contexts(key.Text, number, strict: true).FirstOrDefault(Works)
                ?? Contexts(key.Text, number, strict: false).FirstOrDefault(Works)
                // Nothing short enough worked: the whole line, the number in its place.
                ?? "^" + WholeLine(key.Text, number) + "$";
        });
    }

    public static string Fragment(string text) => Fragment(text, Pool);

    // The shortest piece of the template's own wording that no other template contains
    // (templates containing this whole one are exempt: they say the same). When every piece
    // is shared, the number's position tells lines apart: the words right before and/or after
    // "#" with "\d+" between them (e.g. "# additional Ess" against "an additional Essence"),
    // or a piece elsewhere joined to the number by ".*". Only when even that fails are the
    // singular/plural precautions dropped (a template that differs from another by nothing
    // but a plural "s"), and the last resort is the whole line.
    public static string Fragment(string text, IReadOnlyList<string> pool) =>
        Cache.GetOrAdd((text, null, null, pool), key =>
        {
            var lines = LinesOf(key.Pool);
            bool Works(string p) => Verify(p, key.Text, null, null, lines);
            foreach (bool strict in new[] { true, false })
            {
                var literal = Pieces(key.Text, int.MaxValue, strict)
                    .OrderBy(p => EscapedLength(key.Text, p.Start, p.End))
                    .ThenBy(p => WordStart(key.Text, p.Start) ? 0 : 1)
                    .ThenBy(p => p.Start)
                    .Select(p => Escape(key.Text[p.Start..p.End]))
                    .FirstOrDefault(Works);
                if (literal is not null)
                    return literal;
                if (key.Text.Contains('#') && Contexts(key.Text, @"\d+", strict).FirstOrDefault(Works) is { } context)
                    return context;
            }
            return "^" + WholeLine(key.Text, @"\d+") + "$";
        });

    private static readonly ConcurrentDictionary<(string Text, int? Max, int? Minimum, IReadOnlyList<string> Pool), string> Cache = new();

    private static string WholeLine(string text, string number) => string.Join(number, text.Split('#').Select(Escape));

    // At least four characters, and two letters on each side of a space inside: shorter pieces
    // ("Fla", "inf", "e e") turn up in item names (magic affixes, rare names), which the pool
    // does not hold (review before 1.2.0).
    private const int MinPiece = 4, BesideSpace = 2;

    private static bool SpacedWell(string text, int start, int end)
    {
        for (int i = start; i < end; i++)
            if (text[i] == ' ' && (Letters(text, i - 1, -1, start) < BesideSpace || Letters(text, i + 1, 1, end) < BesideSpace))
                return false;
        return true;
    }

    // Letters in a row from at, going step, without leaving [limit, …) or (…, limit).
    private static int Letters(string text, int at, int step, int limit)
    {
        int n = 0;
        for (int i = at; step < 0 ? i >= limit : i < limit; i += step)
        {
            if (!char.IsLetter(text[i]))
                break;
            n++;
        }
        return n;
    }

    // A piece of literal text usable on its own: inside one stretch between numbers, with a
    // letter, no digits (so it can never line up with another line's number), no space at
    // either end. Strict, it also must not end on a word's final "s", nor run through a
    // plural-looking word anywhere: the game writes a count word in the singular at 1, and
    // an "s" glued to punctuation ("Effectiveness:") is too fragile a mark anyway.
    private static IEnumerable<(int Start, int End)> Pieces(string text, int maxLength, bool strict)
    {
        int segmentStart = 0;
        while (segmentStart <= text.Length)
        {
            int segmentEnd = text.IndexOf('#', segmentStart);
            if (segmentEnd < 0)
                segmentEnd = text.Length;
            for (int start = segmentStart; start < segmentEnd; start++)
            {
                if (text[start] == ' ')
                    continue;
                bool letter = false;
                for (int end = start + 1; end <= segmentEnd && end - start <= maxLength; end++)
                {
                    char c = text[end - 1];
                    if (char.IsDigit(c))
                        break;
                    if (strict && end - 1 > start && PluralEnd(text, end - 2))
                        break;
                    letter |= char.IsLetter(c);
                    if (c == ' ' || !letter || end - start < MinPiece || strict && SingularBreaks(text, end) || !SpacedWell(text, start, end))
                        continue;
                    yield return (start, end);
                }
            }
            segmentStart = segmentEnd + 1;
        }
    }

    // Candidates built around the first number: L N R (the words adjacent to the number, read
    // like the line itself and preferred while short), F.*N R (a piece before the number) and
    // N R.*F (a piece after it), shortest first. R pins the number to its slot when the line
    // also carries literal digits ("up to 100%"). The literal context must hold a letter: a
    // sign alone ("+", "-") is on many lines.
    private static IEnumerable<string> Contexts(string text, string number, bool strict)
    {
        const int maxContext = 60, maxPiece = 24, readable = 24;
        int hash = text.IndexOf('#');
        int right = text.IndexOf('#', hash + 1) is var next && next >= 0 ? next : text.Length;

        // Right contexts: R = text[(hash+1)..end].
        var rights = new List<int>();
        for (int end = hash + 1; end <= right && end - hash - 1 <= maxContext; end++)
        {
            if (end > hash + 1)
            {
                char c = text[end - 1];
                if (char.IsDigit(c))
                    break;
                if (strict && end - 1 > hash + 1 && PluralEnd(text, end - 2))
                    break;
                if (c == ' ' || strict && SingularBreaks(text, end))
                    continue;
            }
            rights.Add(end);
        }
        bool HasLetter(int from, int to) => text[from..to].Any(char.IsLetter);

        var candidates = new List<(int Tier, int Cost, int Form, int Tie, int LStart, int REnd, int FStart, int FEnd)>();
        // L N R; L = text[start..hash].
        for (int start = hash; start >= 0 && hash - start <= maxContext; start--)
        {
            if (start < hash && char.IsDigit(text[start]))
                break;
            if (start < hash && text[start] == ' ')
                continue;
            foreach (int end in rights)
                if (HasLetter(start, hash) || HasLetter(hash + 1, end))
                {
                    int context = EscapedLength(text, start, hash) + EscapedLength(text, hash + 1, end);
                    candidates.Add((context <= readable ? 0 : 1, context + number.Length, 0, hash - start, start, end, 0, 0));
                }
        }
        // F.*N R, with F before the number.
        foreach (var (fs, fe) in Pieces(text[..hash], maxPiece, strict))
            foreach (int end in rights)
                candidates.Add((1, EscapedLength(text, fs, fe) + 2 + number.Length + EscapedLength(text, hash + 1, end), 1, WordStart(text, fs) ? 0 : 1, hash, end, fs, fe));
        // N R.*F, with F after R.
        foreach (var (fs, fe) in Pieces(text, maxPiece, strict).Where(p => p.Start > hash && p.End <= right))
            foreach (int end in rights.Where(e => e <= fs))
                candidates.Add((1, number.Length + EscapedLength(text, hash + 1, end) + 2 + EscapedLength(text, fs, fe), 2, WordStart(text, fs) ? 0 : 1, hash, end, fs, fe));

        foreach (var c in candidates.OrderBy(c => c.Tier).ThenBy(c => c.Cost).ThenBy(c => c.Form).ThenBy(c => c.Tie).ThenBy(c => c.REnd))
        {
            string r = Escape(text[(hash + 1)..c.REnd]);
            yield return c.Form switch
            {
                0 => Escape(text[c.LStart..hash]) + number + r,
                1 => Escape(text[c.FStart..c.FEnd]) + ".*" + number + r,
                _ => number + r + ".*" + Escape(text[c.FStart..c.FEnd]),
            };
        }
    }

    private static bool WordStart(string text, int start) =>
        char.IsLetterOrDigit(text[start]) && (start == 0 || !char.IsLetterOrDigit(text[start - 1]));

    // An "s" closing a word of two or more letters.
    private static bool WordFinalS(string text, int i) =>
        (text[i] == 's' || text[i] == 'S') && i > 0 && char.IsLetter(text[i - 1]) && (i + 1 == text.Length || !char.IsLetter(text[i + 1]));

    // Whether a piece ending before index end would miss the singular form: it ends on a
    // word-final "s", or on the "e" of a plural "-es" ("Strongboxe" is not in "Strongbox").
    private static bool SingularBreaks(string text, int end) =>
        WordFinalS(text, end - 1) || end < text.Length && (text[end - 1] == 'e' || text[end - 1] == 'E') && PluralEnd(text, end);

    // An "s" closing a word of four or more letters: a noun that may be a plural count word.
    // Short words ("as", "is", "has") are never count words.
    private static bool PluralEnd(string text, int i) =>
        WordFinalS(text, i) && i >= 3 && char.IsLetter(text[i - 2]) && char.IsLetter(text[i - 3]);

    // ---- Checking a candidate against the pool --------------------------------------------

    // A template prepared for checking: "#" replaced by a character no pattern can take for a
    // digit, and where each "#" sits.
    private sealed record Line(string Text, string Blank, int[] Slots)
    {
        public string With(int slot, int value) =>
            Blank[..Slots[slot]] + value.ToString(CultureInfo.InvariantCulture) + Blank[(Slots[slot] + 1)..];

        public string WithAll(int value) => Text.Replace("#", value.ToString(CultureInfo.InvariantCulture));
    }

    private const char Hole = '\u0001';

    private static Line Prepare(string text) =>
        new(text, text.Replace('#', Hole), text.Select((c, i) => (c, i)).Where(x => x.c == '#').Select(x => x.i).ToArray());

    private static readonly ConditionalWeakTable<IReadOnlyList<string>, Line[]> Prepared = new();

    private static Line[] LinesOf(IReadOnlyList<string> pool) => Prepared.GetValue(pool, p => p.Select(Prepare).ToArray());

    // Whether the pattern matches its own line (at the minimum and max, or at any value when
    // there is no minimum), not below the minimum, and no line of any other template. Patterns
    // use digits only for the number and digit-free text around it, so for another template it
    // is enough to try each "#" in turn with a value the number part accepts (the minimum),
    // and once with no number at all (covers literal digits and plain text).
    private static bool Verify(string pattern, string own, int? minimum, int? max, Line[] pool)
    {
        var re = new Regex(pattern, Options);
        var self = Prepare(own);
        if (self.Slots.Length == 0)
        {
            if (!re.IsMatch(own))
                return false;
        }
        else if (minimum is { } min)
        {
            if (!re.IsMatch(self.WithAll(min)) || max is { } top && top > min && !re.IsMatch(self.WithAll(top)))
                return false;
            if (re.IsMatch(self.Blank) || min > 0 && (re.IsMatch(self.WithAll(min - 1)) || re.IsMatch(self.WithAll(0))))
                return false;
        }
        else if (!new[] { 1, 25, 150 }.All(v => re.IsMatch(self.WithAll(v))))
            return false;

        int sample = minimum is { } m && m > 0 ? m : 1;
        foreach (var other in pool)
        {
            // Only the template itself is exempt: one that contains it whole is another line
            // ("Abyssal Monsters have #% increased Effectiveness …"; review before 1.2.0).
            if (other.Text.Equals(own, StringComparison.OrdinalIgnoreCase))
                continue;
            if (re.IsMatch(other.Blank))
                return false;
            for (int slot = 0; slot < other.Slots.Length; slot++)
                if (re.IsMatch(other.With(slot, sample)))
                    return false;
        }
        return true;
    }

    // ---- Text helpers ---------------------------------------------------------------------

    private const string Meta = @".*+?^${}()|[]\/";

    internal static string Escape(string s)
    {
        var b = new StringBuilder(s.Length + 4);
        foreach (char c in s)
        {
            if (Meta.Contains(c))
                b.Append('\\');
            b.Append(c);
        }
        return b.ToString();
    }

    private static int EscapedLength(string text, int start, int end)
    {
        int n = end - start;
        for (int i = start; i < end; i++)
            if (Meta.Contains(text[i]))
                n++;
        return n;
    }

    // Quotes keep a term with a space together; "|" is quoted as the game's own examples do.
    internal static string Quote(string term) => term.Contains(' ') || term.Contains('|') ? "\"" + term + "\"" : term;

    // ---- Applying a search text the way the game does -------------------------------------

    public static IEnumerable<string> Terms(string search)
    {
        var term = new StringBuilder();
        bool quoted = false;
        foreach (char c in search)
        {
            if (c == '"')
                quoted = !quoted;
            else if (c == ' ' && !quoted)
            {
                if (term.Length > 0)
                    yield return term.ToString();
                term.Clear();
            }
            else
                term.Append(c);
        }
        if (term.Length > 0)
            yield return term.ToString();
    }

    public static bool Holds(string search, IEnumerable<string> itemLines)
    {
        var lines = itemLines.ToArray();
        foreach (string term in Terms(search))
        {
            bool negated = term.StartsWith('!');
            string expression = negated ? term[1..] : term;
            if (expression.Length == 0)
                continue;
            Regex re;
            try
            {
                re = new Regex(expression, Options);
            }
            catch (ArgumentException)
            {
                // Not a valid expression: taken as plain text.
                re = new Regex(Regex.Escape(expression), Options);
            }
            bool any = lines.Any(re.IsMatch);
            if (any == negated)
                return false;
        }
        return true;
    }

    // The waystone's lines as the search sees them, from a tooltip reading.
    public static IEnumerable<string> ItemLines(WaystoneReading reading) => ItemLines(reading, GameLanguage.English);

    public static IEnumerable<string> ItemLines(WaystoneReading reading, string language)
    {
        var w = In(language);
        yield return reading.Name;
        yield return w.Labels[0].Replace("#", reading.Tier.ToString(CultureInfo.InvariantCulture));
        if (reading.Revives is { } revives)
            yield return w.Labels[1].Replace("#", revives.ToString(CultureInfo.InvariantCulture));
        foreach (var (label, value) in Summary(w.Labels, reading.ItemRarity, reading.PackSize, reading.MonsterRarity, reading.Effectiveness, reading.DropChance))
            if (value is { } v)
                yield return label.Replace("#", v.ToString(CultureInfo.InvariantCulture));
        foreach (var mod in reading.Mods)
            yield return w.Text(mod.Mod).Replace("#", Math.Abs(mod.Value ?? mod.Mod.Min ?? 0).ToString(CultureInfo.InvariantCulture));
        if (reading.Corrupted)
            yield return w.Labels[7];
        yield return w.Labels[8];
    }
}

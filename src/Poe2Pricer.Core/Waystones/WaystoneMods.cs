using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Poe2Pricer.Core.Waystones;

// One line a waystone can carry: the text as the game writes it, with "#" where the number
// sits; prefix or suffix; the span of the number over all tiers; the stat id of the trade
// site's filter (TradeNegated: the site knows the line with the opposite sign, "more" for
// "less"). From data/waystones/mods.json, built by tools/waystone_mods.py from the game files.
// De: the line as the German client writes it; Text stays the line's name everywhere else
// (profiles, trade, search), so a profile holds in both languages.
public sealed record WaystoneMod(string Kind, string Text, string Stat, int? Min, int? Max, string? Trade, bool TradeNegated, string? De = null)
{
    public bool HasValue => Text.Contains('#');

    // Letters only, lower case, without the number: what an OCR line is compared with.
    public string Key { get; } = WaystoneMods.KeyOf(Text);

    public string KeyDe { get; } = WaystoneMods.KeyOf(De ?? Text);

    public override string ToString() => Text;
}

public static partial class WaystoneMods
{
    public static IReadOnlyList<WaystoneMod> All { get; } = Load();

    private static WaystoneMod[] Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Waystones.mods.json")
            ?? throw new InvalidOperationException("Waystones.mods.json missing");
        var root = JsonDocument.Parse(stream).RootElement;
        var mods = new List<WaystoneMod>();
        foreach (var entry in root.GetProperty("mods").EnumerateArray())
        {
            string kind = entry.GetProperty("kind").GetString()!;
            foreach (var line in entry.GetProperty("lines").EnumerateArray())
                mods.Add(new(
                    kind,
                    line.GetProperty("text").GetString()!,
                    line.GetProperty("stat").GetString()!,
                    line.TryGetProperty("min", out var min) ? min.GetInt32() : null,
                    line.TryGetProperty("max", out var max) ? max.GetInt32() : null,
                    line.TryGetProperty("trade", out var trade) && trade.ValueKind == JsonValueKind.String ? trade.GetString() : null,
                    line.TryGetProperty("tradeNegated", out var negated) && negated.GetBoolean(),
                    line.TryGetProperty("de", out var de) && de.ValueKind == JsonValueKind.String ? de.GetString() : null));
        }
        return mods.ToArray();
    }

    // The line as the tooltip shows it with Alt held: the roll's span after the number
    // ("30(25-30)%") and the tier at the end ("T1"); both go. Then only letters count, so
    // that the number, signs, per cent and the OCR's punctuation drop out.
    // Accents go too, "ß" is "ss": the English OCR reads a German line with "É", "Å" or "Ü"
    // where the game wrote "e", "ä" or "ü" (21.10.2026, "MONSTERSELTENHÉIT", "PRÅFIX"), and the
    // German texts are compared in the same plain letters.
    public static string KeyOf(string line) =>
        new(Plain(line).Replace("ß", "ss").Replace("ẞ", "SS").Normalize(System.Text.NormalizationForm.FormD).ToLowerInvariant()
            .Where(char.IsLetter).ToArray());

    private static string Plain(string line) => TierSuffix().Replace(RollSpan().Replace(line, ""), "");

    // The number on the line, if any: signed, the first one ("Monsters have +21% Critical
    // Damage Bonus" → 21; "-10% maximum Player Resistances" → -10, the line's own sign).
    // Two misreads go first: "%" read as "0/6" ("340/6" for "34%"; no mod text holds a "/", so
    // the slash can only come from the per cent sign — the form "04" stays, "104" may be a
    // number), and a digit inside a word ("ABKLlN9ZElT" for "Abklingzeit"), which is a letter.
    public static int? ValueOf(string line)
    {
        string plain = SlashPerCent().Replace(Plain(line), "$1%");
        foreach (Match m in Number().Matches(plain))
        {
            bool letterBefore = m.Index > 0 && char.IsLetter(plain[m.Index - 1]);
            bool letterAfter = m.Index + m.Length < plain.Length && char.IsLetter(plain[m.Index + m.Length]);
            if (!(letterBefore && letterAfter))
                return int.Parse(m.Groups[1].Value);
        }
        return null;
    }

    // The mod an OCR line stands for, and how far the OCR went from its text: at most
    // Tolerance of the letters may differ (the Windows OCR turns "have" into "ÅAV€S" and loses
    // a letter here and there, which the letters-only key absorbs for the most part). Among
    // the four "Monsters deal #% of Damage as Extra <element>" lines the nearest wins; a
    // garble would have to hit the element's letters alone to cross over.
    public static (WaystoneMod Mod, int Distance)? Match(string line) => Nearest(KeyOf(line), All, m => [m.Key, m.KeyDe]);

    // The item whose text a line's key stands for. First the whole line, within Tolerance of
    // the letters. Failing that, a part of a line: the OCR now and then loses the first words
    // ("ERHALTEN 31% VERRINGERTE FLÄSCHCHENFVLLUNGEN" for "Spieler erhalten …", 21.10.2026).
    // The texts are known and few, so a piece that fits one of them, and only one, better than
    // any other, still names it; two that fit alike leave the line unread.
    public static (T Item, int Distance)? Nearest<T>(string key, IEnumerable<T> items, Func<T, string[]> keysOf) where T : class
    {
        if (key.Length < MinimumKey)
            return null;
        T? best = null;
        int bestDistance = int.MaxValue, bestLength = 0, runnerUp = int.MaxValue;
        foreach (var item in items)
        {
            // The item's nearest wording (English or German).
            int distance = int.MaxValue, length = 0;
            foreach (string itemKey in keysOf(item))
            {
                if (Math.Abs(itemKey.Length - key.Length) > Tolerance(itemKey.Length))
                    continue;
                int d = TitleReader.Distance(key, itemKey);
                if (d < distance)
                    (distance, length) = (d, itemKey.Length);
            }
            if (distance < bestDistance)
                (runnerUp, best, bestDistance, bestLength) = (bestDistance, item, distance, length);
            else if (distance < runnerUp)
                runnerUp = distance;
        }
        // Two texts as near as each other leave the line unread: "Monsters deal 20% of Damage as
        // Extra" without its element was taken for the first of the four (review before 1.2.0).
        if (best is not null && bestDistance <= Tolerance(bestLength) && bestDistance < runnerUp)
            return (best, bestDistance);
        if (best is not null && bestDistance <= Tolerance(bestLength))
            return null;
        if (key.Length < MinimumPart)
            return null;
        (best, bestDistance) = (null, int.MaxValue);
        int second = int.MaxValue;
        foreach (var item in items)
        {
            int distance = keysOf(item).Min(k => PartDistance(key, k));
            if (distance < bestDistance)
                (second, best, bestDistance) = (bestDistance, item, distance);
            else if (distance < second)
                second = distance;
        }
        return best is not null && bestDistance <= Tolerance(key.Length) && bestDistance < second ? (best, bestDistance) : null;
    }

    // How far a key is from the closest run of letters inside a text: the text's start and end
    // may be left out at no cost.
    public static int PartDistance(string key, string text)
    {
        var d = new int[text.Length + 1];
        for (int i = 1; i <= key.Length; i++)
        {
            int prev = d[0];
            d[0] = i;
            for (int j = 1; j <= text.Length; j++)
            {
                int old = d[j];
                d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (key[i - 1] == text[j - 1] ? 0 : 1));
                prev = old;
            }
        }
        return d.Min();
    }

    // A value far beyond what the mod rolls is a misread: a "%" read as a digit makes it about
    // ten times as big ("226" for "22%"). Up to twice the highest roll stays, in case a
    // corrupted or otherwise enhanced waystone shows more than the game files list.
    public static int? Plausible(int? value, int? max) => value is { } v && max is { } m && Math.Abs(v) > 2 * m ? null : value;

    public const int MinimumKey = 6;

    // The least letters a piece of a line needs to name its text: two short words ("Monster
    // haben") stand at the start of many lines and name none.
    public const int MinimumPart = 12;

    // One letter in six may be off; at least two.
    public static int Tolerance(int keyLength) => Math.Max(2, keyLength / 6);

    [GeneratedRegex(@"\((\s*-?\d+\s*(-|–|to)\s*-?\d+\s*)\)")]
    private static partial Regex RollSpan();

    [GeneratedRegex(@"\s+T\s?\d{1,2}\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TierSuffix();

    [GeneratedRegex(@"([+-]?\d+)")]
    private static partial Regex Number();

    [GeneratedRegex(@"(\d)\s*(0/0|o/o|O/O|0/6)(?!\d)")]
    private static partial Regex SlashPerCent();
}

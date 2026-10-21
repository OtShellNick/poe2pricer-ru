using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Poe2Pricer.Core.Waystones;

namespace Poe2Pricer.Core.Tablets;

// A kind of tablet (Breach Tablet, Overseer Tablet, ...): its name as the tooltip's title
// says it, the game's tag, and the two implicit lines every tablet of the kind carries
// ("Adds Abysses to a Map", "# uses remaining"). De, ImplicitDe: the same in the German client
// ("Abgrund-Tafel", "# Nutzungen übrig"); Name stays the kind's name everywhere else.
public sealed record TabletKind(string Name, string Tag, string[] Implicit, string? De = null, string[]? ImplicitDe = null)
{
    public override string ToString() => Name;
}

// One line a tablet can carry, as WaystoneMod for waystones: Tablet names the kind that alone
// rolls it, null for a line every kind rolls. From data/tablets/mods.json
// (tools/tablet_mods.py).
public sealed record TabletMod(string Kind, string? Tablet, string Text, string Stat, int? Min, int? Max, string? Trade, bool TradeNegated, string? De = null)
{
    public bool HasValue => Text.Contains('#');

    public string Key { get; } = WaystoneMods.KeyOf(Text);

    // The line as the German client writes it (WaystoneMod.KeyDe).
    public string KeyDe { get; } = WaystoneMods.KeyOf(De ?? Text);

    // The same thing said twice: a generic prefix ("Map contains an additional Essence") and
    // an Overseer suffix ("Map contains # additional Essences") have different stats and
    // trade ids, and on an Overseer Tablet the line at 1 reads like the prefix. A profile
    // that asks for one takes the other too, and the trade query asks for either.
    [System.Text.Json.Serialization.JsonIgnore]
    public TabletMod? Twin { get; internal set; }

    public override string ToString() => Text;
}

public static partial class TabletMods
{
    public static IReadOnlyList<TabletKind> Kinds { get; }
    public static IReadOnlyList<TabletMod> All { get; }

    static TabletMods()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Tablets.mods.json")
            ?? throw new InvalidOperationException("Tablets.mods.json missing");
        var root = JsonDocument.Parse(stream).RootElement;
        Kinds = root.GetProperty("kinds").EnumerateArray()
            .Select(k => new TabletKind(k.GetProperty("name").GetString()!, k.GetProperty("tag").GetString()!, k.GetProperty("implicit").EnumerateArray().Select(l => l.GetString()!).ToArray(),
                k.TryGetProperty("de", out var de) ? de.GetString() : null,
                k.TryGetProperty("implicitDe", out var implicitDe) ? implicitDe.EnumerateArray().Select(l => l.GetString()!).ToArray() : null))
            .ToArray();
        var mods = new List<TabletMod>();
        foreach (var entry in root.GetProperty("mods").EnumerateArray())
        {
            string kind = entry.GetProperty("kind").GetString()!;
            string? tablet = entry.TryGetProperty("tablet", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            foreach (var line in entry.GetProperty("lines").EnumerateArray())
                mods.Add(new(
                    kind,
                    tablet,
                    line.GetProperty("text").GetString()!,
                    line.GetProperty("stat").GetString()!,
                    line.TryGetProperty("min", out var min) && min.ValueKind == JsonValueKind.Number ? min.GetInt32() : null,
                    line.TryGetProperty("max", out var max) && max.ValueKind == JsonValueKind.Number ? max.GetInt32() : null,
                    line.TryGetProperty("trade", out var trade) && trade.ValueKind == JsonValueKind.String ? trade.GetString() : null,
                    line.TryGetProperty("tradeNegated", out var negated) && negated.ValueKind == JsonValueKind.True,
                    line.TryGetProperty("de", out var lineDe) && lineDe.ValueKind == JsonValueKind.String ? lineDe.GetString() : null));
        }
        foreach (var generic in mods.Where(m => m.Tablet is null && !m.HasValue))
        foreach (var own in mods.Where(m => m.Tablet is not null && m.HasValue && m.Stat != generic.Stat))
            if (Singulars(generic.Key).Overlaps(Singulars(own.Key)))
                (generic.Twin, own.Twin) = (own, generic);
        All = mods;
    }

    // The key without "an" before "additional" and without a plural ending: what the two
    // wordings of a twin pair share.
    private static HashSet<string> Singulars(string key)
    {
        string k = key.Replace("anadditional", "additional");
        var set = new HashSet<string> { k };
        if (k.EndsWith("es"))
            set.Add(k[..^2]);
        if (k.EndsWith('s'))
            set.Add(k[..^1]);
        return set;
    }

    public static TabletKind? KindNamed(string name) => Kinds.FirstOrDefault(k => string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase));

    // The lines a tablet of this kind can carry: the generic ones and the kind's own.
    public static IReadOnlyList<TabletMod> For(TabletKind kind) =>
        ForKind.GetOrAdd(kind.Name, name => All.Where(m => m.Tablet is null || m.Tablet == name).ToArray());

    // Built once per kind: the reading asks for the list on every unread line of every frame.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, TabletMod[]> ForKind = new();

    // The kind a title line names: "Irradiated Tablet" on its own line (a rare tablet, the
    // name stands above) or inside the name of a magic one ("Abounding Irradiated Tablet of
    // Shrines").
    // In the German client "Bestrahlte Tafel", "Abgrund-Tafel" (with Alt held the English name).
    public static TabletKind? KindOf(string line)
    {
        if (Title().Match(line) is { Success: true } m)
            return Kinds.FirstOrDefault(k => k.Name.StartsWith(m.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
        foreach (Match german in GermanTitle().Matches(line))
        {
            string word = WaystoneMods.KeyOf(german.Groups[1].Value);
            if (Kinds.FirstOrDefault(k => k.De is { } de && WaystoneMods.KeyOf(de.Split(' ', '-')[0]) == word) is { } kind)
                return kind;
        }
        return null;
    }

    public static bool IsTitle(string line) => KindOf(line) is not null;

    // The mod of this kind an OCR line stands for, as WaystoneMods.Match: nearest key within
    // the tolerance, letters only.
    public static (TabletMod Mod, int Distance)? Match(string line, TabletKind kind) =>
        WaystoneMods.Nearest(WaystoneMods.KeyOf(line), For(kind), m => [m.Key, m.KeyDe]);

    [GeneratedRegex(@"\b(Abyss|Breach|Delirium|Expedition|Irradiated|Overseer|Ritual|Temple)\W{1,3}Tablet\b", RegexOptions.IgnoreCase)]
    private static partial Regex Title();

    // "<word> Tafel" or "<word>-Tafel": the word names the kind (Abgrund, Riss, Bestrahlte, ...).
    [GeneratedRegex(@"(\w+)\W{1,3}Tafel\b", RegexOptions.IgnoreCase)]
    private static partial Regex GermanTitle();
}

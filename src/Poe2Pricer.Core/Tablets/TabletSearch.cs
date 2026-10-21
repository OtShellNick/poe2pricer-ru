using Poe2Pricer.Core.Market;
using Poe2Pricer.Core.Waystones;

namespace Poe2Pricer.Core.Tablets;

// The search text for the game's stash search from a tablet profile, as SearchRegex builds
// it for waystones: the kind by its name, uses left at least N on the "# uses remaining"
// line, wanted mods by their shortest unique fragments, unwanted ones negated. The pool a
// fragment must be unique against: every line any tablet can carry.
public static class TabletSearch
{
    public static readonly string[] Labels =
    [
        "Tablet", "Tablet: Item Level #", "# uses remaining", "Can be used in a personal Map Device to add modifiers to a Map.",
    ];

    public static IReadOnlyList<string> Pool { get; } = [.. Labels, .. TabletMods.Kinds.Select(k => k.Name), .. TabletMods.Kinds.Select(k => k.Implicit[0]), .. TabletMods.All.Select(m => m.Text)];

    // The German client's words (SearchRegex.LabelsDe): the same lines as the game files write them.
    public static readonly string[] LabelsDe =
    [
        "Tafel", "Tafel: Gegenstandsstufe #", "# Nutzungen übrig", "Kann in einem persönlichen Kartenapparat verwendet werden, um einer Karte Modifikatoren hinzuzufügen.",
    ];

    public static IReadOnlyList<string> PoolDe { get; } =
        [.. LabelsDe, .. TabletMods.Kinds.Select(k => k.De ?? k.Name), .. TabletMods.Kinds.Select(k => (k.ImplicitDe ?? k.Implicit)[0]), .. TabletMods.All.Select(m => m.De ?? m.Text)];

    private sealed record Wording(string[] Labels, IReadOnlyList<string> Pool, Func<TabletKind, string> Kind, Func<TabletKind, string> Implicit, Func<TabletMod, string> Text, string Uses);

    private static readonly Wording English = new(Labels, Pool, k => k.Name, k => k.Implicit[0], m => m.Text, " use"),
        German = new(LabelsDe, PoolDe, k => k.De ?? k.Name, k => (k.ImplicitDe ?? k.Implicit)[0], m => m.De ?? m.Text, " Nutz");

    private static Wording In(string language) => language == GameLanguage.German ? German : English;

    public static string Build(TabletProfile profile) => Build(profile, GameLanguage.English);

    // language: the client's (GameLanguage), as SearchRegex.Build.
    public static string Build(TabletProfile profile, string language)
    {
        var w = In(language);
        var terms = new List<string>();
        if (profile.KindOf() is { } kind)
            terms.Add(SearchRegex.Quote(w.Kind(kind)));
        if (profile.UsesMin is { } uses)
            terms.Add(SearchRegex.Quote("^" + SearchRegex.AtLeast(uses, 10) + w.Uses));
        terms.AddRange(profile.Groups.SearchTerms((r, withMinimum) => r.Mod is { } mod ? Pattern(mod, withMinimum ? r.Min : null, w) : null));
        return string.Join(" ", terms);
    }

    // A mod's pattern; with a twin, either wording — the twin only when it can meet the minimum:
    // one without "#" stands for 1 (review before 1.2.0: "≥ 2 Strongboxes" found one).
    private static string Pattern(TabletMod mod, int? minimum, Wording w)
    {
        string own = SearchRegex.ModPattern(w.Text(mod), mod.Max, minimum, w.Pool);
        if (mod.Twin is not { } twin || (!twin.HasValue && minimum is > 1))
            return own;
        return own + "|" + SearchRegex.ModPattern(w.Text(twin), twin.Max, twin.HasValue ? minimum : null, w.Pool);
    }

    // The lines of a tablet's text as the game's search sees them, from a tooltip reading.
    public static IEnumerable<string> ItemLines(TabletReading reading) => ItemLines(reading, GameLanguage.English);

    public static IEnumerable<string> ItemLines(TabletReading reading, string language)
    {
        var w = In(language);
        yield return reading.Name;
        if (reading.Name != w.Kind(reading.Kind))
            yield return w.Kind(reading.Kind);
        yield return w.Implicit(reading.Kind);
        if (reading.Uses is { } uses)
            yield return w.Labels[2].Replace("#", uses.ToString());
        foreach (var mod in reading.Mods)
            yield return w.Text(mod.Mod).Replace("#", Math.Abs(mod.Value ?? mod.Mod.Min ?? 0).ToString());
        yield return w.Labels[3];
    }
}

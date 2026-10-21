using Poe2Pricer.Core.Market;

namespace Poe2Pricer.Core.Tablets;

// What the player asks of a tablet of one kind: uses left at least this many, mods wanted
// (with a value at least this high) and mods not wanted. One profile per kind: the kind is
// the profile's name. As WaystoneProfile, it yields the search text for the stash search
// (TabletSearch), the trade query (TabletTradeQuery) and the vocabulary of the live question
// at a tooltip.
public sealed record TabletProfile
{
    public string Kind { get; init; } = "";
    public int? UsesMin { get; init; }
    // The mod rows in groups (ModGroup: and, or, not), joined by "and".
    public ModGroup<TabletModRule>[] Groups { get; init; } = [];

    // Rows a group of the Tablets tab takes at most: a tablet carries four mods.
    public const int MaxRules = 4;

    // Not a property: a get-only property would be written into settings.json.
    public TabletKind? KindOf() => TabletMods.KindNamed(Kind);

    public bool Equals(TabletProfile? other) =>
        other is not null && (Kind, UsesMin) == (other.Kind, other.UsesMin) && Groups.SequenceEqual(other.Groups);

    public override int GetHashCode() => HashCode.Combine(Kind, UsesMin, Groups.Length);

    // The profile's conditions, each as met or not by a tablet read from its tooltip: the
    // kind; uses exactly (uses the OCR did not read count as 0); a wanted mod met when the
    // tablet has it (under either wording of a twin pair) with at least the minimum less the
    // tolerance (per cent); a mod not wanted met when the tablet lacks it.
    public IEnumerable<bool> Conditions(TabletReading reading, int tolerance)
    {
        if (UsesMin is { } uses)
            yield return (reading.Uses ?? 0) >= uses;
        bool Has(TabletModRule rule) => reading.Mods.Any(m => rule.Names(m.Mod) && (rule.Min is not { } min || m.Value is { } v && Tolerance.Reaches(Math.Abs(v), min, tolerance)));
        foreach (var met in Groups.Conditions(Has, rule => reading.Mods.Any(m => rule.Names(m.Mod))))
            yield return met;
    }

    public int ConditionCount() => (UsesMin is null ? 0 : 1) + Groups.Sum(g => g.Kind == GroupKind.Or ? (g.Rules.Length > 0 ? 1 : 0) : g.Rules.Length);

    // Every mod row of the groups of these kinds.
    public IEnumerable<TabletModRule> RulesOf(params GroupKind[] kinds) => Groups.RulesOf(kinds);

    public int Satisfied(TabletReading reading, int tolerance) => Conditions(reading, tolerance).Count(met => met);

    // Whether the tablet is of the kind and meets every condition.
    public bool Matches(TabletReading reading, int tolerance = 0) =>
        string.Equals(reading.Kind.Name, Kind, StringComparison.OrdinalIgnoreCase) && Conditions(reading, tolerance).All(met => met);
}

// A mod on a profile's list, by its text; Min: the value at least this high.
public sealed record TabletModRule(string Text, int? Min = null)
{
    // Looked up, not stored (WaystoneModRule.Mod).
    [System.Text.Json.Serialization.JsonIgnore]
    public TabletMod? Mod => TabletMods.All.FirstOrDefault(m => m.Text == Text);

    public bool Names(TabletMod mod) => mod.Text == Text || mod.Twin?.Text == Text;
}

// The Tablets tab's settings: one profile per kind and the tolerance (per cent;
// Market.Tolerance). The live price is switched in Settings.LiveTradePrice.
public sealed record TabletSettings
{
    public TabletProfile[] Profiles { get; init; } = [];
    public int Tolerance { get; init; } = Market.Tolerance.Default;

    // The kind's profile, if it has one with a condition.
    public TabletProfile? For(TabletKind kind) => Profiles.FirstOrDefault(p => string.Equals(p.Kind, kind.Name, StringComparison.OrdinalIgnoreCase) && p.ConditionCount() > 0);

    public bool Equals(TabletSettings? other) =>
        other is not null && Tolerance == other.Tolerance && Profiles.SequenceEqual(other.Profiles);

    public override int GetHashCode() => HashCode.Combine(Tolerance, Profiles.Length);
}

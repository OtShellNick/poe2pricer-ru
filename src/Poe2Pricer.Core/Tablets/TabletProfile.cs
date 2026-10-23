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
        var paired = Groups.Paired();
        bool Has(TabletModRule rule) => reading.Mods.Any(m => rule.Names(m.Mod, !paired.Contains(rule)) && (rule.Min is not { } min || m.Value is { } v && Tolerance.Reaches(Math.Abs(v), min, tolerance)));
        foreach (var met in Groups.Conditions(Has, rule => reading.Mods.Any(m => rule.Names(m.Mod, !paired.Contains(rule)))))
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

    // Whether the row names a mod: its own text, and with withTwin the other wording of a twin
    // pair too (TabletMod.Twin; on an Overseer Tablet the suffix at 1 reads like the prefix).
    public bool Names(TabletMod mod, bool withTwin = true) => mod.Text == Text || withTwin && mod.Twin?.Text == Text;
}

public static class TabletGroups
{
    // The rows whose twin stands in the same group as a row of its own: an Overseer Tablet can
    // carry the prefix "Map contains an additional Essence" and the suffix "Map contains #
    // additional Essences" at once (OFFEN Z2, 23.10.2026). Such a row stands for its own
    // wording alone, in the judgement at the tooltip, the search text and the trade query, so
    // that a group asking for both is met only by a tablet with both. Elsewhere a row takes
    // the twin too.
    // The set holds the rows themselves (reference equality): a row of the same wording and
    // minimum in another group is another row, and there the twin counts on (R6).
    public static HashSet<TabletModRule> Paired(this IEnumerable<ModGroup<TabletModRule>> groups)
    {
        var paired = new HashSet<TabletModRule>(ReferenceEqualityComparer.Instance);
        foreach (var group in groups)
            foreach (var rule in group.Rules)
                if (rule.Mod?.Twin is { } twin && group.Rules.Any(other => other.Text == twin.Text))
                    paired.Add(rule);
        return paired;
    }
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

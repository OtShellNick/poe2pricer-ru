using Poe2Pricer.Core.Market;

namespace Poe2Pricer.Core.Waystones;

// What the player asks of a waystone: tier, the summary values
// at least this high, corrupted or not, mods wanted (with a value at least this high) and
// mods not wanted. The same profile yields the search text for the game's stash search
// (SearchRegex), the trade site's query (profile price) and, at a tooltip read from the
// screen, the vocabulary of the live question (TradeQuery.ForReading): among the saved
// profiles the one the waystone has most in common with is taken (Choose). Mods are named by
// their text, the key of WaystoneMods.
public sealed record WaystoneProfile
{
    public string Name { get; init; } = "default";
    public int? TierMin { get; init; }
    public int? TierMax { get; init; }
    public int? RevivesMin { get; init; }
    public int? RevivesMax { get; init; }
    public int? ItemRarity { get; init; }
    public int? PackSize { get; init; }
    public int? MonsterRarity { get; init; }
    public int? Effectiveness { get; init; }
    public int? DropChance { get; init; }
    public CorruptedChoice Corrupted { get; init; } = CorruptedChoice.Any;
    // The mod rows in groups (ModGroup: and, or, not), joined by "and".
    public ModGroup<WaystoneModRule>[] Groups { get; init; } = [];

    public static readonly WaystoneProfile Empty = new();

    // Rows a group of the Waystones tab takes at most: a waystone carries eight mods.
    public const int MaxRules = 8;

    // Value equality over the lists too (a record compares arrays by reference): settings
    // saved and loaded again are the same settings.
    public bool Equals(WaystoneProfile? other) =>
        other is not null && (Name, TierMin, TierMax, RevivesMin, RevivesMax, ItemRarity, PackSize, MonsterRarity, Effectiveness, DropChance, Corrupted)
            == (other.Name, other.TierMin, other.TierMax, other.RevivesMin, other.RevivesMax, other.ItemRarity, other.PackSize, other.MonsterRarity, other.Effectiveness, other.DropChance, other.Corrupted)
        && Groups.SequenceEqual(other.Groups);

    public override int GetHashCode() => HashCode.Combine(Name, TierMin, TierMax, Effectiveness, MonsterRarity, Groups.Length);

    // The profile's conditions, each as met or not by a waystone read from its tooltip: tier
    // and revives exactly; a summary minimum met when the value is not more than the tolerance
    // (per cent) under it (a line the tooltip does not show stands for 0, a value the OCR
    // could not read never meets a minimum); corrupted as asked; a wanted mod met when the
    // waystone has it with at least the minimum less the tolerance; an "or" group one condition,
    // met by any of its rows; a mod of a "not" group met when the waystone lacks it. Methods,
    // not properties: a property would be saved.
    public IEnumerable<bool> Conditions(WaystoneReading reading, int tolerance)
    {
        if (TierMin is { } tierMin)
            yield return reading.Tier >= tierMin;
        if (TierMax is { } tierMax)
            yield return reading.Tier <= tierMax;
        if (RevivesMin is { } revivesMin)
            yield return (reading.Revives ?? 0) >= revivesMin;
        if (RevivesMax is { } revivesMax)
            yield return (reading.Revives ?? 0) <= revivesMax;
        foreach (var (minimum, value) in new[] { (ItemRarity, reading.ItemRarity), (PackSize, reading.PackSize), (MonsterRarity, reading.MonsterRarity), (Effectiveness, reading.Effectiveness), (DropChance, reading.DropChance) })
            if (minimum is { } m)
                yield return Tolerance.Reaches(value ?? 0, m, tolerance);
        if (Corrupted != CorruptedChoice.Any)
            yield return Corrupted == CorruptedChoice.Yes == reading.Corrupted;
        bool Has(WaystoneModRule rule) => reading.Mods.Any(m => m.Mod.Text == rule.Text && (rule.Min is not { } min || m.Value is { } v && Tolerance.Reaches(Math.Abs(v), min, tolerance)));
        foreach (var met in Groups.Conditions(Has, rule => reading.Mods.Any(m => m.Mod.Text == rule.Text)))
            yield return met;
    }

    // Every mod row of the groups of these kinds.
    public IEnumerable<WaystoneModRule> RulesOf(params GroupKind[] kinds) => Groups.RulesOf(kinds);

    public int ConditionCount() => Conditions(new("", 0, null, null, null, null, null, null, [], false, []), 0).Count();

    public int Satisfied(WaystoneReading reading, int tolerance) => Conditions(reading, tolerance).Count(met => met);

    // Whether the waystone meets every condition.
    public bool Matches(WaystoneReading reading, int tolerance = 0) => Conditions(reading, tolerance).All(met => met);

    // The profile a waystone's live question is asked in: of the saved profiles with any
    // condition, the one with the most conditions the waystone meets; on a tie the one in use
    // in the tab, else the first saved. null: no profile with a condition is saved; the
    // question is then the waystone itself (TradeQuery.ForReading).
    public static WaystoneProfile? Choose(WaystoneSettings settings, WaystoneReading reading)
    {
        WaystoneProfile? best = null;
        int bestScore = -1;
        foreach (var profile in settings.Profiles)
        {
            if (profile.ConditionCount() == 0)
                continue;
            int score = profile.Satisfied(reading, settings.Tolerance);
            if (score > bestScore || score == bestScore && profile.Name == settings.Active && best?.Name != settings.Active)
                (best, bestScore) = (profile, score);
        }
        return best;
    }
}

public enum CorruptedChoice { Any, Yes, No }

// A mod on a profile's list: by its text (WaystoneMod.Text); Min: the value at least this
// high, for a wanted mod with a number.
public sealed record WaystoneModRule(string Text, int? Min = null)
{
    // Looked up, not stored: settings.json keeps the text and the minimum only.
    [System.Text.Json.Serialization.JsonIgnore]
    public WaystoneMod? Mod => WaystoneMods.All.FirstOrDefault(m => m.Text == Text);
}

// The Waystones tab's settings: the profiles, which one is in use, and the tolerance
// (Tolerance, per cent; Market.Tolerance). The live price is switched in Settings.LiveTradePrice.
public sealed record WaystoneSettings
{
    public WaystoneProfile[] Profiles { get; init; } = [];
    public string? Active { get; init; }
    public int Tolerance { get; init; } = Market.Tolerance.Default;

    [System.Text.Json.Serialization.JsonIgnore]
    public WaystoneProfile ActiveProfile => Profiles.FirstOrDefault(p => p.Name == Active) ?? Profiles.FirstOrDefault() ?? WaystoneProfile.Empty;

    public bool Equals(WaystoneSettings? other) =>
        other is not null && (Active, Tolerance) == (other.Active, other.Tolerance) && Profiles.SequenceEqual(other.Profiles);

    public override int GetHashCode() => HashCode.Combine(Active, Tolerance, Profiles.Length);
}

namespace Poe2Pricer.Core.Market;

// How a group of mod rows counts, as the trade site's stat groups: And every row required,
// Or at least one row, Not none of the rows. The groups of a profile are joined by "and".
public enum GroupKind { And, Or, Not }

// One group of a profile's mod rows (WaystoneModRule, TabletModRule).
public sealed record ModGroup<TRule>(GroupKind Kind, TRule[] Rules)
{
    // Value equality over the rows (a record compares arrays by reference).
    public bool Equals(ModGroup<TRule>? other) => other is not null && Kind == other.Kind && Rules.SequenceEqual(other.Rules);

    public override int GetHashCode() => HashCode.Combine(Kind, Rules.Length);
}

// What waystone and tablet profiles do alike with their groups of mod rows.
public static class ModGroups
{
    // Every row of the groups of these kinds.
    public static IEnumerable<TRule> RulesOf<TRule>(this IEnumerable<ModGroup<TRule>> groups, params GroupKind[] kinds) =>
        groups.Where(g => kinds.Contains(g.Kind)).SelectMany(g => g.Rules);

    // The groups' conditions, each met or not by an item: an "and" row each, an "or" group one
    // condition met by any of its rows, a "not" row each, met when the item lacks the mod.
    // has: the item carries the row's mod with at least its minimum; carries: the mod at all.
    public static IEnumerable<bool> Conditions<TRule>(this IEnumerable<ModGroup<TRule>> groups, Func<TRule, bool> has, Func<TRule, bool> carries)
    {
        foreach (var group in groups)
            switch (group.Kind)
            {
                case GroupKind.And:
                    foreach (var rule in group.Rules)
                        yield return has(rule);
                    break;
                case GroupKind.Or when group.Rules.Length > 0:
                    yield return group.Rules.Any(has);
                    break;
                case GroupKind.Not:
                    foreach (var rule in group.Rules)
                        yield return !carries(rule);
                    break;
            }
    }

    // The groups as terms of the game's stash search: an "and" row a term each, an "or" group
    // one term of alternatives, a "not" group one negated term of alternatives (the search:
    // every term must hold, "|" either, "!" none of them). pattern(rule, withMinimum): the row's
    // pattern, null for a row whose mod is unknown; "not" rows go without their minimum.
    public static IEnumerable<string> SearchTerms<TRule>(this IEnumerable<ModGroup<TRule>> groups, Func<TRule, bool, string?> pattern)
    {
        foreach (var group in groups)
        {
            bool withMinimum = group.Kind != GroupKind.Not;
            var patterns = group.Rules.Select(r => pattern(r, withMinimum)).Where(p => p is not null).Select(p => p!).ToArray();
            if (patterns.Length == 0)
                continue;
            switch (group.Kind)
            {
                case GroupKind.And:
                    foreach (var p in patterns)
                        yield return Waystones.SearchRegex.Quote(p);
                    break;
                case GroupKind.Or:
                    yield return Waystones.SearchRegex.Quote(string.Join("|", patterns));
                    break;
                case GroupKind.Not:
                    yield return "\"!" + string.Join("|", patterns) + "\"";
                    break;
            }
        }
    }
}

using System.Text.Json.Nodes;
using Poe2Pricer.Core.Market;
using Poe2Pricer.Core.Waystones;

namespace Poe2Pricer.Core.Tablets;

// The trade site's search for tablets: the kind as the base type, category map.tablet, no
// uniques (they would be the cheapest listings), uses left as the site's pseudo stat, mods as
// stat filters as for waystones (TradeEnvelope.Build). A line the site knows under two ids
// (TabletMod.Twin) is asked for as either.
public static class TabletTradeQuery
{
    // "# uses remaining (Tablets)" on the trade site, a minimum.
    public const string UsesStat = "pseudo.pseudo_number_of_uses_remaining";

    // The live question for the tablet read: its own values in the vocabulary of the profile
    // (TradeQuery.ForReading says it for waystones): its uses where the profile asks for uses;
    // of the profile's wanted mods those the tablet has, with the tablet's value lowered by
    // the tolerance; of the mods not wanted those the tablet lacks, excluded. Without a
    // profile (null): the tablet itself, its uses and every mod it has.
    public static JsonObject ForReading(TabletReading reading, TabletProfile? profile, int tolerance)
    {
        var wanted = new List<(TabletMod Mod, int? Min)>();
        var unwanted = new List<TabletMod>();
        if (profile is null)
        {
            foreach (var m in reading.Mods)
                wanted.Add((m.Mod, Lowered(m, null, tolerance)));
        }
        else
        {
            // The "and" and the "or" rows alike: those the tablet has, each required.
            foreach (var rule in profile.RulesOf(GroupKind.And, GroupKind.Or))
                if (reading.Mods.FirstOrDefault(m => rule.Names(m.Mod)) is { } own)
                    wanted.Add((own.Mod, Lowered(own, rule.Min, tolerance)));
            foreach (var rule in profile.RulesOf(GroupKind.Not))
                if (rule.Mod is { } mod && !reading.Mods.Any(m => rule.Names(m.Mod)))
                    unwanted.Add(mod);
            // Nothing of the profile is left (no uses asked or read, no mod it names there):
            // that question finds the cheapest tablet of the kind. Asked for as itself instead.
            bool uses = profile.UsesMin is not null && reading.Uses is > 0;
            if (!uses && wanted.Count == 0 && unwanted.Count == 0)
                return ForReading(reading, null, tolerance);
        }
        return Build(reading.Kind, profile is null || profile.UsesMin is not null ? reading.Uses : null, [(GroupKind.And, wanted), (GroupKind.Not, unwanted.Select(m => (m, (int?)null)).ToList())]);
    }

    private static int? Lowered(TabletModReading own, int? fallback, int tolerance) =>
        own.Value is { } v ? Tolerance.Lower(Math.Abs(v), tolerance) : fallback;

    // Tablets as the profile asks for them: the profile price. null: the profile names no kind.
    public static JsonObject? ForProfile(TabletProfile profile) =>
        profile.KindOf() is { } kind
            ? Build(kind, profile.UsesMin, profile.Groups.Select(g => (g.Kind, Rules(g.Kind == GroupKind.Not ? g.Rules.Select(r => r with { Min = null }).ToArray() : g.Rules))).ToList())
            : null;

    private static List<(TabletMod Mod, int? Min)> Rules(TabletModRule[] rules) => rules.Where(r => r.Mod is not null).Select(r => (r.Mod!, r.Min)).ToList();

    // groups: the profile's groups as lines with their minimum. The uses left are always
    // required. A line with a twin the kind rolls (TabletMod.Twin): in an "and" group a group of
    // its own with either id, in an "or" or "not" group both ids in that group.
    private static JsonObject Build(TabletKind kind, int? usesMin, List<(GroupKind Kind, List<(TabletMod Mod, int? Min)> Lines)> groups)
    {
        var filters = new JsonObject
        {
            ["type_filters"] = new JsonObject { ["filters"] = new JsonObject { ["category"] = new JsonObject { ["option"] = "map.tablet" }, ["rarity"] = new JsonObject { ["option"] = "nonunique" } } },
        };
        var either = new List<StatFilter[]>();
        var stats = new List<(GroupKind, StatFilter?[])>();
        if (usesMin is { } uses && uses > 0)
            stats.Add((GroupKind.And, [new StatFilter(UsesStat, false, true, uses)]));
        foreach (var (groupKind, lines) in groups)
        {
            var group = new List<StatFilter?>();
            foreach (var (mod, min) in lines)
            {
                var twin = Twin(mod, kind) is { } t ? Stat(t, min) : null;
                if (groupKind == GroupKind.And && twin is not null && Stat(mod, min) is { } own)
                    either.Add([own, twin]);
                else
                {
                    group.Add(Stat(mod, min));
                    group.Add(twin);
                }
            }
            stats.Add((groupKind, group.ToArray()));
        }
        return TradeEnvelope.Build(filters, kind.Name, stats, either);
    }

    // The other wording of a line, where this kind rolls it (only the Overseer Tablet rolls
    // both; elsewhere the one id does).
    private static TabletMod? Twin(TabletMod mod, TabletKind kind) =>
        mod.Twin is { } twin && (twin.Tablet is null || twin.Tablet == kind.Name) ? twin : null;

    private static StatFilter? Stat(TabletMod mod, int? min) =>
        mod.Trade is { } id ? new StatFilter(id, mod.TradeNegated, mod.HasValue, min) : null;
}

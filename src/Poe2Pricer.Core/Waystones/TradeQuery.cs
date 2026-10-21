using System.Text.Json.Nodes;
using Poe2Pricer.Core.Market;

namespace Poe2Pricer.Core.Waystones;

// The trade site's search for waystones, as JSON for /api/trade2/search: by the summary
// values the site indexes as map filters (tier, pack size, monster effectiveness and rarity,
// item rarity, drop chance, revives), corrupted or not, and mods as stat filters — wanted ones
// in an "and" (or a "count" of at least one) group, unwanted ones in a "not" group.
// Instant Buyout, cheapest first (the client sets the status: TradeClient.InPerson).
// Tolerance: Market.Tolerance, per cent.
public static class TradeQuery
{
    // The live question for the waystone read: its own values, in the vocabulary of the
    // profile. Its tier; the summary values the profile sets a minimum for, each with the
    // item's value lowered by the tolerance (Market.Tolerance; a value the tooltip does not
    // show is 0 and sets no minimum); its revives where the profile asks for revives; corrupted
    // as the item is; of the profile's wanted mods those the item has, with the item's value
    // lowered by the tolerance (the profile's minimum where the OCR lost the value); of the
    // mods not wanted those the item lacks, excluded. A wanted mod the item lacks and a mod
    // not wanted the item has are left out: the question describes this waystone, not a
    // better one. Without a profile (null) the question is the waystone itself: every summary
    // value it shows and every mod it has.
    public static JsonObject ForReading(WaystoneReading reading, WaystoneProfile? profile, int tolerance)
    {
        var map = new JsonObject
        {
            ["map_tier"] = Range(reading.Tier, reading.Tier),
        };
        if (profile is null || profile.RevivesMin is not null || profile.RevivesMax is not null)
            Minimum(map, "map_revives", reading.Revives, 0);
        foreach (var (key, asked, value) in new[] { ("map_iir", profile?.ItemRarity, reading.ItemRarity), ("map_packsize", profile?.PackSize, reading.PackSize), ("map_rare_monsters", profile?.MonsterRarity, reading.MonsterRarity), ("map_magic_monsters", profile?.Effectiveness, reading.Effectiveness), ("map_bonus", profile?.DropChance, reading.DropChance) })
            if (profile is null || asked is not null)
                Minimum(map, key, value, tolerance);
        var wanted = new List<StatFilter?>();
        var unwanted = new List<StatFilter?>();
        if (profile is null)
        {
            foreach (var m in reading.Mods)
                wanted.Add(Own(m, null, tolerance));
        }
        else
        {
            // The "and" and the "or" rows alike: those the item has, each required.
            foreach (var rule in profile.RulesOf(GroupKind.And, GroupKind.Or))
                if (reading.Mods.FirstOrDefault(m => m.Mod.Text == rule.Text) is { } own)
                    wanted.Add(Own(own, rule.Min, tolerance));
            foreach (var rule in profile.RulesOf(GroupKind.Not))
                if (!reading.Mods.Any(m => m.Mod.Text == rule.Text))
                    unwanted.Add(Stat(rule with { Min = null }));
            // Nothing of the profile is left but the tier (no value it names shows, no mod it
            // names is there): such a question finds the cheapest waystone of the tier. Asked
            // for as itself instead.
            if (map.Count == 1 && !wanted.Any(f => f is not null) && !unwanted.Any(f => f is not null))
                return ForReading(reading, null, tolerance);
        }
        return TradeEnvelope.Build(Filters(map, reading.Corrupted ? CorruptedChoice.Yes : CorruptedChoice.No), null, [(GroupKind.And, wanted.ToArray()), (GroupKind.Not, unwanted.ToArray())], []);
    }

    // The item's own line as a stat filter: at least its value lowered by the tolerance;
    // fallback: the profile's minimum where the OCR lost the value.
    private static StatFilter? Own(WaystoneModReading own, int? fallback, int tolerance) =>
        own.Mod.Trade is { } id ? new StatFilter(id, own.Mod.TradeNegated, own.Mod.HasValue, own.Value is { } v ? Market.Tolerance.Lower(Math.Abs(v), tolerance) : fallback) : null;

    // Waystones as the profile asks for them: the profile price.
    public static JsonObject ForProfile(WaystoneProfile profile)
    {
        var map = new JsonObject();
        if (profile.TierMin is not null || profile.TierMax is not null)
            map["map_tier"] = Range(profile.TierMin, profile.TierMax);
        if (profile.RevivesMin is not null || profile.RevivesMax is not null)
            map["map_revives"] = Range(profile.RevivesMin, profile.RevivesMax);
        Minimum(map, "map_iir", profile.ItemRarity, 0);
        Minimum(map, "map_packsize", profile.PackSize, 0);
        Minimum(map, "map_rare_monsters", profile.MonsterRarity, 0);
        Minimum(map, "map_magic_monsters", profile.Effectiveness, 0);
        Minimum(map, "map_bonus", profile.DropChance, 0);
        return Build(map, profile.Corrupted, profile.Groups);
    }

    private static JsonObject Build(JsonObject map, CorruptedChoice corrupted, ModGroup<WaystoneModRule>[] groups) =>
        TradeEnvelope.Build(Filters(map, corrupted), null, groups.Select(g => (g.Kind, g.Rules.Select(r => Stat(g.Kind == GroupKind.Not ? r with { Min = null } : r)).ToArray())), []);

    private static JsonObject Filters(JsonObject map, CorruptedChoice corrupted)
    {
        var filters = new JsonObject
        {
            ["type_filters"] = new JsonObject { ["filters"] = new JsonObject { ["category"] = new JsonObject { ["option"] = "map.waystone" } } },
        };
        if (map.Count > 0)
            filters["map_filters"] = new JsonObject { ["filters"] = map };
        if (corrupted != CorruptedChoice.Any)
            filters["misc_filters"] = new JsonObject { ["filters"] = new JsonObject { ["corrupted"] = new JsonObject { ["option"] = corrupted == CorruptedChoice.Yes ? "true" : "false" } } };
        return filters;
    }

    // A mod as a stat filter: by its trade id; a minimum on a line the site knows with the
    // opposite sign ("less" as "more") becomes a maximum of the negated value.
    private static StatFilter? Stat(WaystoneModRule rule) =>
        rule.Mod is { Trade: { } id } mod ? new StatFilter(id, mod.TradeNegated, mod.HasValue, rule.Min) : null;

    private static void Minimum(JsonObject map, string key, int? value, int tolerance)
    {
        if (value is not { } v || v <= 0)
            return;
        map[key] = new JsonObject { ["min"] = Market.Tolerance.Lower(v, tolerance) };
    }

    private static JsonObject Range(int? min, int? max)
    {
        var range = new JsonObject();
        if (min is not null)
            range["min"] = min;
        if (max is not null)
            range["max"] = max;
        return range;
    }
}

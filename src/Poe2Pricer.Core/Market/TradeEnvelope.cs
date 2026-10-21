using System.Text.Json.Nodes;

namespace Poe2Pricer.Core.Market;

// What the waystone and the tablet questions to the trade site share: the stat groups around
// the item filters (Build) and the one-line description of a question for the log (Describe).
public static class TradeEnvelope
{
    // The query around the filters, for waystones and tablets alike: a stat group for each group of
    // the profile, "and" as "and", "or" as a "count" of at least one, "not" as "not" (empty
    // groups left out), and a "count" group of its own for each stat the site knows under two
    // ids (either one). type: the item's base type (a tablet's kind), null for any. Sellers
    // online, cheapest first.
    public static JsonObject Build(JsonObject filters, string? type, IEnumerable<(GroupKind Kind, StatFilter?[] Filters)> groups, IEnumerable<StatFilter[]> either)
    {
        var stats = new JsonArray();
        // All "and" groups as one: the same question, shorter.
        var list = groups.ToList();
        var allAnd = list.Where(g => g.Kind == GroupKind.And).SelectMany(g => g.Filters).ToArray();
        list = [.. list.Where(g => g.Kind != GroupKind.And)];
        if (allAnd.Length > 0)
            list.Insert(0, (GroupKind.And, allAnd));
        foreach (var (kind, group) in list)
        {
            var json = group.Where(s => s is not null).Select(s => (JsonNode)s!.Json()).ToArray();
            if (json.Length == 0)
                continue;
            var entry = new JsonObject { ["type"] = kind switch { GroupKind.Or => "count", GroupKind.Not => "not", _ => "and" } };
            if (kind == GroupKind.Or)
                entry["value"] = new JsonObject { ["min"] = 1 };
            entry["filters"] = new JsonArray(json);
            stats.Add(entry);
        }
        foreach (var pair in either)
            stats.Add(new JsonObject { ["type"] = "count", ["value"] = new JsonObject { ["min"] = 1 }, ["filters"] = new JsonArray(pair.Select(s => (JsonNode)s.Json()).ToArray()) });
        var query = new JsonObject
        {
            ["status"] = new JsonObject { ["option"] = TradeClient.InstantBuyout },
        };
        if (type is not null)
            query["type"] = type;
        query["filters"] = filters;
        if (stats.Count > 0)
            query["stats"] = stats;
        return new JsonObject { ["query"] = query, ["sort"] = new JsonObject { ["price"] = "asc" } };
    }

    // One line for the log and the status: what was asked.
    public static string Describe(JsonObject query)
    {
        var parts = new List<string>();
        if (query["query"]?["status"]?["option"]?.GetValue<string>() is { } status && status != TradeClient.InstantBuyout)
            parts.Add("sellers:" + status);
        if (query["query"]?["type"]?.GetValue<string>() is { } type)
            parts.Add(type);
        if (query["query"]?["filters"]?["type_filters"]?["filters"]?["rarity"]?["option"]?.GetValue<string>() is { } rarity)
            parts.Add("rarity:" + rarity);
        if (query["query"]?["filters"]?["map_filters"]?["filters"] is JsonObject map)
            foreach (var (key, value) in map)
                parts.Add(key.Replace("map_", "") + ((value?["min"] is { } min ? "≥" + min : "") + (value?["max"] is { } max ? "≤" + max : "")));
        if (query["query"]?["filters"]?["misc_filters"]?["filters"]?["corrupted"]?["option"] is { } corrupted)
            parts.Add("corrupted=" + corrupted);
        if (query["query"]?["stats"] is JsonArray stats)
            foreach (var group in stats)
                parts.Add(group!["type"] + ":" + group["filters"]!.AsArray().Count);
        return string.Join(" ", parts);
    }
}

// One stat filter of a trade query: the site's id, whether the site knows the line with the
// opposite sign, whether the line has a number, and the minimum asked for.
public sealed record StatFilter(string Id, bool Negated, bool HasValue, int? Min)
{
    public JsonObject Json()
    {
        var filter = new JsonObject { ["id"] = Id };
        if (Min is { } min && HasValue)
            filter["value"] = Negated ? new JsonObject { ["max"] = -min } : new JsonObject { ["min"] = min };
        return filter;
    }
}

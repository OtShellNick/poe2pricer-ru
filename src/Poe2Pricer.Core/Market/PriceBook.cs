using System.Globalization;
using System.Text.Json.Nodes;

namespace Poe2Pricer.Core.Market;

// Listings: number of offers behind a unique price; null for exchange items, whose volume
// is traded value, not a count of offers.
public record Price(decimal Divine, decimal ExaltedPerDivine, int? Listings, string Category)
{
    public decimal Exalted => Divine * ExaltedPerDivine;

    // Fewer than five offers: shown with "~".
    public bool Thin => Listings < Tuning.Labels.ThinBelow;

    // From 1 divine on in div with two decimals, below in ex with at most one; always ".".
    public string Text =>
        (Thin ? "~" : "")
        + (Divine >= Tuning.Labels.DivineFrom
            ? Divine.ToString("0.00", CultureInfo.InvariantCulture) + " div"
            : Exalted.ToString("0.#", CultureInfo.InvariantCulture) + " ex");
}

public sealed class PriceBook(string league)
{
    private readonly Dictionary<string, Price> byName = new(StringComparer.OrdinalIgnoreCase);

    public string League { get; } = league;
    // One exchange rate for everything: the Currency category's, poe.ninja's actual trade
    // rate. Each response carries its own rate, and slower categories lag behind by several
    // percent; mixing them would make "not below 1 ex" drop an Exalted Orb.
    public decimal ExaltedPerDivine { get; private set; }
    public decimal ChaosPerDivine { get; private set; }
    private bool currencyRate;
    public int Count => byName.Count;

    public Price? Find(string nameEn) =>
        byName.GetValueOrDefault(nameEn) is { } p ? p with { ExaltedPerDivine = ExaltedPerDivine } : null;

    public IEnumerable<string> Names => byName.Keys;

    // Item art per exchange item (poe.ninja path below web.poecdn.com) and its category, for
    // the symbol comparison in stash tabs. Also items without a price line have one.
    // The names with a price as the client of a language shows them, each with its English
    // name; built once per language when first asked for.
    private readonly Dictionary<string, (string Shown, string English)[]> shownIn = [];

    private (string Shown, string English)[] NamesIn(string language)
    {
        lock (shownIn)
        {
            if (shownIn.TryGetValue(language, out var names))
                return names;
            names = language == GameLanguage.English
                ? byName.Keys.Select(n => (n, n)).ToArray()
                : byName.Keys.Select(n => (Shown: GameNames.In(language, n), English: n)).GroupBy(n => n.Shown).Where(g => g.Count() == 1).Select(g => g.Single()).ToArray();
            return shownIn[language] = names;
        }
    }

    private readonly Dictionary<string, (string Path, string Category)> images = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, (string Path, string Category)> Images => images;

    // Name read by OCR: exact after folding quotes, else the closest name within 1 edit per
    // 8 letters (OCR of the reward list is clean; a count "1x" read as "lx" at most).
    // A number in a name is not a slip of the reading: a name whose numbers differ is another
    // item, so a level poe.ninja does not list must not take the price of a neighbouring one
    // ("Thaumaturgic Flux (Level 12)" that of Level 13). A name without numbers still forgives
    // a digit read for a letter ("0rb").
    //
    // language: the language the client runs in. The name was read in that language and is
    // compared with what the game calls the priced items there (GameNames); what comes back is
    // the English name the price stands under. Compared are the names with a price only, in
    // whatever language — about a thousand, never the whole table of the game's names. A name
    // that two priced items share in that language names neither.
    public (string Name, Price Price)? Match(string text, string language = GameLanguage.English)
    {
        static string Digits(string s) => new(s.Where(char.IsDigit).ToArray());
        string Fold(string s) => new(s.Replace('’', '\'').ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == '\'').ToArray());
        var key = Fold(text);
        if (key.Length < Tuning.Labels.MinimumKeyLength)
            return null;
        string? best = null;
        int bestDistance = int.MaxValue;
        foreach (var (shown, name) in NamesIn(language))
        {
            var other = Fold(shown);
            if (Math.Abs(other.Length - key.Length) > Math.Max(1, key.Length / Tuning.Labels.EditsPerLetters))
                continue;
            if (Digits(other) is { Length: > 0 } numbers && numbers != Digits(key))
                continue;
            int d = TitleReader.Distance(key, other);
            if (d < bestDistance)
                (best, bestDistance) = (name, d);
        }
        return best is not null && bestDistance <= Math.Max(1, key.Length / Tuning.Labels.EditsPerLetters) ? (best, Find(best)!) : null;
    }

    // Prices are given in the response's primary currency (divine at present); rates[x] is
    // the amount of x worth one primary unit.
    private (decimal ToDivine, decimal ExPerDiv) Rates(JsonNode json)
    {
        decimal ex = Rate(json, "exalted"),
            div = Rate(json, "divine");
        if (ex <= 0 || div <= 0)
            throw Texts.Failure(m => new InvalidDataException(m), "NinjaRateMissing");
        return (div, ex / div);
    }

    // The amount of a currency worth one unit of the response's primary currency; 0: not given.
    private static decimal Rate(JsonNode json, string id)
    {
        var core = json["core"]!;
        return id == core["primary"]!.GetValue<string>() ? 1 : core["rates"]?[id]?.GetValue<decimal>() ?? 0;
    }

    private void UseRate(JsonNode json, decimal exPerDiv, bool fromCurrency)
    {
        if (currencyRate || ExaltedPerDivine > 0 && !fromCurrency)
            return;
        currencyRate = fromCurrency;
        ExaltedPerDivine = exPerDiv;
        if (Rate(json, "chaos") is > 0 and var chaos && Rate(json, "divine") is > 0 and var div)
            ChaosPerDivine = chaos / div;
    }

    // poe.ninja answers a category it has nothing for with an empty list — for uniques without
    // "core", for the exchange with "core" but without rates (in Standard most unique
    // categories, in Hardcore every category). That is an answer, not a failure: the category
    // has no prices, and a missing "core" must not break the whole load.
    private static bool Empty(JsonNode json) => json["lines"] is not JsonArray { Count: > 0 };

    public void AddExchange(JsonNode json, string category)
    {
        if (Empty(json))
            return;
        var (toDivine, exPerDiv) = Rates(json);
        UseRate(json, exPerDiv, category == "Currency");
        var names = json["items"]!.AsArray().ToDictionary(i => i!["id"]!.GetValue<string>(), i => i!["name"]!.GetValue<string>());
        foreach (var item in json["items"]!.AsArray())
            if (item!["image"]?.GetValue<string>() is { } image)
                images.TryAdd(item["name"]!.GetValue<string>(), (image, category));
        foreach (var line in json["lines"]!.AsArray())
        {
            var id = line!["id"]!.GetValue<string>();
            if (names.TryGetValue(id, out var name) && line["primaryValue"] is { } value)
                byName.TryAdd(name, new(value.GetValue<decimal>() * toDivine, exPerDiv, null, category));
        }
    }

    // Runemastered and Runeforged variants share name and art with the plain unique but do
    // not come from rituals; corrupted ones neither. Of the remaining
    // variants the one with the most offers counts.
    public void AddUniques(JsonNode json, string category)
    {
        if (Empty(json))
            return;
        var (toDivine, exPerDiv) = Rates(json);
        UseRate(json, exPerDiv, category == "Currency");
        var plain = json["lines"]!
            .AsArray()
            .Select(l => l!)
            .Where(l =>
                l["corrupted"]?.GetValue<bool>() != true
                && l["baseType"]?.GetValue<string>() is { } b
                && !b.StartsWith("Runemastered ")
                && !b.StartsWith("Runeforged ")
            )
            .GroupBy(l => l["name"]!.GetValue<string>());
        foreach (var g in plain)
        {
            var best = g.MaxBy(l => l["listingCount"]?.GetValue<int>() ?? 0)!;
            byName.TryAdd(
                g.Key,
                new(best["primaryValue"]!.GetValue<decimal>() * toDivine, exPerDiv, best["listingCount"]?.GetValue<int>(), category)
            );
        }
    }
}

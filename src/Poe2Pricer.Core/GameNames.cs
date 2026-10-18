using System.Text.Json;

namespace Poe2Pricer.Core;

// What the game calls its items in another language than English: the name there for every
// English name (base items and uniques), from the game's own tables — built into the program
// like the rumour texts, one file per language (data/names/<language>.json; `poe2pricer
// game-names` compares it with the tables as published now and writes it anew). poe.ninja and
// the recognition catalog know items by their English names only; a name read off a German
// client is found through this.
// Only the file of the language the client runs in is read, once, when first asked for; and a
// name is never searched for in the whole table: PriceBook and TitleReader look up the names of
// the few items in question (those with a price, the omens of one group).
public static class GameNames
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Loaded = [];

    // English name → name in this language. English itself, and a language without a file: empty.
    public static IReadOnlyDictionary<string, string> Of(string language)
    {
        lock (Gate)
        {
            if (Loaded.TryGetValue(language, out var names))
                return names;
            using var stream = typeof(GameNames).Assembly.GetManifestResourceStream($"Names.{language}.json");
            names = stream is null ? new Dictionary<string, string>() : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
            return Loaded[language] = names;
        }
    }

    // The item's name as the client of this language shows it; without an entry, the English one
    // (many names are the same in both).
    public static string In(string language, string english) =>
        language == GameLanguage.English ? english : Of(language).GetValueOrDefault(english, english);
}

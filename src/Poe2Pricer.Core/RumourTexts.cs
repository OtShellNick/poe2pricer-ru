namespace Poe2Pricer.Core;

// A rumour as the game writes it on the panel ("Somethin' fishy..."), and the island it hints at.
public sealed record RumourText(string Text, string Island);

// The rumour texts of the game, built into the program: nothing is fetched at run time (a
// download is one more thing that can be tampered with or fail). Taken from the game's table
// of endgame maps, which holds one "expedition clue" per island. When the game adds a rumour,
// its line is still named by the OCR and its picture
// remembered (RumourShapes); `poe2pricer rumour-texts` compares this list with the game's
// tables as published now.
public static class RumourTexts
{
    public static readonly IReadOnlyList<RumourText> All =
    [
        new("All that glitters...", "Castaway"),
        new("Almost paradise.", "Untainted Paradise"),
        new("A good fellow...", "Moment of Zen"),
        new("Reflective waters...", "The Fractured Lake"),
        new("Crazed Chieftain...", "The Jade Isles"),
        new("Somethin' fishy...", "Barren Atoll"),
        new("End of the circle...", "Sprawling Jungle"),
        new("The last to fall...", "Mournful Cliffside"),
        new("Stardrinker...", "Secluded Temple"),
        new("Origin of the fall...", "Obscure Island"),
        new("Nothin' to drink...", "Stagnant Basin"),
        new("Unknown ruins...", "Exhumed Ruins"),
        new("It's dry at least...", "Sloughed Gully"),
        new("Fallen stars...", "Moor of Fallen Skies"),
        new("Endless cliffs...", "Craggy Peninsula"),
        new("Warm but risky...", "Grazed Prairie"),
        new("Bleak and awful...", "Bleached Shoals"),
        new("Wild roaming free...", "Lush Isle"),
        new("Cold as ice...", "Frigid Bluffs"),
        new("Sulphite!", "Scorched Cay"),
    ];

    // The same texts as the German client writes them (the same table, the column of the
    // language), by the English text: the rumour table knows a rumour by that.
    public static readonly IReadOnlyDictionary<string, string> German = new Dictionary<string, string>
    {
        ["All that glitters..."] = "Alles, was glänzt ...",
        ["Almost paradise."] = "Fast wie im Paradies.",
        ["A good fellow..."] = "Ein guter Kerl ...",
        ["Reflective waters..."] = "Spiegelnde Gewässer ...",
        ["Crazed Chieftain..."] = "Verrückter Häuptling ...",
        ["Somethin' fishy..."] = "Da ist was faul ...",
        ["End of the circle..."] = "Das Ende des Kreises ...",
        ["The last to fall..."] = "Die Letzte, die gefallen ist ...",
        ["Stardrinker..."] = "Sternentrinker ...",
        ["Origin of the fall..."] = "Der Ursprung des Untergangs ...",
        ["Nothin' to drink..."] = "Nichts zu trinken ...",
        ["Unknown ruins..."] = "Unbekannte Ruinen ...",
        ["It's dry at least..."] = "Wenigstens ist es trocken ...",
        ["Fallen stars..."] = "Gefallene Sterne ...",
        ["Endless cliffs..."] = "Endlose Klippen ...",
        ["Warm but risky..."] = "Warm, aber riskant ...",
        ["Bleak and awful..."] = "Düster und schrecklich ...",
        ["Wild roaming free..."] = "Wild und frei ...",
        ["Cold as ice..."] = "So kalt wie Eis ...",
        ["Sulphite!"] = "Sulfit!",
    };

    // The islands as the German client names them (same data: the area each rumour's map
    // stands for), by the English text of the rumour.
    public static readonly IReadOnlyDictionary<string, string> GermanIslands = new Dictionary<string, string>
    {
        ["All that glitters..."] = "Schiffbruch",
        ["Almost paradise."] = "Unberührtes Paradies",
        ["A good fellow..."] = "Augenblick des Zen",
        ["Reflective waters..."] = "Der zerbrochene See",
        ["Crazed Chieftain..."] = "Die Jadeinseln",
        ["Somethin' fishy..."] = "Ödes Atoll",
        ["End of the circle..."] = "Weitläufiger Dschungel",
        ["The last to fall..."] = "Trostlose Steilküste",
        ["Stardrinker..."] = "Abgelegener Tempel",
        ["Origin of the fall..."] = "Geheimnisvolle Insel",
        ["Nothin' to drink..."] = "Stagnierendes Becken",
        ["Unknown ruins..."] = "Exhumierte Ruinen",
        ["It's dry at least..."] = "Abgetragene Schlucht",
        ["Fallen stars..."] = "Heide der Untergegangenen Himmel",
        ["Endless cliffs..."] = "Zerklüftete Halbinsel",
        ["Warm but risky..."] = "Abgegraste Prärie",
        ["Bleak and awful..."] = "Gebleichte Untiefen",
        ["Wild roaming free..."] = "Üppige Insel",
        ["Cold as ice..."] = "Kalte Klippen",
        ["Sulphite!"] = "Verbrannte Sandbank",
    };

    // Rumour and island as the client of this language names them, for the table the overlay
    // draws beside the panel (it follows the game's language, not the program window's);
    // null: as the rumour table has them (English).
    public static (string Rumour, string Island)? Shown(string language, string english) =>
        language == GameLanguage.German && German.TryGetValue(english, out var rumour) ? (rumour, GermanIslands[english]) : null;

    // The texts as the client of this language writes them, each with the English text it
    // stands for. A language without texts of its own: the English ones.
    public static IReadOnlyList<(string Shown, string English)> In(string language) =>
        language == GameLanguage.German ? All.Select(t => (German[t.Text], t.Text)).ToArray() : All.Select(t => (t.Text, t.Text)).ToArray();
}

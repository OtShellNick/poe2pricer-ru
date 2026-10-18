namespace Poe2Pricer.Core;

// The language the game client runs in — not the language of the program window (Texts): a
// German client may be priced by an English program. Read from the game's settings file
// (GameConfig), entry "language" below [LANGUAGE]. What the game writes in its own language
// and the program reads as text or draws as a picture depends on it: so far the rumour lines
// (RumourTexts, RumourLineArt).
public static class GameLanguage
{
    public const string English = "en";
    public const string German = "de";

    // The languages the program can read the game in; any other counts as English.
    public static string Known(string? code) => code == German ? German : English;
}

using System.Windows.Media;

namespace Poe2Pricer.Ui;

// Colours of the program window. The only source of the colour values: Palette lays them into
// the resources as brushes, Themes/Theme.xaml names them from there.
// Colour has a meaning, it is no decoration: brass (Accent) for what is
// active, the main action, the focus and links, and for a hint that is no fault (the text-size
// line above the foot, issue #1); red (Warning) for a warning or an error; green
// (Ok) for "loaded" and "saved" in the status line; everything else in the neutral colours.
public sealed record Theme(
    Color Back,
    Color Panel,
    Color Field,
    Color TabIdle,
    Color Hover,
    Color Line,
    Color LineLight,
    Color Text,
    Color Muted,
    Color Accent,
    Color AccentHover,
    Color AccentDark,
    Color OnAccent,
    Color Chip,
    Color Warning,
    Color Ok
)
{
    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    // The program's palette: slate blue with antique brass, dark and strong (window, panel,
    // field and idle tab measured in the mock-up, text and accent set, the rest derived).
    // OnAccent is the dark text on brass. Chip is the dark ground of a rating (here the colour
    // of the fields), Warning the name of a rumour the source warns of.
    public static readonly Theme Slate = new(
        Back: C("#03141B"), Panel: C("#102D3C"), Field: C("#071B26"), TabIdle: C("#0C2732"), Hover: C("#1A3F52"),
        Line: C("#24495A"), LineLight: C("#3A6478"), Text: C("#E8E6D9"), Muted: C("#8FA9B5"),
        Accent: C("#C7A75A"), AccentHover: C("#E0C98A"), AccentDark: C("#8A7036"), OnAccent: C("#1B2630"),
        Chip: C("#071B26"), Warning: C("#FF7878"),
        // The dot of the status line, as in the mock-up.
        Ok: C("#4FC36B"));

    public static Theme Current { get; } = Slate;

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}

using System.Windows;
using System.Windows.Media;

namespace Poe2Pricer.Ui;

// The colours of Theme.Current as brushes, each under the name of its colour ("Field", "Line",
// …), and the brass gradient of the corner fittings ("Ornament"). Themes/Theme.xaml merges it, so its styles can name the brushes; the values stay in the
// class Theme and nowhere else.
public sealed class Palette : ResourceDictionary
{
    public Palette()
    {
        var theme = Theme.Current;
        foreach (var colour in typeof(Theme).GetProperties().Where(p => p.PropertyType == typeof(Color)))
        {
            var brush = new SolidColorBrush((Color)colour.GetValue(theme)!);
            brush.Freeze();
            this[colour.Name] = brush;
        }
        // The corner fittings: brass running from light to dark, as on a worked metal edge.
        var ornament = new LinearGradientBrush(
            [new GradientStop(theme.AccentHover, 0), new GradientStop(theme.Accent, .45), new GradientStop(theme.AccentDark, 1)],
            new System.Windows.Point(0, 0),
            new System.Windows.Point(1, 1)
        );
        ornament.Freeze();
        this[Ornament] = ornament;
    }

    public const string Ornament = "Ornament";
}

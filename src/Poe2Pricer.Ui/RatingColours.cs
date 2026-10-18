using System.Windows.Media;

namespace Poe2Pricer.Ui;

// Rating colours as in PoeAncient: S gold, A green, B light blue, C/D grey, F red. The same in
// the overlay (LabelLayer) and in the program window's rumour list.
public static class RatingColours
{
    private static readonly Brush S = Fixed(255, 201, 64), A = Fixed(80, 220, 110), B = Fixed(120, 190, 255), F = Fixed(255, 90, 90), Other = Fixed(170, 170, 170);

    // One brush per colour, frozen: asked for with every row drawn, in the window and the overlay.
    private static Brush Fixed(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public static Brush Of(string rating) =>
        rating.Length == 0 ? Brushes.Gray
        : char.ToUpperInvariant(rating[0]) switch
        {
            'S' => S,
            'A' => A,
            'B' => B,
            'F' => F,
            _ => Other,
        };
}

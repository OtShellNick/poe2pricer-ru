using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Poe2Pricer.Core;
using Poe2Pricer.Core.Market;

namespace Poe2Pricer.App;

// Draws the price labels: number on a dark plate centred on the item's bottom edge, the
// currency orb small in the top right corner of the item on a plate of its own; "!" large, "?"
// and "<" small, also at the bottom. Coordinates are physical pixels relative to the game
// client area.
public sealed class LabelLayer : FrameworkElement
{
    private static readonly Brush Plate = Frozen(new SolidColorBrush(Color.FromArgb(190, 0, 0, 0)));
    private static readonly Brush LightPlate = Frozen(new SolidColorBrush(Color.FromArgb(235, 240, 234, 220)));
    private static readonly Brush Unique = Frozen(new SolidColorBrush(Color.FromRgb(223, 142, 60)));
    private static readonly Pen Outline = new(Brushes.Black, 2) { LineJoin = PenLineJoin.Round };
    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public Label[] Labels { get; set; } = [];

    // Rumour table beside the "Uncharted Waters" panel, like PoeAncientsPriceHelper shows it:
    // Rumour, Map, Mods, Rating; one row per rumour, level with its line in the panel.
    public (Box Panel, RumourLine[] Lines)? Rumours { get; set; }
    public ImageSource? Divine { get; set; }
    public ImageSource? Exalted { get; set; }
    public double PixelScale { get; set; } = 1;

    // Where the labels' origin lies on this element (the preview draws onto a whole picture;
    // the overlay window itself lies on the game's part of the screen).
    public Point Origin { get; set; }

    private static Brush Frozen(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }

    // On a light plate the same meaning in dark shades.
    public static Brush DarkColourOf(Label l) =>
        l.MostValuable ? new SolidColorBrush(Color.FromRgb(0, 120, 40))
        : l.Tier switch
        {
            Tier.Low => new SolidColorBrush(Color.FromRgb(95, 95, 95)),
            Tier.High => new SolidColorBrush(Color.FromRgb(150, 105, 0)),
            Tier.Divine => new SolidColorBrush(Color.FromRgb(185, 80, 0)),
            _ => Brushes.Black,
        };

    public static Brush ColourOf(Label l) =>
        l.MostValuable ? new SolidColorBrush(Color.FromRgb(80, 255, 120))
        : l.Tier switch
        {
            Tier.Low => new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            Tier.High => new SolidColorBrush(Color.FromRgb(255, 201, 64)),
            Tier.Divine => new SolidColorBrush(Color.FromRgb(255, 140, 0)),
            _ => Brushes.White,
        };

    protected override void OnRender(DrawingContext dc)
    {
        dc.PushTransform(new TranslateTransform(Origin.X, Origin.Y));
        dc.PushTransform(new ScaleTransform(PixelScale, PixelScale));
        if (Rumours is { } r)
            DrawRumours(dc, r.Panel, r.Lines);
        foreach (var l in Labels)
        {
            var b = l.Bounds;
            double cx = b.X + b.Width / 2.0;
            if (l.Row)
            {
                DrawRow(dc, l);
                continue;
            }
            if (l.Mark is "<" or "?")
            {
                // "<" below the floor, "?" no market price: both mean "recognised, no number".
                // Small grey mark on a small plate, so they do not compete with the prices:
                // an item without any label looks like a recognition failure, a "?" as large
                // as a price out of proportion. "!" stays large: it asks for the mouse or
                // cursor on a greyed-out omen.
                dc.DrawRoundedRectangle(Plate, null, new Rect(cx - 11, b.Bottom - 26, 22, 22), 4, 4);
                Text(dc, l.Mark, 18, Brushes.Silver, cx, b.Bottom - 15);
                continue;
            }
            if (l.Mark is { } mark)
            {
                double my = b.Bottom - 3 - 19;
                dc.DrawRoundedRectangle(Plate, null, new Rect(cx - 14, my - 19, 28, 38), 6, 6);
                Text(dc, mark, mark == "!" ? 34 : 26, mark == "!" ? Brushes.Red : Brushes.LightGray, cx, my);
                continue;
            }
            if (l.Text is null)
                continue;
            string number = l.Text.Replace(" div", "").Replace(" ex", "");
            // One size for every price, whatever the size of the item (PriceSize). Only a number
            // too long for its field is set smaller, as far as it takes to fit.
            double size = PriceSize;
            var text = Format(number, size, ColourOf(l));
            while (text.Width + 12 > b.Width && size > SmallestPriceSize)
                text = Format(number, --size, ColourOf(l));
            // Centred on the bottom edge: the top left holds the stack size in the stash, the
            // top right the orb (for every window alike).
            double pw = text.Width + 12,
                ph = size + 8,
                py = b.Bottom - 3 - ph / 2;
            dc.DrawRoundedRectangle(Plate, null, new Rect(cx - pw / 2, py - ph / 2, pw, ph), 5, 5);
            Draw(dc, text, cx - text.Width / 2, py - text.Height / 2);
            var orb = l.UsesDivine ? Divine : Exalted;
            if (orb is not null)
            {
                double s = OrbSize;
                dc.DrawRoundedRectangle(Plate, null, new Rect(b.Right - s - 5, b.Y + 3, s + 2, s + 2), 4, 4);
                dc.DrawImage(orb, new Rect(b.Right - s - 4, b.Y + 4, s, s));
            }
        }
        dc.Pop();
        dc.Pop();
    }

    // One size for every price of the overlay — on a small item, on a large one and beside a
    // list row — and one for the orb beside it. 18 is the largest size at which five
    // characters ("116.7") fit the narrowest field (64 px); no price is longer.
    private const double PriceSize = 18;
    private const double SmallestPriceSize = 12;
    private const double OrbSize = 20;

    // Beside a list row: plate with the orb left and the number right of it; anchored at the
    // left edge, or at the right edge when AlignRight.
    private void DrawRow(DrawingContext dc, Label l)
    {
        var b = l.Bounds;
        double cy = b.Y + b.Height / 2.0;
        var plate = l.Light ? LightPlate : Plate;
        if (l.Note is { } note)
        {
            // A fixed text instead of a price: no orb, the colour of unique items.
            var words = Format(note, 18, l.Light ? Brushes.SaddleBrown : Unique);
            double nw = words.Width + 16,
                nx = l.AlignRight ? b.Right - nw : b.X;
            dc.DrawRoundedRectangle(plate, null, new Rect(nx, cy - 16, nw, 32), 5, 5);
            Draw(dc, words, nx + 8, cy - words.Height / 2, outline: !l.Light);
            return;
        }
        if (l.Mark is { } mark)
        {
            double mx = l.AlignRight ? b.Right - 30 : b.X;
            dc.DrawRoundedRectangle(plate, null, new Rect(mx, cy - 15, 30, 30), 5, 5);
            var m = Format(mark, 22, l.Light ? Brushes.DimGray : Brushes.LightGray);
            Draw(dc, m, mx + 15 - m.Width / 2, cy - m.Height / 2, outline: !l.Light);
            return;
        }
        if (l.Text is null)
            return;
        string number = l.Text.Replace(" div", "").Replace(" ex", "");
        var text = Format(number, PriceSize, l.Light ? DarkColourOf(l) : ColourOf(l));
        // The best reward lies out of view: an arrow that way before the orb.
        var arrow = l.Arrow is null ? null : Format(l.Arrow, 18, l.Light ? DarkColourOf(l) : ColourOf(l));
        double s = OrbSize,
            lead = arrow is null ? 0 : arrow.Width + 6,
            w = 6 + lead + s + 6 + text.Width + 8,
            x = l.AlignRight ? b.Right - w : b.X;
        dc.DrawRoundedRectangle(plate, null, new Rect(x, cy - 16, w, 32), 5, 5);
        if (arrow is not null)
            Draw(dc, arrow, x + 6, cy - arrow.Height / 2, outline: !l.Light);
        if ((l.UsesDivine ? Divine : Exalted) is { } orb)
            dc.DrawImage(orb, new Rect(x + 6 + lead, cy - s / 2, s, s));
        Draw(dc, text, x + 6 + lead + s + 6, cy - text.Height / 2, outline: !l.Light);
    }

    private static readonly Typeface Regular = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private void DrawRumours(DrawingContext dc, Box panel, RumourLine[] lines)
    {
        if (lines.Length == 0)
            return;
        const double size = 17,
            gap = 18,
            pad = 10,
            rowHeight = 30;
        FormattedText T(string text, Brush brush, bool bold = false, double sz = size) =>
            new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold ? Face : Regular, sz, brush, 1);
        // The table follows the language of the game, not that of the program window: head, rumour
        // and island as the game names them; mods and notes are the community's (English).
        string language = lines[0].Language;
        var header = new[] { "OverlayRumour", "OverlayIsland", "OverlayMods", "OverlayTier", "OverlayNote" }.Select(h => T(Poe2Pricer.Core.Texts.In(language, h), Brushes.Gray, false, 13)).ToArray();
        var warn = new SolidColorBrush(Color.FromRgb(255, 110, 110));
        // Hint column at most 280 px wide, two lines, then "…": long warnings would make the
        // whole table too wide. Rumour lines in the panel are ~50 px apart,
        // so two lines keep the rows aligned.
        FormattedText Hint(string text, Brush brush)
        {
            var f = T(text, brush, false, 14);
            f.MaxTextWidth = 280;
            f.MaxLineCount = 2;
            f.Trimming = TextTrimming.CharacterEllipsis;
            return f;
        }
        var cells = lines
            .Select(l =>
                l.Entry is { } e
                    ? new[]
                    {
                        T(l.Name ?? e.Name, e.Warning.Length > 0 ? warn : Brushes.White, true),
                        T(l.Island ?? e.Map, Brushes.LightGray),
                        T(e.Mods, Brushes.LightGray),
                        T(e.Shown + (e.Own.Length > 0 ? "*" : ""), Poe2Pricer.Ui.RatingColours.Of(e.Shown), true),
                        Hint(e.Hint, e.Note.Length == 0 && e.Warning.Length > 0 ? warn : Brushes.Gray),
                    }
                    : new[] { T(l.Text, Brushes.Gray), T("", Brushes.Gray), T("", Brushes.Gray), T("?", Brushes.Gray, true), T("", Brushes.Gray) }
            )
            .ToArray();
        var widths = Enumerable.Range(0, 5).Select(c => Math.Max(header[c].Width, cells.Max(r => r[c].Width))).ToArray();
        double width = widths.Sum() + gap * 4 + pad * 2;
        // Opposite the side the panel leans to; left when there is room.
        double x = panel.X - width - 12 >= 0 ? panel.X - width - 12 : panel.Right + 12;
        double top = lines.Min(l => l.CenterY) - rowHeight / 2 - 22;
        // Sources are credited in the program window only.
        double height = (lines.Max(l => l.CenterY) - lines.Min(l => l.CenterY)) + rowHeight + 22 + pad;
        dc.DrawRoundedRectangle(Frozen(new SolidColorBrush(Color.FromArgb(215, 12, 12, 16))), null, new Rect(x, top, width, height), 6, 6);
        double cx = x + pad;
        for (int c = 0; c < 5; c++)
        {
            dc.DrawText(header[c], new Point(cx, top + 5));
            cx += widths[c] + gap;
        }
        for (int i = 0; i < lines.Length; i++)
        {
            cx = x + pad;
            for (int c = 0; c < 5; c++)
            {
                dc.DrawText(cells[i][c], new Point(cx, lines[i].CenterY - cells[i][c].Height / 2));
                cx += widths[c] + gap;
            }
        }
    }

    private static FormattedText Format(string s, double size, Brush brush) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush, 1);

    private static void Text(DrawingContext dc, string s, double size, Brush brush, double cx, double cy)
    {
        var t = Format(s, size, brush);
        Draw(dc, t, cx - t.Width / 2, cy - t.Height / 2);
    }

    private static void Draw(DrawingContext dc, FormattedText t, double x, double y, bool outline = true)
    {
        if (outline)
            dc.DrawGeometry(null, Outline, t.BuildGeometry(new Point(x, y)));
        dc.DrawText(t, new Point(x, y));
    }

    public static ImageSource? LoadImage(string path)
    {
        if (!File.Exists(path))
            return null;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }
}

using OpenCvSharp;
using Tune = Poe2Pricer.Core.Tuning.RarityCorner;

namespace Poe2Pricer.Core;

// The ornament the game draws at two corners of a unique item: top left and bottom right of
// the whole item, however many cells it takes (orange; grey when the item is greyed out).
// Currency carries none. So a cell read as currency that shows the ornament is no small item
// but a corner of a large one that was not recognised — a staff under its socket rings read
// cell by cell, the lowest cell taken for an orb with a price.
// The templates are the game's own pictures (data/ui/rarity-unique-*.png, fetched by
// tools/tooltip_headers.py); the game draws them at Scale of their size on the reference
// picture, the same factor as the title bars of the tooltips.
public sealed class RarityCorners
{
    private readonly Piece topLeft,
        bottomRight;
    private readonly HashSet<string> currency;

    // The part of the picture that is not see-through (the ornament itself; the picture is as
    // large as a cell and empty but for its corner): its points with their grey value less
    // the mean of all, the length of that, its size, and where it lies in the whole picture.
    private sealed record Piece((int X, int Y, double Value)[] Points, double Length, Size Size, Rect In, Size Whole);

    public RarityCorners(string uiDirectory, Catalog catalog)
    {
        topLeft = Load(uiDirectory, Tune.TopLeftFile);
        bottomRight = Load(uiDirectory, Tune.BottomRightFile);
        currency = catalog.Items.Where(i => i.Kind == Tune.CurrencyKind).Select(i => i.Id).ToHashSet();
    }

    // What was read for certain as a one-cell piece of currency, in a cell that carries the
    // ornament, is not certain: no price there. Whatever else was read stays as it is — a cell
    // without a corner (the middle of the staff), a cell read as a unique or as a greyed-out
    // omen of a shape group ("!"), an ornament hidden by the item's own picture, by the
    // controller's cursor or dimmed away. There the rule does nothing, and the reading is
    // what it was before there was this rule.
    public Recognition Unmask(Mat bgr, Recognition recognition) =>
        recognition with
        {
            Items = recognition
                .Items.Select(item =>
                    item is { Columns: 1, Rows: 1, Uncertain: false, CatalogId: { } id } && currency.Contains(id) && At(bgr, item.Bounds)
                        ? item with { Uncertain = true }
                        : item
                )
                .ToArray(),
        };

    // The picture in grey at the size the game draws it, cut to what is not see-through.
    private static Piece Load(string uiDirectory, string file)
    {
        using var art = Cv2.ImRead(Path.Combine(uiDirectory, file), ImreadModes.Unchanged);
        if (art.Empty() || art.Channels() != 4)
            throw Texts.Failure(m => new InvalidDataException(m), "MissingFile", file);
        using var sized = new Mat();
        Cv2.Resize(art, sized, new Size(), Tune.Scale, Tune.Scale, InterpolationFlags.Area);
        using var gray = new Mat();
        Cv2.CvtColor(sized, gray, ColorConversionCodes.BGRA2GRAY);
        using var alpha = new Mat();
        Cv2.ExtractChannel(sized, alpha, 3);
        using var mask = new Mat();
        Cv2.Threshold(alpha, mask, Tune.AlphaVisible, 255, ThresholdTypes.Binary);
        var seen = Cv2.BoundingRect(mask);
        var points = new List<(int X, int Y, double Value)>();
        for (int y = seen.Top; y < seen.Bottom; y++)
        for (int x = seen.Left; x < seen.Right; x++)
            if (mask.At<byte>(y, x) != 0)
                points.Add((x - seen.X, y - seen.Y, gray.At<byte>(y, x)));
        if (points.Count == 0)
            throw Texts.Failure(m => new InvalidDataException(m), "MissingFile", file);
        double mean = points.Average(p => p.Value);
        var centred = points.Select(p => (p.X, p.Y, p.Value - mean)).ToArray();
        double length = Math.Sqrt(centred.Sum(p => p.Item3 * p.Item3));
        // One grey all over cannot be compared with anything: every fit would be "no number",
        // and the rule would be off without a word. As with every template of data/ui, a
        // picture that cannot be used counts as missing.
        if (length < 1)
            throw Texts.Failure(m => new InvalidDataException(m), "MissingFile", file);
        return new(centred, length, seen.Size, seen, sized.Size());
    }

    // How well the ornament fits at the cell's top left corner (1: as drawn; currency: below
    // Tune.Minimum). The grid is found to a few pixels, so the corner is looked for around it.
    public double TopLeft(Mat bgr, Box cell) => Best(bgr, cell.X + topLeft.In.X, cell.Y + topLeft.In.Y, topLeft);

    public double BottomRight(Mat bgr, Box cell) =>
        Best(bgr, cell.Right - bottomRight.Whole.Width + bottomRight.In.X, cell.Bottom - bottomRight.Whole.Height + bottomRight.In.Y, bottomRight);

    // A cell with the ornament at either corner.
    public bool At(Mat bgr, Box cell) => TopLeft(bgr, cell) >= Tune.Minimum || BottomRight(bgr, cell) >= Tune.Minimum;

    // The correlation of the ornament's points with the picture under them, at the best of
    // the places around (x, y). Counted by hand: OpenCV's masked comparison takes seven times
    // as long for these few points (measured: 0.45 ms per item for both corners against
    // 0.064 ms — some 3 ms per look over a grid).
    private static double Best(Mat bgr, int x, int y, Piece piece)
    {
        var area = new Rect(x - Tune.Search, y - Tune.Search, piece.Size.Width + 2 * Tune.Search, piece.Size.Height + 2 * Tune.Search).Intersect(new Rect(0, 0, bgr.Width, bgr.Height));
        if (area.Width < piece.Size.Width || area.Height < piece.Size.Height)
            return 0;
        using var crop = new Mat(bgr, area);
        using var gray = new Mat();
        Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
        gray.GetArray(out byte[] seen);
        int width = gray.Width;
        double best = 0;
        for (int dy = 0; dy <= area.Height - piece.Size.Height; dy++)
        for (int dx = 0; dx <= area.Width - piece.Size.Width; dx++)
        {
            double sum = 0,
                squares = 0,
                product = 0;
            foreach (var (px, py, value) in piece.Points)
            {
                double v = seen[(dy + py) * width + dx + px];
                sum += v;
                squares += v * v;
                product += v * value;
            }
            // Without any contrast under the ornament there is nothing to compare.
            double spread = squares - sum * sum / piece.Points.Length;
            if (spread > 1e-6)
                best = Math.Max(best, product / (Math.Sqrt(spread) * piece.Length));
        }
        return best;
    }
}

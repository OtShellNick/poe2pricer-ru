using OpenCvSharp;
using Tune = Poe2Pricer.Core.Tuning.Favours;

namespace Poe2Pricer.Core;

// The grid area of one frame in grey values, cut out once per frame.
public sealed record GridPicture(byte[] Pixels, int Width, int Height)
{
    public static GridPicture Of(Mat bgr, FavoursGrid grid)
    {
        var area = grid.Bounds.Rect().Intersect(new Rect(0, 0, bgr.Width, bgr.Height));
        var pixels = new byte[grid.Bounds.Width * grid.Bounds.Height];
        if (area.Width <= 0 || area.Height <= 0)
            return new(pixels, grid.Bounds.Width, grid.Bounds.Height);
        using var crop = new Mat(bgr, area);
        using var gray = new Mat();
        Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
        gray.GetArray(out byte[] seen);
        // A frame that does not hold the whole grid (a cropped capture) leaves the rest black.
        int dx = area.X - grid.Bounds.X,
            dy = area.Y - grid.Bounds.Y;
        for (int y = 0; y < area.Height; y++)
            Array.Copy(seen, y * area.Width, pixels, (y + dy) * grid.Bounds.Width + dx, area.Width);
        return new(pixels, grid.Bounds.Width, grid.Bounds.Height);
    }
}

// A cell now against the picture it was read from. Brightness: the median of every pixel's
// brightness over the same pixel then (1 = unchanged). Structure: the correlation of the grey
// values (1 = the same picture, whatever its brightness).
public readonly record struct CellLook(double Brightness, double Structure)
{
    // Under a popup: a tooltip or dialog darkens by a fixed factor.
    public bool Covered => Brightness < Tune.CoveredBelow;

    // The stored picture was the covered one; the cell is free now.
    public bool Uncovered => Brightness > 1 / Tune.CoveredBelow;

    // To be seen: unchanged, greyed out, deferred, under the mouse or cursor.
    public bool Visible => !Covered && !Uncovered;

    // Something opaque lies there (a tooltip's title bar): nothing of the stored picture is left.
    public bool Replaced => Structure < Tune.ReplacedBelow;

    // To be seen and still the item (or the empty cell) that was read.
    public bool Same => Visible && Structure >= Tune.SameStructure;

    // A picture now against the one stored of the same place, pixel for pixel (grey values).
    public static CellLook Between(ReadOnlySpan<byte> then, ReadOnlySpan<byte> now)
    {
        int n = Math.Min(then.Length, now.Length);
        var ratios = new double[n];
        int measurable = 0;
        double sumThen = 0,
            sumNow = 0,
            thenThen = 0,
            nowNow = 0,
            thenNow = 0;
        for (int i = 0; i < n; i++)
        {
            int was = then[i],
                seen = now[i];
            if (was > Tune.DarkPixel)
                ratios[measurable++] = (double)seen / was;
            sumThen += was;
            sumNow += seen;
            thenThen += was * was;
            nowNow += seen * seen;
            thenNow += was * seen;
        }
        double spread = (thenThen - sumThen * sumThen / n) * (nowNow - sumNow * sumNow / n);
        double structure = spread > 0 ? (thenNow - sumThen * sumNow / n) / Math.Sqrt(spread) : 0;
        double brightness;
        if (measurable >= Tune.MeasurablePixels)
        {
            Array.Sort(ratios, 0, measurable);
            brightness = ratios[measurable / 2];
        }
        else
            // A picture too dark to compare pixel by pixel: the means say whether it got brighter.
            brightness = (sumNow / n + 1) / (sumThen / n + 1);
        return new(brightness, structure);
    }
}

// What every cell of the Favours grid looked like when it was read. Whether a cell is covered,
// greyed out, bought or free again is told by comparing it with its own picture — not by
// guessing where a tooltip lies from its outline, which breaks with every new background,
// title bar and overlay. Immutable: With returns the
// memory with more cells stored.
public sealed class CellMemory
{
    public const int Columns = 12,
        Rows = 10;

    private readonly FavoursGrid grid;
    private readonly byte[] pixels;
    private readonly bool[] stored;
    private readonly bool[] occupied;

    private CellMemory(FavoursGrid grid, byte[] pixels, bool[] stored, bool[] occupied)
    {
        this.grid = grid;
        this.pixels = pixels;
        this.stored = stored;
        this.occupied = occupied;
    }

    public static CellMemory Empty(FavoursGrid grid) =>
        new(grid, new byte[grid.Bounds.Width * grid.Bounds.Height], new bool[Columns * Rows], new bool[Columns * Rows]);

    public bool Stored(int cell) => stored[cell];

    public bool Occupied(int cell) => occupied[cell];

    public bool AnyUnread => stored.Contains(false);

    // The cell's place inside the grid picture.
    private Rect Place(int cell)
    {
        var box = FavoursLocator.CellBox(grid, cell % Columns, cell / Columns, 1, 1);
        return new Rect(box.X - grid.Bounds.X, box.Y - grid.Bounds.Y, box.Width, box.Height).Intersect(new Rect(0, 0, grid.Bounds.Width, grid.Bounds.Height));
    }

    // These cells as they are on this picture; occupied: whether each holds (part of) an item.
    public CellMemory With(GridPicture now, IEnumerable<int> cells, Func<int, bool> isOccupied)
    {
        var next = (byte[])pixels.Clone();
        var has = (bool[])stored.Clone();
        var used = (bool[])occupied.Clone();
        foreach (int cell in cells)
        {
            var at = Place(cell);
            for (int y = at.Top; y < at.Bottom; y++)
                Array.Copy(now.Pixels, y * now.Width + at.Left, next, y * now.Width + at.Left, at.Width);
            has[cell] = true;
            used[cell] = isOccupied(cell);
        }
        return new(grid, next, has, used);
    }

    // Every stored cell against its picture; null for a cell never read.
    public CellLook?[] Look(GridPicture now)
    {
        var looks = new CellLook?[Columns * Rows];
        for (int cell = 0; cell < looks.Length; cell++)
            if (stored[cell])
                looks[cell] = Compare(now, Place(cell));
        return looks;
    }

    // A place in the grid (screen coordinates) against what was stored there: the label spot
    // of an item. Null when it lies outside the grid.
    public CellLook? Look(GridPicture now, Box spot)
    {
        var at = new Rect(spot.X - grid.Bounds.X, spot.Y - grid.Bounds.Y, spot.Width, spot.Height).Intersect(new Rect(0, 0, grid.Bounds.Width, grid.Bounds.Height));
        return at.Width <= 0 || at.Height <= 0 ? null : Compare(now, at);
    }

    private CellLook Compare(GridPicture now, Rect at)
    {
        int n = at.Width * at.Height;
        var then = new byte[n];
        var seen = new byte[n];
        for (int y = 0; y < at.Height; y++)
        {
            Array.Copy(pixels, (at.Top + y) * now.Width + at.Left, then, y * at.Width, at.Width);
            Array.Copy(now.Pixels, (at.Top + y) * now.Width + at.Left, seen, y * at.Width, at.Width);
        }
        return CellLook.Between(then, seen);
    }

    // The cell shows the empty grid: like the picture of a cell that was read as empty (the
    // ornament is the same in every cell). False when no empty cell was ever stored.
    public bool LooksEmpty(GridPicture now, int cell)
    {
        var at = Place(cell);
        int tried = 0;
        for (int other = 0; other < stored.Length && tried < 3; other++)
        {
            if (!stored[other] || occupied[other] || other == cell)
                continue;
            tried++;
            var like = Place(other);
            int w = Math.Min(at.Width, like.Width),
                h = Math.Min(at.Height, like.Height);
            var deviations = new int[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                deviations[y * w + x] = Math.Abs(now.Pixels[(at.Top + y) * now.Width + at.Left + x] - pixels[(like.Top + y) * now.Width + like.Left + x]);
            Array.Sort(deviations);
            if (deviations[deviations.Length / 2] <= Tune.EmptyDeviation)
                return true;
        }
        return false;
    }

    // The grid that was read is still on the picture: the window is open, whatever its button says.
    public bool StillThere(GridPicture now)
    {
        var looks = Look(now).OfType<CellLook>().ToArray();
        return looks.Length > 0 && looks.Count(l => l.Same) >= looks.Length * Tune.StillThere;
    }

    // A dialog dims the whole window at once.
    public static bool WindowDimmed(CellLook?[] looks)
    {
        var seen = looks.OfType<CellLook>().ToArray();
        return seen.Length > 0 && seen.Count(l => l.Brightness < Tune.DimmedBelow) >= seen.Length * Tune.WindowDimmed;
    }
}

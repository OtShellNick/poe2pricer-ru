using OpenCvSharp;
using Tune = Poe2Pricer.Core.Tuning.Favours;

namespace Poe2Pricer.Core;

// Finds the Favours grid by the frame of the tribute bar below it (Anchor): that piece of
// artwork belongs to this window alone, so other 12-column grids are rejected (the Gamble
// vendor's grid sits almost exactly where the keyboard Favours grid is), and item tooltips,
// which open above an item, do not reach it. The window is the same in both UI modes; the
// controller UI shows it 55 px lower, which is how the mode is told. The button "Offer Tribute
// to the King" is no anchor: lettering, which does not fit at other sizes (Anchor).
public sealed class FavoursLocator : IDisposable
{
    private readonly Mat anchor;

    public FavoursLocator(string uiDirectory) => anchor = Anchor.Load(uiDirectory, Tune.AnchorFile);

    public FavoursGrid? Locate(Mat bgr)
    {
        FavoursGrid? best = null;
        foreach (var (mode, top, dx, dy) in Tune.GridTop)
        {
            if (Anchor.InRow(bgr, anchor, top + dy, Tune.SearchRadius) is not { } found)
                continue;
            if (found.Score < Tune.MinimumAnchorScore || found.Score <= (best?.AnchorScore ?? 0))
                continue;
            best = new(
                mode,
                new Box(found.At.X - dx, found.At.Y - dy, (int)Math.Round(Tune.Cell * 12), (int)Math.Round(Tune.Cell * 10)),
                Tune.Cell,
                found.Score
            );
        }
        return best;
    }

    // The part of the picture that tells whether this window is still open and what moved in
    // it: its grid and the place of its anchor.
    public Box LookArea(FavoursGrid grid)
    {
        var (_, _, dx, dy) = Tune.GridTop.First(g => g.Mode == grid.Mode);
        return grid.Bounds.Union(
            new Box(grid.Bounds.X + dx - Tune.SearchRadius, grid.Bounds.Y + dy - Tune.SearchRadius, anchor.Width + 2 * Tune.SearchRadius, anchor.Height + 2 * Tune.SearchRadius)
        );
    }

    public static Box CellBox(FavoursGrid grid, int x, int y, int w, int h)
    {
        int left = (int)Math.Round(grid.Bounds.X + x * grid.CellSize),
            top = (int)Math.Round(grid.Bounds.Y + y * grid.CellSize);
        return new(
            left,
            top,
            (int)Math.Round(grid.Bounds.X + (x + w) * grid.CellSize) - left,
            (int)Math.Round(grid.Bounds.Y + (y + h) * grid.CellSize) - top
        );
    }

    public void Dispose() => anchor.Dispose();
}

using OpenCvSharp;
using Tune = Poe2Pricer.Core.Tuning.Occluders;

namespace Poe2Pricer.Core;

// Finds game popups that lie over a window, by two methods:
// - Outline (Favours grid, remnant list): item tooltips by the left end of their title bar (one
//   style per rarity, one for the skill of a gem), game notifications by their red close
//   button, the dialog, the keyboard hints. Recognition must not trust cells under such a
//   popup: the tooltip darkens everything below it to about a fifth, and its text then passes
//   for items.
// - Darkening (windows with fixed slots, the stash tabs): a popup darkens what lies below it;
//   the slot frames are fixed art, so the bottom frame edge of a covered slot is darker than
//   in the template, right where its label goes. Controller tooltips are see-through, so
//   their outline is not to be guessed there.
public sealed class OccluderDetector : IDisposable
{
    // The ends of a tooltip's title bar are the game's own art, one pair per kind of bar
    // (data/ui/tooltip-header-<art>-left|right.png, fetched from the game files by
    // tools/tooltip_headers.py). "grey" is the bar of currency — orbs, omens, runes —, "white"
    // that of a normal item. Cuts from screenshots are no substitute: they fit their own
    // picture and miss at other window sizes, the game's pieces fit at every size.
    // A Vaal unique carries a medallion on the left end of the unique bar (the game's
    // VaalItemIcon, 2.5 px from the left and 22.5 px from the top of the end piece): a quarter
    // of the piece is covered, and the bare piece fits only around the threshold. Its piece is
    // the two put together, kept at twice the size so that the half pixels are whole ones
    // (Scale: the size of the picture against the game's art); the right end is the unique's.
    // The tooltip of a skill (a gem's row in the remnant list, hovered) has no item bar at
    // all: its title is the game's GemHoverTitle, an ornament on the left with the bar fading
    // out to the right, and the skill's picture in a frame from column 31 on. The piece is the
    // first 30 columns — wider, the picture intrudes and the fit falls — and there is no right
    // end (RightEnd finds none, and the tooltip counts to the grid's or tab's right edge as
    // when a pin hides the end). It fits at least .95 at a skill tooltip, at most .50 elsewhere.
    private static readonly (string Style, string Left, string? Right, double Scale)[] Headers =
    [
        ("grey", "currency", "currency", 1),
        ("white", "white", "white", 1),
        ("magic", "magic", "magic", 1),
        ("rare", "rare", "rare", 1),
        ("unique", "unique", "unique", 1),
        ("unique", "unique-vaal", "unique", 2),
        ("gem", "gem", null, 1),
    ];

    // Measures and thresholds: Tuning.Occluders. The tooltip bottom is a bright line across
    // the full width; separator lines inside the tooltip are as bright but shorter. The
    // confirmation dialog ("Offer all remaining Tribute to the king?") lies over the right
    // columns of the grid.
    private readonly (string Style, Mat Left, Mat? Right, double Scale)[] art;

    // The pieces as the game draws them at a popup size (GameConfig.PopupSize), by the size in
    // hundredths: Tune.HeaderScale × the popup size (the game draws its art at 0.9 of its file
    // size on a picture 1440 px high, times the popup size). Every other measure of a tooltip
    // in pixels grows with the popup size too (Px); the key hints beside the mouse do not.
    // RightLower: the lower half of the right end. A pin lies on its upper half while the tooltip
    // is pinned; the whole end then no longer matches.
    // LeftCoarse: the left piece at Tune.CoarseScale, for the first, cheap search (LeftEnds).
    private readonly Dictionary<int, (string Style, Mat Left, Mat LeftCoarse, Mat? Right, Mat? RightLower)[]> sized = [];
    private readonly Lock sizing = new();
    private readonly Mat close;
    private readonly Mat cancel;
    private readonly Mat cancelLeft;
    private readonly Mat cancelRight;
    private readonly Mat[] hints;

    public double CapThreshold { get; init; } = Tune.CapThreshold;
    public double CloseThreshold { get; init; } = Tune.CloseThreshold;
    public double HintThreshold { get; init; } = Tune.HintThreshold;
    public double DialogThreshold { get; init; } = Tune.DialogThreshold;

    public OccluderDetector(string uiDirectory)
    {
        art = Headers.Select(h => (h.Style, Load(uiDirectory, $"tooltip-header-{h.Left}-left.png"), h.Right is null ? null : Load(uiDirectory, $"tooltip-header-{h.Right}-right.png"), h.Scale)).ToArray();
        close = Load(uiDirectory, "notification-close.png");
        hints = [Load(uiDirectory, "hint-alt.png"), Load(uiDirectory, "hint-shift.png")];
        cancel = Load(uiDirectory, "dialog-cancel.png");
        cancelLeft = new Mat(cancel, new Rect(0, 0, Tune.DialogCap, cancel.Height)).Clone();
        cancelRight = new Mat(cancel, new Rect(cancel.Width - Tune.DialogCap, 0, Tune.DialogCap, cancel.Height)).Clone();
    }

    private static Mat Load(string directory, string name)
    {
        var m = Cv2.ImRead(Path.Combine(directory, name), ImreadModes.Grayscale);
        return m.Empty() ? throw Texts.Failure(m => new InvalidDataException(m), "MissingFile", name) : m;
    }

    private (string Style, Mat Left, Mat LeftCoarse, Mat? Right, Mat? RightLower)[] Caps(double popup)
    {
        int key = (int)Math.Round(popup * 100);
        lock (sizing)
        {
            if (!sized.TryGetValue(key, out var set))
                sized[key] = set = art.Select(a =>
                    {
                        var left = Drawn(a.Left, popup / a.Scale);
                        var coarse = new Mat();
                        Cv2.Resize(left, coarse, new Size(), Tune.CoarseScale, Tune.CoarseScale, InterpolationFlags.Area);
                        var right = a.Right is null ? null : Drawn(a.Right, popup);
                        return (a.Style, left, coarse, right, right is null ? null : new Mat(right, new Rect(0, right.Height / 2, right.Width, right.Height - right.Height / 2)));
                    })
                    .ToArray();
            return set;
        }
    }

    private static Mat Drawn(Mat piece, double popup)
    {
        double scale = Tune.HeaderScale * popup;
        var size = new Size((int)Math.Round(piece.Width * scale), (int)Math.Round(piece.Height * scale));
        var drawn = new Mat();
        Cv2.Resize(piece, drawn, size, 0, 0, scale < 1 ? InterpolationFlags.Area : InterpolationFlags.Cubic);
        return drawn;
    }

    // A measure of a tooltip at the game's standard size, at this popup size.
    private static int Px(int standard, double popup) => GameConfig.Scaled(standard, popup);

    public Occluder[] Detect(Mat bgr, FavoursGrid grid, double popup = GameConfig.StandardPopup)
    {
        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        var found = new List<Occluder>();
        // A mouse tooltip opens above the item, so its title bar lies above the grid bottom;
        // the controller tooltip sits right of the window. Tooltips that miss the grid are
        // kept: their title names the selected item.
        var area = new Rect(0, 0, gray.Width, Math.Min(gray.Height, grid.Bounds.Bottom));
        foreach (var (style, x, y, score, _, right, rightLower) in LeftEnds(gray, _ => area, popup))
        {
            int end = RightEnd(gray, right, rightLower, x, y, popup) ?? grid.Bounds.Right;
            // Not ended at the dark inner margin as beside the remnant list: over the Favours
            // grid that margin ends too early in tall unique tooltips, and the items under the
            // rest of the tooltip would count as visible.
            int bottom = Bottom(gray, x, end, y, popup);
            found.Add(new($"tooltip-{style}", new Box(x, y, end - x, bottom - y), score));
        }
        // The window close buttons share the red cross; only a button beside the grid rows
        // belongs to a notification that can reach over the grid.
        var beside = new Rect(grid.Bounds.X, grid.Bounds.Y, gray.Width - grid.Bounds.X, Math.Min(gray.Height, grid.Bounds.Bottom) - grid.Bounds.Y);
        foreach (var (at, score) in Peaks(gray, beside, close, CloseThreshold))
        {
            int cx = at.X + close.Width / 2,
                cy = at.Y + close.Height / 2;
            int x = Math.Max(0, cx - Tune.NotificationLeft),
                y = Math.Max(0, cy - Tune.NotificationHalfHeight);
            found.Add(new("notification", new Box(x, y, cx + close.Width - x, cy + Tune.NotificationHalfHeight - y), score));
        }
        var button = DialogButton(gray);
        if (button.Score >= DialogThreshold)
            found.Add(new("dialog", new Box(button.At.X - Tune.DialogLeft, button.At.Y - Tune.DialogUp, Tune.DialogLeft + Tune.DialogRight, Tune.DialogUp + Tune.DialogDown), button.Score));
        found.AddRange(HintsIn(gray, grid.Bounds));
        return found.ToArray();
    }

    // The dialog's right-hand button by its two ends, the width of the button apart: the frame
    // without the lettering, so that the button is found whatever the client's language writes
    // on it ("CANCEL", "ABBRECHEN"). With the dialog open the pair fits almost perfectly, on a
    // Favours window without it about half at most. In stash pictures the pair fits other
    // buttons of the same build — it is searched only while the Favours window is open. At a
    // larger UI Text Size the button grows and the pair no longer fits.
    private (Point At, double Score) DialogButton(Mat gray)
    {
        int apart = cancel.Width - Tune.DialogCap;
        if (gray.Width < cancel.Width || gray.Height < cancel.Height)
            return default;
        using var leftScores = new Mat();
        using var rightScores = new Mat();
        Cv2.MatchTemplate(gray, cancelLeft, leftScores, TemplateMatchModes.CCoeffNormed);
        Cv2.MatchTemplate(gray, cancelRight, rightScores, TemplateMatchModes.CCoeffNormed);
        using var leftHere = new Mat(leftScores, new Rect(0, 0, leftScores.Width - apart, leftScores.Height));
        using var rightThere = new Mat(rightScores, new Rect(apart, 0, rightScores.Width - apart, rightScores.Height));
        using var both = new Mat();
        Cv2.Min(leftHere, rightThere, both);
        Cv2.MinMaxLoc(both, out _, out double score, out _, out Point at);
        return (at, score);
    }

    // Only the keyboard hints ("ALT Inspect", "SHIFT Compare") beside the item under the mouse:
    // fixed art the game draws itself, found in a few ms. Enough for every frame after the first
    // look, where the cell memory tells what a popup covers (CellMemory).
    public Occluder[] Hints(Mat bgr, FavoursGrid grid)
    {
        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        return HintsIn(gray, grid.Bounds).ToArray();
    }

    // The key hints ("ALT Inspect") inside an area: the stash tab.
    public Box[] Hints(Mat bgr, Box inside)
    {
        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        return HintsIn(gray, inside).Select(h => h.Bounds).ToArray();
    }

    private List<Occluder> HintsIn(Mat gray, Box area)
    {
        var found = new List<Occluder>();
        var inside = area.Rect().Intersect(new Rect(0, 0, gray.Width, gray.Height));
        foreach (var hint in hints)
        foreach (var (at, score) in Peaks(gray, inside, hint, HintThreshold))
            found.Add(new("hint", new Box(at.X + Tune.HintLeft, at.Y - Tune.HintTop, Tune.HintRight - Tune.HintLeft, Tune.HintTop + Tune.HintBottom), score));
        return found;
    }

    // The left ends of title bars in an area (it may depend on the piece searched), each at the
    // place of the bar itself: the game's piece carries a shadow of Tune.HeaderInset px around
    // the bar, and every measure of a tooltip counts from the bar. One tooltip has one left
    // end — where pieces of several kinds fit at one place, the best fitting is it (the bars of
    // currency and of a normal item differ by a spiral only: on a currency tooltip both fit).
    // Title: how far the title bar reaches down from there — the piece's height, one line or two.
    private List<(string Style, int X, int Y, double Score, int Title, Mat? Right, Mat? RightLower)> LeftEnds(Mat gray, Func<Mat, Rect> area, double popup)
    {
        // Searched first at half resolution, then each candidate exactly where it lies: the
        // search over the whole area for every kind of bar is otherwise the largest part of
        // reading a stash tab. What fits at full resolution fits at half resolution a little
        // less (CoarseSlack below the threshold); places without a tooltip stay well below.
        using var coarse = new Mat();
        Cv2.Resize(gray, coarse, new Size(), Tune.CoarseScale, Tune.CoarseScale, InterpolationFlags.Area);
        var all = new List<(string Style, Point At, double Score, Mat Left, Mat? Right, Mat? RightLower)>();
        foreach (var (style, left, leftCoarse, right, rightLower) in Caps(popup))
        {
            var within = area(left).Intersect(new Rect(0, 0, gray.Width, gray.Height));
            var small = new Rect(
                (int)(within.X * Tune.CoarseScale),
                (int)(within.Y * Tune.CoarseScale),
                Math.Min(coarse.Width, (int)Math.Ceiling(within.Right * Tune.CoarseScale)) - (int)(within.X * Tune.CoarseScale),
                Math.Min(coarse.Height, (int)Math.Ceiling(within.Bottom * Tune.CoarseScale)) - (int)(within.Y * Tune.CoarseScale)
            );
            var ends = new List<(Point At, double Score)>();
            foreach (var (near, _) in Peaks(coarse, small, leftCoarse, CapThreshold - Tune.CoarseSlack))
            {
                var around = new Rect(
                    (int)(near.X / Tune.CoarseScale) - Tune.CoarseMargin,
                    (int)(near.Y / Tune.CoarseScale) - Tune.CoarseMargin,
                    left.Width + 2 * Tune.CoarseMargin,
                    left.Height + 2 * Tune.CoarseMargin
                ).Intersect(within);
                var exact = Peaks(gray, around, left, CapThreshold);
                if (exact.Count > 0 && !ends.Any(e => e.At == exact[0].At))
                    ends.Add(exact[0]);
            }
            // Best first, as a search at full resolution finds them.
            all.AddRange(ends.OrderByDescending(e => e.Score).Select(e => (style, e.At, e.Score, left, right, rightLower)));
        }
        int inset = Px(Tune.HeaderInset, popup);
        return all.Where(e => !all.Any(o => o.Score > e.Score && Math.Abs(o.At.X - e.At.X) < e.Left.Width && Math.Abs(o.At.Y - e.At.Y) < e.Left.Height))
            .Select(e => (e.Style, e.At.X + inset, e.At.Y + inset, e.Score, e.Left.Height - inset, e.Right, e.RightLower))
            .ToList();
    }

    // Nearest right end on the same row, as the place where the bar itself ends; the comparison tooltip for
    // equipment always sits further right, so the nearest one closes the item's own tooltip.
    // With a pin on it only the lower half of the end is compared. A bar without a right end
    // (gem) has none to find.
    private int? RightEnd(Mat gray, Mat? right, Mat? rightLower, int x, int y, double popup)
    {
        if (right is null || rightLower is null)
            return null;
        int from = x + Px(Tune.RightFrom, popup),
            top = Math.Max(0, y - Px(Tune.RightAbove, popup)),
            extra = Px(Tune.RightExtra, popup);
        if (from + right.Width > gray.Width)
            return null;
        var band = new Rect(from, top, gray.Width - from, Math.Min(gray.Height - top, right.Height + extra));
        var peaks = Peaks(gray, band, right, CapThreshold);
        if (peaks.Count == 0)
        {
            int lower = top + right.Height - rightLower.Height;
            peaks = Peaks(gray, new Rect(from, lower, gray.Width - from, Math.Min(gray.Height - lower, rightLower.Height + extra)), rightLower, CapThreshold);
        }
        return peaks.Count == 0 ? null : peaks.Min(p => p.At.X) + right.Width - Px(Tune.HeaderInset, popup);
    }

    // The game draws the bottom border in the colour of what lies behind it: where the screen
    // behind is pure black there is no border to see, and those pixels say nothing about the row
    // (the tooltip of an item in the left columns hangs over the black beside the window). The
    // row must still show on at least VisibleBorder of the width.
    private static int Bottom(Mat gray, int left, int right, int top, double popup)
    {
        int inset = Px(Tune.BorderInset, popup);
        int x0 = Math.Min(gray.Width - 1, left + inset),
            x1 = Math.Max(x0 + 1, Math.Min(gray.Width, right - inset));
        var row = new byte[x1 - x0];
        var columns = new int[x1 - x0];
        for (int y = top + Px(Tune.MinimumTooltipHeight, popup); y < gray.Height; y++)
        {
            int seen = 0;
            for (int x = x0; x < x1; x++)
                if (gray.At<byte>(y, x) is > 0 and var value)
                {
                    row[seen] = value;
                    columns[seen++] = x;
                }
            if (seen == 0 || seen < (x1 - x0) * Tune.VisibleBorder)
                continue;
            Array.Sort(row, 0, seen);
            if (row[seen / 2] >= Tune.BrightBorder && row[seen / 10] >= Tune.FullWidth
                && (Undimmed(gray, columns.AsSpan(0, seen), y, popup) || DarkOnBothSides(gray, columns.AsSpan(0, seen), y, popup) && row[seen / 10] >= Tune.FullWidthOverDark))
                return y;
        }
        return gray.Height;
    }

    // Tooltips whose left edge lies in a narrow vertical band: beside the remnant list the
    // game opens them right at the window edge. Searching only that band takes well under a
    // millisecond, so it can run several times a second. Width is not measured (the labels
    // there sit at the left edge); the bottom is where the tooltip stops dimming the scene
    // (DimmingEnd).
    public Box[] TooltipsAt(Mat bgr, int xFrom, int xTo, double popup = GameConfig.StandardPopup)
    {
        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        var band = new Rect(Math.Max(0, xFrom), 0, Math.Min(gray.Width, xTo) - Math.Max(0, xFrom), gray.Height);
        var found = new List<Box>();
        foreach (var (_, x, y, _, title, _, _) in LeftEnds(gray, _ => band, popup))
        {
            int bottom = DimmingEnd(gray, x, y, title, popup);
            found.Add(new Box(x, y, Px(Tune.BesideWidth, popup), bottom - y));
        }
        return found.ToArray();
    }

    // Tooltips over a stash tab, from the left end of the title bar to its mirrored right end.
    // The left end is searched from the picture's left edge, not from the tab's: in the
    // controller UI the tooltip of a slot in the left columns hangs over the tab list, its end
    // left of the tab. The title bar is as wide as the item's text, so a fixed width as beside
    // the remnant list would leave the right slots free. Without a right end the tooltip counts
    // up to the tab's right edge.
    // The bottom comes from the dark inner margin as in TooltipsAt, but only where that margin
    // lies over the tab. Left of it the margin says nothing: over the black beside the window it
    // never turns bright (the box would reach the picture's bottom and hide every price), over
    // the tab list it is bright at once. There only the title bar counts; the body below it is
    // dark and left to the slots' own measure. The title bar counts in any case: the margin
    // search can end inside a two-line title.
    public Box[] TooltipsOver(Mat bgr, Box tab, double popup = GameConfig.StandardPopup)
    {
        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        var found = new List<Box>();
        // A title bar that starts below the tab covers none of it; the search costs by area.
        foreach (var (_, x, y, _, title, right, rightLower) in LeftEnds(gray, left => new Rect(0, 0, Math.Min(gray.Width, tab.Right), Math.Min(gray.Height, tab.Bottom + left.Height)), popup))
        {
            int end = RightEnd(gray, right, rightLower, x, y, popup) ?? tab.Right;
            int bottom = y + Px(Tune.TitleHeight, popup);
            if (x + Px(Tune.MarginFrom, popup) >= tab.X)
                bottom = Math.Max(bottom, MarginBottom(gray, x, y, title, popup));
            found.Add(new Box(x, y, end - x, bottom - y));
        }
        return found.ToArray();
    }

    // Bottom of a tooltip that lies over the game's scene, as beside the remnant list. The body
    // has no frame and no art of its own (the game files hold none): it is the scene, dimmed to
    // about a quarter. So the bottom is where the dimming ends: the row below which the scene
    // is DimFactor times as bright as above it, across the tooltip's left BesideWidth px
    // (narrower than any tooltip). Above and below are the medians of DimWindow rows per
    // column — more rows than a line of text is high, so writing and separator lines drop out —
    // and what counts is the share of columns that show the step. Going down from the title
    // bar, the first rows where at least DimShare of the columns show it are the bottom, at the
    // row that shows it most; its place is then taken exactly with a window of DimFine rows.
    // At the real bottom most columns show the step, at any other place (lines of writing, a
    // see-through "GAME PAUSED") about half at most; window, factor and floor may vary widely
    // without moving the result. The dark inner margin is no measure here: over a bright scene
    // it is brighter than its limit right below the title bar and would end the tooltip far
    // too early.
    // No such row — the scene below is black, or the tooltip reaches the picture's bottom: the
    // tooltip counts to the bottom of the picture.
    // Over the Favours grid or a stash tab this does not hold: what lies below a tooltip there
    // is dark interface, not scene.
    private static int DimmingEnd(Mat gray, int left, int top, int title, double popup)
    {
        int x0 = left + Px(Tune.DimFrom, popup),
            x1 = Math.Min(gray.Width, left + Px(Tune.BesideWidth, popup)),
            first = top + title,
            window = Px(Tune.DimWindow, popup),
            fine = Px(Tune.DimFine, popup);
        int columns = (x1 - x0) / Tune.DimBin,
            rows = gray.Height - first;
        if (columns < 1 || rows < Tune.DimBelowTitle + window + 3)
            return gray.Height;
        using var strip = new Mat(gray, new Rect(x0, first, columns * Tune.DimBin, rows));
        using var binned = new Mat();
        Cv2.Resize(strip, binned, new Size(columns, rows), 0, 0, InterpolationFlags.Area);
        binned.GetArray(out byte[] pixels);

        // The medians slide down row by row: a histogram per column and window, all in one
        // block (two for each column: above, below), with the median kept as values come and
        // go.
        int slots = 2 * columns;
        int[] counts = System.Buffers.ArrayPool<int>.Shared.Rent(slots * 256);
        Array.Clear(counts, 0, slots * 256);
        Span<int> total = stackalloc int[slots],
            median = stackalloc int[slots],
            less = stackalloc int[slots]; // values below the median
        total.Clear();
        median.Clear();
        less.Clear();
        var medians = new byte[rows * columns];
        int best = -1;
        double bestShare = 0;
        try
        {
            // Row r (counted from below the title bar) is compared: rows [r−1−window, r−1)
            // above it against rows [r+2, r+2+window) below it.
            int start = Tune.DimBelowTitle,
                end = rows - window - 2;
            for (int c = 0; c < columns; c++)
            {
                for (int r = Math.Max(0, start - 1 - window); r < start - 1; r++)
                    Put(counts, total, median, less, 2 * c, pixels[r * columns + c], 1);
                for (int r = start + 2; r < start + 2 + window; r++)
                    Put(counts, total, median, less, 2 * c + 1, pixels[r * columns + c], 1);
            }
            // The rows above row r are the rows below row r − 3 − window: their medians were
            // taken then and are kept, so that only the window below slides. The first rows
            // have no such earlier row (and a shorter window above, cut off at the title
            // bar); for them the window above slides too.
            int kept = start + 3 + window;
            for (int r = start; r < end; r++)
            {
                int steps = 0,
                    here = r * columns,
                    earlier = (r - 3 - window) * columns;
                for (int c = 0; c < columns; c++)
                {
                    int under = Middle(counts, total, median, less, 2 * c + 1);
                    medians[here + c] = (byte)under;
                    int over = r < kept ? Middle(counts, total, median, less, 2 * c) : medians[earlier + c];
                    if (Step(over, under))
                        steps++;
                }
                // The first rows that show the step are the bottom, where they show it most:
                // what lies further down is scene, and an edge there is none of the tooltip.
                double share = (double)steps / columns;
                if (share >= Tune.DimShare && share > bestShare)
                    (best, bestShare) = (r, share);
                if (r + 1 == end || best >= 0 && share < Tune.DimShare)
                    break;
                int enters = (r - 1) * columns,
                    leaves = (r - 1 - window) * columns,
                    gone = (r + 2) * columns,
                    comes = (r + 2 + window) * columns;
                for (int c = 0; c < columns; c++)
                {
                    Put(counts, total, median, less, 2 * c + 1, pixels[gone + c], -1);
                    Put(counts, total, median, less, 2 * c + 1, pixels[comes + c], 1);
                }
                if (r + 1 < kept)
                    for (int c = 0; c < columns; c++)
                    {
                        Put(counts, total, median, less, 2 * c, pixels[enters + c], 1);
                        if (leaves >= 0)
                            Put(counts, total, median, less, 2 * c, pixels[leaves + c], -1);
                    }
            }
        }
        finally
        {
            System.Buffers.ArrayPool<int>.Shared.Return(counts);
        }
        if (best < 0)
            return gray.Height;

        // The tall windows find the bottom for certain but smeared over their height; the
        // short ones say where it is. A sharp edge shows equally strong over as many rows as
        // the short window reaches: the middle of those rows is the edge. Where the short
        // windows show no step at all (a soft edge), the row of the tall ones stands.
        // The median of the fine rows from each row on, once per row and column: the rows
        // above row r begin at r − 1 − fine, the rows below it at r + 2.
        int lowest = Math.Max(fine + 1, best - window),
            highest = Math.Min(best + window, rows - 2 - fine),
            base0 = lowest - 1 - fine,
            count = highest + 2 - base0 + 1;
        int from = best,
            to = best,
            most = 0;
        if (highest >= lowest)
        {
            var fineMedians = new byte[count * columns];
            Span<byte> some = stackalloc byte[fine];
            for (int row = 0; row < count; row++)
            for (int c = 0; c < columns; c++)
            {
                // Sorted as they are put in: a handful of values.
                for (int i = 0; i < fine; i++)
                {
                    byte value = pixels[(base0 + row + i) * columns + c];
                    int at = i;
                    for (; at > 0 && some[at - 1] > value; at--)
                        some[at] = some[at - 1];
                    some[at] = value;
                }
                fineMedians[row * columns + c] = some[(fine - 1) / 2];
            }
            for (int r = lowest; r <= highest; r++)
            {
                int over = (r - 1 - fine - base0) * columns,
                    under = (r + 2 - base0) * columns,
                    steps = 0;
                for (int c = 0; c < columns; c++)
                    if (Step(fineMedians[over + c], fineMedians[under + c]))
                        steps++;
                if (steps > most)
                    (from, to, most) = (r, r, steps);
                else if (steps == most && most > 0 && r == to + 1)
                    to = r;
            }
        }
        return first + (from + to) / 2;
    }

    // The scene shows below (brighter than DimFloor) and is DimFactor times as bright as above.
    private static bool Step(int above, int below) => below >= Tune.DimFloor && below >= Tune.DimFactor * Math.Max(above, 1);

    // One value more (by = 1) or less (by = −1) in a slot's histogram.
    private static void Put(int[] counts, Span<int> total, Span<int> median, Span<int> less, int slot, byte value, int by)
    {
        counts[slot * 256 + value] += by;
        total[slot] += by;
        if (value < median[slot])
            less[slot] += by;
    }

    // The median of a slot's values (the lower one of two middle values), moved on from where
    // it stood.
    private static int Middle(int[] counts, Span<int> total, Span<int> median, Span<int> less, int slot)
    {
        if (total[slot] == 0)
            return 0;
        int middle = (total[slot] - 1) / 2,
            at = median[slot],
            below = less[slot],
            from = slot * 256;
        while (below > middle)
            below -= counts[from + --at];
        while (below + counts[from + at] <= middle)
            below += counts[from + at++];
        median[slot] = at;
        less[slot] = below;
        return at;
    }

    // Bottom of a tooltip over a stash tab: the body is near black right inside its left
    // frame, and the first rows where that margin turns bright end it. The border-line estimate
    // stops at separator lines inside short tooltips. The title bar is skipped by its own
    // height (title: the matched piece, one line or two): a fixed height ends just below a
    // one-line bar, which a picture one pixel off no longer clears, and inside every two-line
    // title.
    private static int MarginBottom(Mat gray, int left, int top, int title, double popup)
    {
        int x0 = left + Px(Tune.MarginFrom, popup),
            x1 = Math.Min(gray.Width, left + Px(Tune.MarginTo, popup));
        if (x1 <= x0)
            return top;
        int bright = 0;
        for (int y = top + title; y < gray.Height; y++)
        {
            double sum = 0;
            for (int x = x0; x < x1; x++)
                sum += gray.At<byte>(y, x);
            bright = sum / (x1 - x0) > Tune.MarginBright ? bright + 1 : 0;
            if (bright == Tune.MarginRows)
                return y - Tune.MarginRows;
        }
        return gray.Height;
    }

    // Below the real bottom border the screen is no longer darkened: at least twice as
    // bright as just above. A bright overlay seen through the tooltip (an FPS graph) forms a
    // line too, but stays dark on both sides.
    private static bool Undimmed(Mat gray, ReadOnlySpan<int> columns, int y, double popup)
    {
        int aboveFrom = Px(Tune.UndimmedAboveFrom, popup),
            aboveTo = Px(Tune.UndimmedAboveTo, popup),
            belowFrom = Px(Tune.UndimmedBelowFrom, popup),
            belowTo = Px(Tune.UndimmedBelowTo, popup);
        if (y - aboveFrom < 0 || y + belowTo > gray.Height)
            return false;
        double below = Mean(gray, columns, y + belowFrom, y + belowTo),
            above = Mean(gray, columns, y - aboveFrom, y - aboveTo);
        return below >= Tune.UndimmedFactor * Math.Max(Tune.UndimmedFloor, above);
    }

    // A border over empty, black grid cells: dark above (the tooltip's body) and dark below.
    private static bool DarkOnBothSides(Mat gray, ReadOnlySpan<int> columns, int y, double popup)
    {
        int aboveFrom = Px(Tune.UndimmedAboveFrom, popup),
            aboveTo = Px(Tune.UndimmedAboveTo, popup),
            belowFrom = Px(Tune.UndimmedBelowFrom, popup),
            belowTo = Px(Tune.UndimmedBelowTo, popup);
        if (y - aboveFrom < 0 || y + belowTo > gray.Height)
            return false;
        return Mean(gray, columns, y - aboveFrom, y - aboveTo) <= Tune.DarkSides && Mean(gray, columns, y + belowFrom, y + belowTo) <= Tune.DarkSides;
    }

    private static double Mean(Mat gray, ReadOnlySpan<int> columns, int from, int to)
    {
        long sum = 0;
        for (int y = from; y < to; y++)
        foreach (int x in columns)
            sum += gray.At<byte>(y, x);
        return (double)sum / (columns.Length * (to - from));
    }

    private static List<(Point At, double Score)> Peaks(Mat gray, Rect area, Mat template, double threshold)
    {
        var result = new List<(Point, double)>();
        if (area.Width < template.Width || area.Height < template.Height)
            return result;
        using var roi = new Mat(gray, area);
        using var scores = new Mat();
        Cv2.MatchTemplate(roi, template, scores, TemplateMatchModes.CCoeffNormed);
        while (true)
        {
            Cv2.MinMaxLoc(scores, out _, out double max, out _, out Point at);
            if (max < threshold)
                break;
            result.Add((new Point(area.X + at.X, area.Y + at.Y), max));
            // Suppress this peak and its neighbourhood.
            var r = new Rect(
                Math.Max(0, at.X - template.Width),
                Math.Max(0, at.Y - template.Height),
                template.Width * 2,
                template.Height * 2
            );
            r = r.Intersect(new Rect(0, 0, scores.Width, scores.Height));
            using var taken = scores[r];
            taken.SetTo(Scalar.All(-1));
        }
        return result;
    }

    // Highest score of each template anywhere in the frame, for calibrating the thresholds.
    public Dictionary<string, double> BestScores(Mat bgr, double popup = GameConfig.StandardPopup)
    {
        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        var all = new Rect(0, 0, gray.Width, gray.Height);
        var best = new Dictionary<string, double>();
        foreach (var (style, left, _, _, _) in Caps(popup))
            best[style] = Max(gray, all, left);
        best["close"] = Max(gray, all, close);
        best["alt"] = Max(gray, all, hints[0]);
        best["shift"] = Max(gray, all, hints[1]);
        best["cancel"] = Max(gray, all, cancel);
        return best;
    }

    private static double Max(Mat gray, Rect area, Mat template)
    {
        using var roi = new Mat(gray, area);
        using var scores = new Mat();
        Cv2.MatchTemplate(roi, template, scores, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(scores, out _, out double max);
        return max;
    }

    public void Dispose()
    {
        foreach (var (_, left, leftCoarse, right, rightLower) in sized.Values.SelectMany(s => s))
        {
            left.Dispose();
            leftCoarse.Dispose();
            rightLower?.Dispose();
            right?.Dispose();
        }
        foreach (var (_, left, right, _) in art)
        {
            left.Dispose();
            right?.Dispose();
        }
        close.Dispose();
        foreach (var h in hints)
            h.Dispose();
        cancel.Dispose();
        cancelLeft.Dispose();
        cancelRight.Dispose();
    }
}

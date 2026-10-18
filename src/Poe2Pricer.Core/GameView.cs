using OpenCvSharp;

namespace Poe2Pricer.Core;

// How the game's picture is brought to the size every reader is measured at (1440 px high).
// The game lays its interface out by the height of its window alone (Exiled Exchange 2: "sidebar
// is 986px at Wx1600H"; measured at five window heights: brought to 1440, every window lies
// vertically at the same place within ±1 px). So the picture is resized once, here, and
// everything behind it keeps its measures; labels are drawn back at Scale. Wider than 2.4:1 the
// game leaves black bars left and right; only the game's part is read.
public sealed class GameView
{
    public const int ReferenceHeight = (int)Tuning.Screen.ReferenceHeight;

    // The game's part of the client area, in its own pixels.
    public Rect Game { get; }

    // Pixels of the game per pixel of the reference picture, as the fraction up / down.
    public double Scale => (double)up / down;

    // The reference picture's size.
    public Size Reference { get; }

    private readonly int up,
        down;

    private GameView(Rect game, Size reference, int up, int down)
    {
        Game = game;
        Reference = reference;
        this.up = up;
        this.down = down;
    }

    public static GameView Of(int width, int height)
    {
        // A height within 2 % of the reference is that height with a few pixels cut off (stored
        // pictures) or a window pulled a pixel too far, not another scale.
        int up = 1,
            down = 1;
        if (Math.Abs(height / (double)ReferenceHeight - 1) >= Tuning.Screen.SameScale)
        {
            int common = (int)System.Numerics.BigInteger.GreatestCommonDivisor(height, ReferenceHeight);
            (up, down) = (height / common, ReferenceHeight / common);
        }
        int shown = Math.Min(width, (int)Math.Round(height * Tuning.Screen.WidestAspect));
        int left = (width - shown) / 2;
        // Where parts of the picture are taken (Part: steps no longer than LongestStep), the
        // width is a whole number of steps, so that a part resizes exactly as the whole. What
        // does not fit is left off at the right; the left edge, where stash and remnant list
        // are anchored, stays. A window height that shares no handy divisor with the reference
        // has steps almost as long as the picture is high, and no parts are taken of it: there
        // the whole width is read (cut to whole steps, a window of 1920×1009 — windowed mode
        // with a title bar — would be left as a square, and most odd heights would lose more
        // than 2 % of their width).
        int reference;
        if (down <= Tuning.Screen.LongestStep)
        {
            shown -= shown % up;
            reference = shown / up * down;
        }
        else
            reference = (int)Math.Round(shown * (double)down / up);
        return new(new Rect(left, 0, shown, height), new Size(reference, up == down ? height : ReferenceHeight), up, down);
    }

    // The reference picture of a picture of the whole client area. area: only this part of the
    // reference picture is wanted; the rest stays black.
    public Mat Read(Mat client, Box? area = null)
    {
        var (from, to) = Part(area);
        using var part = new Mat(client, new Rect(Game.X + from.X, Game.Y + from.Y, from.Width, from.Height));
        return Placed(part, to);
    }

    // What to take from the game's picture for this part of the reference picture (from, in
    // the game's pixels, relative to Game) and where it lies in the reference picture (to).
    // The part is widened to whole steps of the scale plus a margin: then its pixels come out
    // exactly as they do when the whole picture is resized, which is what lets the glance (a
    // part) be compared with what a read (the whole) stored.
    public (Rect From, Rect To) Part(Box? area)
    {
        var whole = (new Rect(0, 0, Game.Width, Game.Height), new Rect(0, 0, Reference.Width, Reference.Height));
        if (area is null || down > Tuning.Screen.LongestStep)
            return whole;
        int margin = up == down ? 0 : (int)Math.Ceiling(Tuning.Screen.ResizeMargin / Scale / down) * down;
        int left = Math.Max(0, Floor(area.X - margin)),
            top = Math.Max(0, Floor(area.Y - margin)),
            right = Math.Min(Reference.Width, Ceiling(area.Right + margin)),
            bottom = Math.Min(Reference.Height, Ceiling(area.Bottom + margin));
        if (right <= left || bottom <= top)
            return whole;
        return (new Rect(left / down * up, top / down * up, (right - left) / down * up, (bottom - top) / down * up), new Rect(left, top, right - left, bottom - top));
    }

    private int Floor(int value) => (int)Math.Floor(value / (double)down) * down;

    private int Ceiling(int value) => (int)Math.Ceiling(value / (double)down) * down;

    // The taken part resized into its place in a black reference picture.
    public Mat Placed(Mat part, Rect to)
    {
        var reference = new Mat(Reference.Height, Reference.Width, MatType.CV_8UC3, Scalar.All(0));
        using var place = new Mat(reference, to);
        if (up == down)
            part.CopyTo(place);
        else
        {
            // Smaller: every pixel the mean of what it covers. Larger: smooth, the readers
            // compare pictures and read text.
            using var sized = new Mat();
            Cv2.Resize(part, sized, to.Size, 0, 0, up > down ? InterpolationFlags.Area : InterpolationFlags.Cubic);
            sized.CopyTo(place);
        }
        return reference;
    }

    // A point of the client area (the mouse pointer) on the reference picture; null outside the game.
    public (int X, int Y)? Point((int X, int Y)? client) =>
        client is { } p && Game.Contains(new Point(p.X, p.Y)) ? ((int)((p.X - Game.X) / Scale), (int)((p.Y - Game.Y) / Scale)) : null;
}

using System.Text.RegularExpressions;
using OpenCvSharp;
using Tune = Poe2Pricer.Core.Tuning.Remnants;

namespace Poe2Pricer.Core;

public sealed record RemnantWindow(UiMode Mode, Box List, int WindowRight, double AnchorScore);

// Picture: the row as it looks (RowPicture); null for rows made up in tests.
public sealed record RemnantRow(int Count, string Text, double CenterY, [property: System.Text.Json.Serialization.JsonIgnore] RowPicture? Picture = null);

// The reward list of a Verisium remnant ("Runeshape Combinations"): a window on the left, one
// reward per row, no item images. Found by a corner of its page (Anchor), read by Windows OCR
// over the full row width (long names run far left).
public sealed partial class RemnantReader : IDisposable
{
    private static readonly (UiMode Mode, int X, int Y, Box List, int Right)[] Layouts = Tune.Layouts;
    private const int SearchRadius = Tune.SearchRadius;

    private readonly Mat anchor;
    private readonly TitleReader ocr;

    public RemnantReader(string uiDirectory, TitleReader ocr)
    {
        this.ocr = ocr;
        anchor = Anchor.Load(uiDirectory, Tune.AnchorFile);
    }

    public RemnantWindow? Locate(Mat bgr)
    {
        RemnantWindow? best = null;
        foreach (var l in Layouts)
        {
            if (Anchor.InRow(bgr, anchor, l.Y, SearchRadius) is not { } found)
                continue;
            if (found.Score < Tune.MinimumAnchorScore || found.Score <= (best?.AnchorScore ?? 0))
                continue;
            int dx = found.At.X - l.X,
                dy = found.At.Y - l.Y;
            best = new(l.Mode, l.List with { X = l.List.X + dx, Y = l.List.Y + dy }, l.Right + dx, found.Score);
        }
        return best;
    }

    // The part of the picture that tells whether the list is still open and what changed: its
    // title, the list, and the band beside it where the game opens tooltips (to the full
    // height, as far right as a tooltip's inner margin is looked at).
    public Box LookArea(RemnantWindow window, int height, double popup = GameConfig.StandardPopup)
    {
        var area = window.List;
        foreach (var l in Layouts)
            if (l.Mode == window.Mode)
                area = area.Union(new Box(window.List.X - l.List.X + l.X - SearchRadius, window.List.Y - l.List.Y + l.Y - SearchRadius, anchor.Width + 2 * SearchRadius, anchor.Height + 2 * SearchRadius));
        int from = window.WindowRight - Band(Tune.TooltipBandLeft, popup),
            to = window.WindowRight + Band(Tune.TooltipBandRight, popup) + Band(Tuning.Occluders.BesideWidth, popup);
        return area.Union(new Box(from, 0, to - from, height));
    }

    // A measure of the tooltip band at the game's standard popup size, at this size.
    public static int Band(int standard, double popup) => GameConfig.Scaled(standard, popup);

    // The count is a number; the OCR reads some of its digits as the letters they resemble
    // ("lx", "IOX" for "10x").
    [GeneratedRegex(@"^\s*([0-9OoQDlIi|!ZzSsB]{1,3})\s*[xX×]\s+(.{3,})$")]
    private static partial Regex RowPattern();

    // A reward is the line that ends at the list's right edge: the game writes the name
    // right-aligned, the rune symbols stand on the left and read as noise ("ezeü"). Most rewards
    // are "Nx Name"; some stand without a count ("Unique Belt").
    public async Task<RemnantRow[]> ReadAsync(Mat bgr, RemnantWindow window)
    {
        var lines = await ocr.ReadRegionAsync(bgr, window.List.Rect());
        var rows = new List<RemnantRow>();
        foreach (var (text, y, _, right) in lines)
        {
            if (right < window.List.Right - Tune.RightAligned)
                continue;
            var m = RowPattern().Match(text);
            var digits = new string(m.Groups[1].Value.Select(Digit).ToArray());
            var picture = RowPicture.Of(bgr, window.List, y);
            if (m.Success && int.TryParse(digits, out int count) && count > 0)
                rows.Add(new(count, m.Groups[2].Value.Trim(), y, picture));
            else if (text.Count(char.IsLetter) >= 3)
                rows.Add(new(1, text.Trim(), y, picture));
        }
        return rows.OrderBy(r => r.CenterY).ToArray();
    }

    private static char Digit(char read) =>
        read switch
        {
            >= '0' and <= '9' => read,
            'O' or 'o' or 'Q' or 'D' => '0',
            'Z' or 'z' => '2',
            'S' or 's' => '5',
            'B' => '8',
            _ => '1',
        };

    // Coarse picture of the list; a scroll or a new remnant changes it.
    public static double[] Signature(Mat bgr, RemnantWindow window)
    {
        using var roi = new Mat(bgr, window.List.Rect().Intersect(new Rect(0, 0, bgr.Width, bgr.Height)));
        using var gray = new Mat();
        Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
        using var small = new Mat();
        Cv2.Resize(gray, small, new Size(Tune.SignatureWidth, Tune.SignatureHeight), 0, 0, InterpolationFlags.Area);
        var s = new double[Tune.SignatureWidth * Tune.SignatureHeight];
        for (int y = 0; y < Tune.SignatureHeight; y++)
        for (int x = 0; x < Tune.SignatureWidth; x++)
            s[y * Tune.SignatureWidth + x] = small.At<byte>(y, x);
        return s;
    }

    public static bool Changed(double[] a, double[] b) => a.Zip(b).Average(p => Math.Abs(p.First - p.Second)) > Tune.ChangedAbove;

    public void Dispose() => anchor.Dispose();
}

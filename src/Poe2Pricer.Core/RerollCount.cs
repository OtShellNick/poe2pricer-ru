using OpenCvSharp;
using Tune = Poe2Pricer.Core.Tuning.Favours;

namespace Poe2Pricer.Core;

// The number on the reroll button: rerolls left, one or more digits, "0" when none is left. It
// is not read, only compared: another number than at the last read is a new roll (the game's
// own sign of a roll). The digits are white with a black outline whether
// the button is red, greyed out or under the controller cursor; the die and the arrows around
// them are not, so the digits can be found the same way in every state. Found, not compared:
// the limits are fixed brightnesses, and a button that is fading under a dialog gives another
// mask for the same number (RerollMark).
public static class RerollCount
{
    public static Box Button(FavoursGrid grid)
    {
        double s = grid.CellSize / Tune.Cell;
        return new(
            grid.Bounds.X + (int)Math.Round(Tune.RerollLeft * s),
            grid.Bounds.Y + (int)Math.Round(Tune.RerollTop * s),
            (int)Math.Round(Tune.RerollWidth * s),
            (int)Math.Round(Tune.RerollHeight * s)
        );
    }

    // The digits as a mask over the button. Null when there are none (a dialog dims the
    // window: no white is left). Whether they are the button's digits and not letters of a
    // tooltip lying on it, the picture of the button tells (RerollMark).
    public static bool[]? Read(Mat bgr, FavoursGrid grid)
    {
        var button = Button(grid);
        if (button.X < 0 || button.Y < 0 || button.Right > bgr.Width || button.Bottom > bgr.Height)
            return null;
        using var crop = new Mat(bgr, button.Rect());
        using var white = new Mat();
        using var black = new Mat();
        Cv2.InRange(crop, Scalar.All(Tune.DigitWhite), Scalar.All(255), white);
        Cv2.InRange(crop, Scalar.All(0), Scalar.All(Tune.DigitBlack), black);
        using var labels = new Mat();
        int areas = Cv2.ConnectedComponents(white, labels, PixelConnectivity.Connectivity8, MatType.CV_32S);
        int width = crop.Cols,
            height = crop.Rows;
        var pixels = new List<int>[areas];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            if (labels.At<int>(y, x) is > 0 and var area)
                (pixels[area] ??= []).Add(y * width + x);

        var digits = new bool[width * height];
        bool any = false;
        for (int area = 1; area < areas; area++)
        {
            if (pixels[area] is not { Count: var size } own || size < Tune.DigitPixels)
                continue;
            // The border: every pixel next to the area that is not part of it.
            var border = new HashSet<int>();
            foreach (int p in own)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = p % width + dx,
                        y = p / width + dy;
                    if (x >= 0 && y >= 0 && x < width && y < height && labels.At<int>(y, x) != area)
                        border.Add(y * width + x);
                }
            int outlined = border.Count(p => black.At<byte>(p / width, p % width) != 0);
            if (border.Count == 0 || (double)outlined / border.Count < Tune.DigitOutline)
                continue;
            foreach (int p in own)
                digits[p] = true;
            any = true;
        }
        return any ? digits : null;
    }
}

// The reroll button as it was when its number was taken: its picture and where the number
// stood on it. Another number is another picture at that place — compared by structure, so the
// brightness does not count: cutting the digits out again with fixed brightness limits gives
// another mask for the same number while the "Offer Tribute" dialog fades in or out (the whole
// window gets darker step by step), and each step would count as a new roll. Measured
// (brightness/structure): the same number, red, grey, under the controller cursor or in defer
// mode .85–1.28/1.00, under the dialog .52–.64/.98, the picture evenly darkened: structure
// 1.00; another number: structure .21–.56.
//
// The rest of the button tells whether the button can be seen at all — under a tooltip its
// letters would pass for digits and every tooltip on the button count as a new roll. Measured
// against its picture: the same number .84–1.04/.92–1.00, after a roll .96–1.47/.92–1.00,
// under the dialog .51–.57/.99; under the body of a tooltip .17–.19, under its edge 1.00/.61.
public sealed record RerollMark(byte[] Pixels, int Width, Box Number)
{
    // Taken only where digits are found: a button that is covered or dimmed beyond that gives
    // no mark, and the number is taken later (FavoursGameWindow).
    public static RerollMark? Of(Mat bgr, FavoursGrid grid)
    {
        if (RerollCount.Read(bgr, grid) is not { } digits || Picture(bgr, grid) is not { } pixels)
            return null;
        int width = RerollCount.Button(grid).Width,
            height = pixels.Length / width;
        int left = width,
            top = height,
            right = -1,
            bottom = -1;
        for (int i = 0; i < digits.Length; i++)
        {
            if (!digits[i])
                continue;
            left = Math.Min(left, i % width);
            right = Math.Max(right, i % width);
            top = Math.Min(top, i / width);
            bottom = Math.Max(bottom, i / width);
        }
        left = Math.Max(0, left - Tune.NumberMargin);
        top = Math.Max(0, top - Tune.NumberMargin);
        right = Math.Min(width - 1, right + Tune.NumberMargin);
        bottom = Math.Min(height - 1, bottom + Tune.NumberMargin);
        return new(pixels, width, new(left, top, right - left + 1, bottom - top + 1));
    }

    // The button as it was read, for the evidence of a roll (FavoursGameWindow.Evidence).
    public Mat Picture()
    {
        var picture = new Mat(Pixels.Length / Width, Width, MatType.CV_8UC1);
        picture.SetArray(Pixels);
        return picture;
    }

    // True: another number stands on the button — a new roll. False: the same number, however
    // bright. Null: the button cannot be seen as it was (covered, off the picture), nothing is
    // decided.
    public bool? OtherNumber(Mat bgr, FavoursGrid grid)
    {
        if (Picture(bgr, grid) is not { } now || now.Length != Pixels.Length)
            return null;
        var (number, rest) = Looks(now);
        if (!rest.Same || !number.Visible)
            return null;
        return number.Structure < Tune.SameStructure;
    }

    // The place of the number and the button around it, each against its stored picture.
    private (CellLook Number, CellLook Around) Looks(byte[] now)
    {
        int inside = Number.Width * Number.Height;
        byte[] numberThen = new byte[inside],
            numberNow = new byte[inside],
            restThen = new byte[Pixels.Length - inside],
            restNow = new byte[Pixels.Length - inside];
        int n = 0,
            r = 0;
        for (int i = 0; i < Pixels.Length; i++)
        {
            int x = i % Width,
                y = i / Width;
            if (x >= Number.X && x < Number.Right && y >= Number.Y && y < Number.Bottom)
            {
                numberThen[n] = Pixels[i];
                numberNow[n++] = now[i];
            }
            else
            {
                restThen[r] = Pixels[i];
                restNow[r++] = now[i];
            }
        }
        return (CellLook.Between(numberThen, numberNow), CellLook.Between(restThen, restNow));
    }

    private static byte[]? Picture(Mat bgr, FavoursGrid grid)
    {
        var button = RerollCount.Button(grid);
        if (button.X < 0 || button.Y < 0 || button.Right > bgr.Width || button.Bottom > bgr.Height)
            return null;
        using var crop = new Mat(bgr, button.Rect());
        using var gray = new Mat();
        Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
        gray.GetArray(out byte[] pixels);
        return pixels;
    }
}

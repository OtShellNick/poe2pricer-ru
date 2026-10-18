using OpenCvSharp;
using Tune = Poe2Pricer.Core.Tuning.Remnants;

namespace Poe2Pricer.Core;

// A row of the remnant list as it looks: the strip of the picture around its name, over the
// list's whole width, in grey values. The OCR reads one and the same row differently from one
// scroll position to the next ("Skill: Grim Pillars", "Grim Pillars", "ESkill Level 20: …" —
// in the keyboard UI the rune symbols stand right before the name), so a row is known again by
// its picture, not by its text — by text, a scrolled list is taken for another list after a few
// steps. The same idea as the cell memory of the Favours window and the line pictures of the
// rumour panel.
public sealed record RowPicture(int Width, int Height, byte[] Gray)
{
    // null when the strip does not lie wholly on the picture.
    public static RowPicture? Of(Mat bgr, Box list, double centerY)
    {
        var strip = new Rect(list.X, (int)Math.Round(centerY) - Tune.RowBand, list.Width, 2 * Tune.RowBand);
        if (strip.X < 0 || strip.Y < 0 || strip.Right > bgr.Width || strip.Bottom > bgr.Height)
            return null;
        using var crop = new Mat(bgr, strip);
        using var gray = new Mat();
        Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
        gray.GetArray(out byte[] pixels);
        return new(strip.Width, strip.Height, pixels);
    }

    // The same row: its middle lies somewhere in the other strip, a few pixels up or down (the
    // centre of a line as the OCR gives it is not exact). Correlation of the grey values, so
    // the highlight of the selected row does not matter.
    public bool Same(RowPicture other) => Likeness(other) >= Tune.SameRow;

    public double Likeness(RowPicture other)
    {
        if (Width != other.Width || Height != other.Height)
            return 0;
        int shift = Tune.RowShift,
            h = Height - 2 * shift,
            n = Width * h;
        double sum = 0,
            squares = 0;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < Width; x++)
        {
            int v = Gray[(y + shift) * Width + x];
            sum += v;
            squares += v * v;
        }
        double spread = squares - sum * sum / n;
        if (spread <= 0)
            return 0;
        double best = 0;
        for (int dy = 0; dy <= 2 * shift; dy++)
        {
            double otherSum = 0,
                otherSquares = 0,
                both = 0;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < Width; x++)
            {
                int a = Gray[(y + shift) * Width + x],
                    b = other.Gray[(y + dy) * Width + x];
                otherSum += b;
                otherSquares += b * b;
                both += a * b;
            }
            double otherSpread = otherSquares - otherSum * otherSum / n;
            if (otherSpread > 0)
                best = Math.Max(best, (both - sum * otherSum / n) / Math.Sqrt(spread * otherSpread));
        }
        return best;
    }
}

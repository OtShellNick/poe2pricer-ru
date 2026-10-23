using OpenCvSharp;
using Tune = Poe2Pricer.Core.Tuning.Stash;

namespace Poe2Pricer.Core;

// Lettering the game draws on a stash slot: the stack number at the top left and the tier mark
// at the bottom right — neutral white glyphs with a black outline, the tier mark on a dark
// backing. Both grow with the UI Text Size, and the backing then reaches into the strip where
// the label goes (measured 22.10.2026 at Large and Extra Large). Found by their look, so no size
// table is needed: white pixels with black ones within reach, widened to take the outline and
// the backing with them — but only in the two corners the game writes in (Tune.NumberRight …):
// the glints of some art (a Divine Orb) look like lettering too, and in the middle of the slot
// they would mask the art itself.
public static class GameText
{
    // 255 where lettering and its surround lie in `bgr` (a slot), 0 elsewhere.
    public static Mat Surround(Mat bgr, int surround = Tune.TextSurround)
    {
        using var white = new Mat();
        using var black = new Mat();
        Cv2.InRange(bgr, Scalar.All(Tune.TextWhite), Scalar.All(255), white);
        Cv2.InRange(bgr, Scalar.All(0), Scalar.All(Tune.TextBlack), black);
        // Neutral: the brightest and the darkest channel no further apart than TextTint.
        var channels = bgr.Split();
        try
        {
            using var most = new Mat();
            using var least = new Mat();
            using var tint = new Mat();
            using var neutral = new Mat();
            Cv2.Max(channels[0], channels[1], most);
            Cv2.Max(most, channels[2], most);
            Cv2.Min(channels[0], channels[1], least);
            Cv2.Min(least, channels[2], least);
            Cv2.Subtract(most, least, tint);
            Cv2.Threshold(tint, neutral, Tune.TextTint, 255, ThresholdTypes.BinaryInv);
            Cv2.BitwiseAnd(white, neutral, white);
        }
        finally
        {
            foreach (var channel in channels)
                channel.Dispose();
        }
        // The corners: the number's at the top left, the mark's at the bottom right.
        using var corners = new Mat(bgr.Size(), MatType.CV_8UC1, Scalar.All(0));
        Cv2.Rectangle(corners, new Rect(0, 0, (int)(bgr.Width * Tune.NumberRight), (int)(bgr.Height * Tune.NumberBottom)), Scalar.All(255), -1);
        Cv2.Rectangle(corners, new Rect((int)(bgr.Width * Tune.MarkLeft), (int)(bgr.Height * Tune.MarkTop), bgr.Width - (int)(bgr.Width * Tune.MarkLeft), bgr.Height - (int)(bgr.Height * Tune.MarkTop)), Scalar.All(255), -1);
        Cv2.BitwiseAnd(white, corners, white);
        using var outline = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(2 * Tune.TextOutline + 1, 2 * Tune.TextOutline + 1));
        using var nearBlack = new Mat();
        Cv2.Dilate(black, nearBlack, outline);
        using var glyphs = new Mat();
        Cv2.BitwiseAnd(white, nearBlack, glyphs);
        using var wide = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2 * surround + 1, 2 * surround + 1));
        var mask = new Mat();
        Cv2.Dilate(glyphs, mask, wide);
        return mask;
    }
}

using OpenCvSharp;

namespace Poe2Pricer.Core;

// A piece of a window's artwork by which the window is found. The game places its windows by
// the height of the picture: on the reference picture (GameView) a window always lies at the
// same height, while how far left or right depends on the picture's width and on the UI mode
// (keyboard stash and remnant list at the left, the ritual in the middle; in the controller UI
// in the middle from 16:9 on and moved together at 16:10 — no formula covers that). So the
// anchor is looked for at its height, over the whole width.
// Artwork, not lettering: the game scales artwork with the picture, but sets text anew at every
// size (the remnant title is a third larger at 1080 in the controller UI; "Offer Tribute" fits
// 3 % larger at 1600), so a text template made at one size does not fit at another.
public static class Anchor
{
    // The best place of the template with its top at y ± radius; null when the band does not
    // lie on the picture.
    public static (double Score, Point At)? InRow(Mat bgr, Mat template, int y, int radius)
    {
        var band = new Rect(0, y - radius, bgr.Width, template.Height + 2 * radius).Intersect(new Rect(0, 0, bgr.Width, bgr.Height));
        if (band.Height < template.Height || band.Width < template.Width)
            return null;
        using var crop = new Mat(bgr, band);
        using var gray = new Mat();
        Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
        using var scores = new Mat();
        Cv2.MatchTemplate(gray, template, scores, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(scores, out _, out double score, out _, out Point at);
        return (score, new Point(at.X, band.Y + at.Y));
    }

    public static Mat Load(string uiDirectory, string file)
    {
        var template = Cv2.ImRead(Path.Combine(uiDirectory, file), ImreadModes.Grayscale);
        return template.Empty() ? throw Texts.Failure(m => new InvalidDataException(m), "MissingFile", file) : template;
    }
}

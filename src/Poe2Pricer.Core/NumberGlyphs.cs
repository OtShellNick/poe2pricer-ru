using System.Text.RegularExpressions;
using OpenCvSharp;
using Tune = Poe2Pricer.Core.Tuning.Glyphs;

namespace Poe2Pricer.Core;

// The number of a waystone or tablet mod line, read from the game's own glyphs where the OCR
// left it out. Windows' text recognition drops the number block of a line as a non-word now
// and then, above all the roll shown with Alt held ("32(30-40)%": 25 of 27 missing values on
// 30 pictures, 23.10.2026), and reads nothing when given the block alone, in any preparation.
// The block's place is known all the same: the mod's text says where the "#" stands between
// its words, and the OCR gives each word's box (Gap). In that gap the glyphs are compared with
// pictures of the game's digits, brackets, minus and per cent sign (data/ui/glyphs, cut from
// the author's diagnoses of 21.10.2026 at popup 1; the game draws them the same at every UI
// Text Size, measured on the Extra Large fixtures, and scaled with the popup size). A digit
// matches its picture at .91 to 1.00, a wrong one at .72 at most (Tune.Match); a letter "I"
// comes near a "1" (.86), which the shape of the whole block rules out (WellFormed).
public sealed partial class NumberGlyphs : IDisposable
{
    private readonly (string Char, Mat Picture)[] templates;
    private readonly Dictionary<double, (string Char, Mat Picture)[]> scaled = [];
    private readonly Lock scaling = new();

    // The files in data/ui/glyphs and the character each stands for.
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        ["0"] = "0", ["1"] = "1", ["2"] = "2", ["3"] = "3", ["4"] = "4", ["5"] = "5", ["6"] = "6", ["7"] = "7", ["8"] = "8", ["9"] = "9",
        ["paren-left"] = "(", ["paren-right"] = ")", ["minus"] = "-", ["percent"] = "%", ["plus"] = "+",
    };

    public NumberGlyphs(string uiDirectory)
    {
        templates = Names.Select(n => (n.Value, Cv2.ImRead(Path.Combine(uiDirectory, "glyphs", n.Key + ".png"), ImreadModes.Grayscale))).ToArray();
        foreach (var (name, picture) in templates)
            if (picture.Empty())
                throw new FileNotFoundException($"glyph picture missing: {name}", Path.Combine(uiDirectory, "glyphs"));
    }

    // Where a line's number lies, from the words the OCR read of it (picture pixels).
    // template: the mod's text in the line's language, "#" for the number. The gap reaches
    // Tune.Above/Below beyond the words' boxes: the brackets are taller than the digits.
    // The OCR left the number out as a rule, so the gap is between the words before and after
    // the "#" when the count of words fits; a word with a digit is the number itself (with
    // letters stuck to it, the block's width from its left); else the words nearest the ones
    // beside the "#" bound the gap, and at a line's start or end the block's width does.
    public static Rect? Gap(string template, IReadOnlyList<OcrWord> words, double popup, int pictureWidth, int pictureHeight)
    {
        if (words.Count == 0)
            return null;
        var parts = template.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int hash = Array.FindIndex(parts, p => p.Contains('#'));
        if (hash < 0)
            return null;
        int before = hash, after = parts.Length - hash - 1, n = words.Count;
        int top = words.Min(w => w.Box.Top), bottom = words.Max(w => w.Box.Bottom);
        double charWidth = words.Sum(w => w.Box.Width) / (double)Math.Max(1, words.Sum(w => w.Text.Length));
        int estimate = (int)Math.Round(charWidth * Tune.BlockChars);
        int left, right;
        int digitWord = -1;
        for (int i = 0; i < n && digitWord < 0; i++)
            if (words[i].Text.Any(char.IsDigit))
                digitWord = i;
        if (digitWord >= 0)
        {
            var box = words[digitWord].Box;
            left = box.Left;
            right = words[digitWord].Text.Count(char.IsLetter) >= Tune.StuckLetters ? box.Left + estimate : box.Right;
        }
        else if (n == before + after && before > 0 && after > 0)
        {
            left = words[before - 1].Box.Right;
            right = words[before].Box.Left;
        }
        else
        {
            int b = before > 0 ? Nearest(words, parts[before - 1]) : -1, a = after > 0 ? Nearest(words, parts[hash + 1]) : -1;
            if (b >= 0 && a >= 0 && a > b)
                (left, right) = (words[b].Box.Right, words[a].Box.Left);
            else if (b >= 0)
                (left, right) = (words[b].Box.Right, words[b].Box.Right + estimate);
            else if (a >= 0)
                (left, right) = (words[a].Box.Left - estimate, words[a].Box.Left);
            else if (before == 0)
                (left, right) = (words[0].Box.Left - estimate, words[0].Box.Left);
            else
                (left, right) = (words[^1].Box.Right, words[^1].Box.Right + estimate);
        }
        if (right - left < estimate / 3)
        {
            left -= estimate / 2;
            right += estimate / 2;
        }
        int side = (int)Math.Round(Tune.Beside * popup), above = (int)Math.Round(Tune.Above * popup);
        var gap = new Rect(left - side, top - above, right - left + 2 * side, bottom - top + 2 * above).Intersect(new Rect(0, 0, pictureWidth, pictureHeight));
        return gap.Width > 0 && gap.Height > 0 ? gap : null;
    }

    // The word nearest a word of the template, by its letters; -1: none near enough.
    private static int Nearest(IReadOnlyList<OcrWord> words, string want)
    {
        string key = Waystones.WaystoneMods.KeyOf(want);
        int best = -1, bestDistance = int.MaxValue;
        for (int i = 0; i < words.Count; i++)
        {
            int d = TitleReader.Distance(Waystones.WaystoneMods.KeyOf(words[i].Text), key);
            if (d < bestDistance && d <= Math.Max(1, key.Length / Tune.NearPart))
                (best, bestDistance) = (i, d);
        }
        return best;
    }

    // The block in the gap as text ("32(30-40)%", "22%"), or null when what matched is not
    // the shape of one. popup: the game's popup size, which the glyphs grow with.
    public string? Read(Mat bgr, Rect gap, double popup)
    {
        using var crop = new Mat(bgr, gap);
        using var kept = TitleReader.Undimmed(crop, gray: true);
        var hits = new List<(int X, int Width, double Score, string Char)>();
        foreach (var (ch, picture) in At(popup))
        {
            if (picture.Width > kept.Width || picture.Height > kept.Height)
                continue;
            using var result = new Mat();
            Cv2.MatchTemplate(kept, picture, result, TemplateMatchModes.CCoeffNormed);
            for (int y = 0; y < result.Rows; y++)
                for (int x = 0; x < result.Cols; x++)
                {
                    float score = result.At<float>(y, x);
                    if (score >= Tune.Match && Peak(result, x, y, score))
                        hits.Add((x, picture.Width, score, ch));
                }
        }
        // The best match wins where two overlap (a "1" where the ")" is).
        var kept2 = new List<(int X, int Width, double Score, string Char)>();
        foreach (var hit in hits.OrderByDescending(h => h.Score))
            if (!kept2.Any(k => Math.Min(k.X + k.Width, hit.X + hit.Width) - Math.Max(k.X, hit.X) > Tune.Overlap * Math.Min(k.Width, hit.Width)))
                kept2.Add(hit);
        string text = string.Concat(kept2.OrderBy(k => k.X).Select(k => k.Char));
        return WellFormed(text) ? text : null;
    }

    // A number block as the game writes it: the value, with Alt its roll, a per cent sign.
    public static bool WellFormed(string text) => Block().IsMatch(text);

    private static bool Peak(Mat result, int x, int y, float score)
    {
        for (int dy = -Tune.PeakReach; dy <= Tune.PeakReach; dy++)
            for (int dx = -Tune.PeakReach; dx <= Tune.PeakReach; dx++)
            {
                int yy = y + dy, xx = x + dx;
                if (yy >= 0 && xx >= 0 && yy < result.Rows && xx < result.Cols && result.At<float>(yy, xx) > score)
                    return false;
            }
        return true;
    }

    private (string Char, Mat Picture)[] At(double popup)
    {
        if (Math.Abs(popup - 1) < 0.005)
            return templates;
        lock (scaling)
        {
            if (!scaled.TryGetValue(popup, out var pictures))
            {
                pictures = templates.Select(t =>
                {
                    var picture = new Mat();
                    Cv2.Resize(t.Picture, picture, new Size(), popup, popup, InterpolationFlags.Cubic);
                    return (t.Char, picture);
                }).ToArray();
                scaled[popup] = pictures;
            }
            return pictures;
        }
    }

    public void Dispose()
    {
        foreach (var (_, picture) in templates)
            picture.Dispose();
        lock (scaling)
            foreach (var pictures in scaled.Values)
                foreach (var (_, picture) in pictures)
                    picture.Dispose();
    }

    [GeneratedRegex(@"^[+-]?\d{1,3}(\(\d{1,3}-\d{1,3}\))?%?$")]
    private static partial Regex Block();
}

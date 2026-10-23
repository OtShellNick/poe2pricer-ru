using OpenCvSharp;
using Poe2Pricer.Core.Market;
using static System.FormattableString;
using Tune = Poe2Pricer.Core.Tuning.StashNames;

namespace Poe2Pricer.Core;

// Symbol comparison for filled stash slots against the poe.ninja art (IconStore):
// - a slot named by the table is checked once: an icon far off the cell means the table is
//   wrong there, and a wrong price is worse than none;
// - a slot without a name (free slots, items not settled from their silhouette) is named by
//   the best icon of the tab's categories (free slots: all), only when clearly ahead.
// Score: colour template correlation, minus the difference in mean colour under the icon —
// the Delirium bottles share one shape and differ only in colour.
// Tiered currency shares its art, so it never gets far enough ahead; it stays unnamed.
public sealed class StashIdentifier
{
    private readonly IconStore icons;

    public StashIdentifier(IconStore icons)
    {
        this.icons = icons;
        // Art that arrives later (the fetch failed at the first start and succeeds at the
        // next opening of the stash): what was told without it does not hold, or a slot with
        // several possible names would stay on the first of them, and a free one without a
        // name, for the rest of the run.
        icons.Arrived += () =>
        {
            lock (sync)
                known.Clear();
        };
    }

    // How a slot got its name, as the diagnostics say it: from the table of the tab alone.
    // Everything else names the comparison and its numbers, always with a decimal point.
    public const string ByTable = "table";

    // Only a gross mismatch rejects a table name; a free or unnamed slot is named only with
    // AcceptLead ahead (Mind was taken for Thawing with a lead of .15). Tuning.StashNames.
    public double RejectBelow { get; init; } = Tune.RejectBelow;
    public double AcceptScore { get; init; } = Tune.AcceptScore;
    public double AcceptLead { get; init; } = Tune.AcceptLead;

    private static readonly Vec3b Ground = Tune.Ground;
    private static readonly double[] Sizes = Tune.Sizes;
    private const int Shift = Tune.Shift;

    private sealed record Picture(Mat Bgr, Mat Mask, float[] Thumb);

    // While item art is still being fetched (first start, new items), a slot that needs it is
    // not named: "?" instead of a guess, and nothing about it is remembered. Art poe.ninja
    // does not have at all does not count as missing.
    public const string Pending = "icons loading";

    private readonly Dictionary<(string, int), Picture?> arts = [];
    private static readonly Mat Edge = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(2 * Tune.ArtEdge + 1, 2 * Tune.ArtEdge + 1));
    private readonly Dictionary<string, (string? Name, string By)> known = [];
    private readonly object sync = new();
    private PriceBook? knownFor; // the price book `known` was read with

    public StashSlot[] Identify(Mat bgr, StashTabWindow window, StashSlot[] slots, PriceBook book, IReadOnlyList<string> categories)
    {
        // What was read with one price book does not hold for the next: another league may
        // carry what this one did not, and a slot remembered as "not known" would stay without
        // a name. It also keeps the memory from growing for ever.
        // A book without any prices (the diagnosis before they are loaded) is not "the next".
        if (book.Images.Count > 0)
            lock (sync)
                if (!ReferenceEquals(book, knownFor))
                {
                    known.Clear();
                    knownFor = book;
                }
        var result = new StashSlot[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            // A covered slot shows a darkened picture: nothing can be told from its symbol, and
            // it gets no label anyway.
            if (!slot.Present || slot.Covered)
            {
                result[i] = slot;
                continue;
            }
            var key = Key(bgr, window, slot);
            (string? Name, string By) found;
            lock (sync)
            {
                if (!known.TryGetValue(key, out found))
                {
                    bool Waiting(string name) => book.Images.ContainsKey(name) && !icons.Has(name) && !icons.Tried(name);
                    bool waiting;
                    if (slot.Alternatives is { Length: > 1 } alternatives)
                    {
                        waiting = alternatives.Any(Waiting);
                        found = waiting ? (null, Pending) : Choose(bgr, slot, alternatives);
                    }
                    else if (slot.Name is { } name)
                    {
                        // Named by the table: shown as such until its art is there to check it.
                        waiting = Waiting(name);
                        found = waiting ? (name, ByTable) : Check(bgr, slot, name);
                    }
                    else
                    {
                        var candidates = slot.Free ? null : categories;
                        waiting = book.Images.Any(p => (candidates is null || candidates.Contains(p.Value.Category)) && Waiting(p.Key));
                        found = waiting ? (null, Pending) : Name(bgr, slot, book, candidates);
                    }
                    // Not remembered either: what was read without any prices (a diagnosis
                    // before they are loaded reads with an empty book) — "no icons" would stay
                    // for the rest of the run.
                    // Nor what a look still running with the price book before wrote after
                    // the memory was emptied for the next one (a look and a diagnosis run side
                    // by side).
                    if (!waiting && book.Images.Count > 0 && ReferenceEquals(book, knownFor))
                        known[key] = found;
                }
            }
            result[i] = slot with { Name = found.Name, NamedBy = found.By };
        }
        return result;
    }

    // Slot content as a small picture: the same stack in the same slot is compared once.
    private static string Key(Mat bgr, StashTabWindow window, StashSlot slot)
    {
        using var roi = new Mat(bgr, slot.Bounds.Rect().Intersect(new Rect(0, 0, bgr.Width, bgr.Height)));
        using var small = new Mat();
        Cv2.Resize(roi, small, new Size(6, 6), 0, 0, InterpolationFlags.Area);
        small.GetArray(out Vec3b[] px);
        return $"{window.Kind}/{slot.Index}/{slot.Name}/" + string.Concat(px.Select(p => $"{p.Item0 / 16:x}{p.Item1 / 16:x}{p.Item2 / 16:x}"));
    }

    private (string? Name, string By) Check(Mat bgr, StashSlot slot, string name)
    {
        if (Art(name, Size(slot, Tune.Size)) is null)
            return (name, ByTable); // no poe.ninja art (e.g. Preserved Vertebrae): trust the table
        double score = Score(bgr, slot, name);
        return score >= RejectBelow ? (name, ByTable) : (null, Invariant($"table rejected ({name} {score:0.00})"));
    }

    // A slot for one of several items: the best-scoring art among them; unknown art counts as
    // a candidate only when no other has art (then the first listed name).
    private (string? Name, string By) Choose(Mat bgr, StashSlot slot, string[] alternatives)
    {
        var scored = alternatives.Where(n => Art(n, Size(slot, Tune.Size)) is not null).Select(n => (Name: n, Score: Score(bgr, slot, n))).OrderByDescending(p => p.Score).ToArray();
        if (scored.Length == 0)
            return (alternatives[0], ByTable);
        return scored[0].Score >= RejectBelow
            ? (scored[0].Name, Invariant($"choice {scored[0].Score:0.00}"))
            : (null, Invariant($"choice rejected ({scored[0].Name} {scored[0].Score:0.00})"));
    }

    // Also used by the command line to calibrate on slots the table already names.
    public (string? Name, string By) Name(Mat bgr, StashSlot slot, PriceBook book, IReadOnlyList<string>? categories)
    {
        var names = book.Images.Where(p => categories is null || categories.Contains(p.Value.Category)).Select(p => p.Key).Where(icons.Has).ToArray();
        if (names.Length == 0)
            return (null, "no icons");
        // Cheap preselection on 12x12 thumbnails, then the full comparison.
        var thumb = Thumb(bgr, slot);
        var pre = names
            .Select(n => (Name: n, Art: Art(n, Size(slot, Tune.Size))))
            .Where(p => p.Art is not null)
            .Select(p => (p.Name, Score: Dot(p.Art!.Thumb, thumb)))
            .OrderByDescending(p => p.Score)
            .Take(Tune.PreselectCount)
            .Select(p => (p.Name, Score: Score(bgr, slot, p.Name)))
            .OrderByDescending(p => p.Score)
            .ToArray();
        if (pre.Length == 0)
            return (null, "no icons");
        double lead = pre[0].Score - (pre.Length > 1 ? pre[1].Score : 0);
        return pre[0].Score >= AcceptScore && lead >= AcceptLead
            ? (pre[0].Name, Invariant($"icon {pre[0].Score:0.00}, margin {lead:0.00}"))
            : (null, Invariant($"uncertain: {pre[0].Name} {pre[0].Score:0.00}, margin {lead:0.00}"));
    }

    private static int Size(StashSlot slot, double f) => (int)(Math.Min(slot.Bounds.Width, slot.Bounds.Height) * f);

    // Best over the sizes and ±Shift px: correlation minus the weighted colour difference. The
    // game's lettering on the slot (stack number, tier mark; GameText) is no part of the art
    // and is left out of both — it grows with the UI Text Size and hid enough of the art to
    // fail the check (measured 22.10.2026: Vaal Orb .40 / .35 / .14 at medium / Large / Extra
    // Large with the lettering in, .60–.67 without; Breach Splinter .31 / .06 → .76 / .69).
    public double Score(Mat bgr, StashSlot slot, string name)
    {
        double best = double.NegativeInfinity;
        int cx = slot.Bounds.X + slot.Bounds.Width / 2,
            cy = slot.Bounds.Y + slot.Bounds.Height / 2;
        var picture = new Rect(0, 0, bgr.Width, bgr.Height);
        var box = slot.Bounds.Rect();
        if (!picture.Contains(box))
            return best;
        using var slotRoi = new Mat(bgr, box);
        using var lettering = GameText.Surround(slotRoi);
        foreach (var f in Sizes)
        {
            int s = Size(slot, f);
            if (Art(name, s) is not { } art)
                continue;
            var area = new Rect(cx - s / 2 - Shift, cy - s / 2 - Shift, s + 2 * Shift, s + 2 * Shift);
            if (!picture.Contains(area))
                continue;
            // The lettering at the art's place in the slot (the ±Shift px lie within its surround).
            var place = new Rect(cx - s / 2 - box.X, cy - s / 2 - box.Y, s, s);
            if (place.X < 0 || place.Y < 0 || place.Right > box.Width || place.Bottom > box.Height)
                continue;
            using var letteringAt = new Mat(lettering, place);
            using var keep = new Mat();
            Cv2.BitwiseNot(letteringAt, keep);
            using var both = new Mat();
            Cv2.BitwiseAnd(art.Mask, keep, both);
            // Art that looks like lettering itself keeps the whole comparison (Tune.MinimumArtShare).
            bool lettered = Cv2.CountNonZero(both) >= Cv2.CountNonZero(art.Mask) * Tune.MinimumArtShare;
            using var roi = new Mat(bgr, area);
            using var scores = new Mat();
            // Compared within the art and its edge then (Tune.ArtEdge), not the ground around.
            if (lettered)
            {
                using var edged = new Mat();
                Cv2.Dilate(art.Mask, edged, Edge);
                using var compared = new Mat();
                Cv2.BitwiseAnd(edged, keep, compared);
                Cv2.MatchTemplate(roi, art.Bgr, scores, TemplateMatchModes.CCoeffNormed, compared);
            }
            else
                Cv2.MatchTemplate(roi, art.Bgr, scores, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(scores, out _, out double corr, out _, out Point at);
            using var cell = new Mat(roi, new Rect(at.X, at.Y, s, s));
            var within = lettered ? both : art.Mask;
            var colour = MeanColour(cell, within);
            var artColour = MeanColour(art.Bgr, within);
            double diff = Math.Abs(colour[0] - artColour[0]) + Math.Abs(colour[1] - artColour[1]) + Math.Abs(colour[2] - artColour[2]);
            best = Math.Max(best, corr - Tune.ColourWeight * diff);
        }
        return best;
    }

    private Picture? Art(string name, int size)
    {
        lock (arts)
        {
            if (arts.TryGetValue((name, size), out var cached))
                return cached;
            Picture? art = null;
            if (icons.Get(name) is { } icon)
            {
                using var scaled = new Mat();
                Cv2.Resize(icon, scaled, new Size(size, size), 0, 0, InterpolationFlags.Area);
                var bgrArt = new Mat(size, size, MatType.CV_8UC3);
                var mask = new Mat(size, size, MatType.CV_8UC1);
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = scaled.At<Vec4b>(y, x);
                    double a = p.Item3 / 255.0;
                    bgrArt.Set(y, x, new Vec3b(
                        (byte)(p.Item0 * a + Ground.Item0 * (1 - a)),
                        (byte)(p.Item1 * a + Ground.Item1 * (1 - a)),
                        (byte)(p.Item2 * a + Ground.Item2 * (1 - a))));
                    mask.Set(y, x, (byte)(p.Item3 > Tune.AlphaVisible ? 255 : 0));
                }
                art = new(bgrArt, mask, ThumbOf(bgrArt));
            }
            if (art is not null)
                arts[(name, size)] = art; // missing art may still arrive
            return art;
        }
    }

    // Mean colour under the mask, as shares of its sum (brightness left out).
    private static double[] MeanColour(Mat bgr, Mat mask)
    {
        var m = Cv2.Mean(bgr, mask);
        double sum = m.Val0 + m.Val1 + m.Val2 + 1e-6;
        return [m.Val0 / sum, m.Val1 / sum, m.Val2 / sum];
    }

    private static float[] Thumb(Mat bgr, StashSlot slot)
    {
        int s = Size(slot, Tune.Size),
            cx = slot.Bounds.X + slot.Bounds.Width / 2,
            cy = slot.Bounds.Y + slot.Bounds.Height / 2;
        using var roi = new Mat(bgr, new Rect(cx - s / 2, cy - s / 2, s, s).Intersect(new Rect(0, 0, bgr.Width, bgr.Height)));
        return ThumbOf(roi);
    }

    // 12x12 colour thumbnail, mean-free and of unit length.
    private static float[] ThumbOf(Mat bgr)
    {
        using var small = new Mat();
        Cv2.Resize(bgr, small, new Size(12, 12), 0, 0, InterpolationFlags.Area);
        small.GetArray(out Vec3b[] px);
        var v = px.SelectMany(p => new float[] { p.Item0, p.Item1, p.Item2 }).ToArray();
        float mean = v.Average();
        for (int i = 0; i < v.Length; i++)
            v[i] -= mean;
        float norm = MathF.Sqrt(v.Sum(x => x * x)) + 1e-6f;
        for (int i = 0; i < v.Length; i++)
            v[i] /= norm;
        return v;
    }

    private static double Dot(float[] a, float[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++)
            s += a[i] * b[i];
        return s;
    }
}

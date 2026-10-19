using OpenCvSharp;
using Tune = Poe2Pricer.Core.Tuning.Islands;

namespace Poe2Pricer.Core;

// Which uncharted island the rumour panel stands at, told from the map around it, so that the
// rumours of several openings of the same panel can be collected (moving an item in the
// inventory rolls the three shown rumours anew, and the panel closes and opens again each
// time). The panel itself says nothing about the island: in the controller UI the game centres
// the chosen island and the panel stands in the middle of the screen for every island; with
// keyboard and mouse the panel stands where the island is, but the player can drag and zoom
// the map between two openings.
//
// What stays is the sea chart: the dotted routes between the islands and the glowing waypoints
// on them, seen from the ship marker of the chosen island. The marker is found next to the
// panel (Marker), the dots and glows around it make the print (Print), and two prints are the
// same island when their points fall on each other at some scale (Match). Colours of the
// waypoints are state (green: done, blue: reachable) and change as the player plays; only the
// places count.
public static class IslandMarks
{
    // The ship marker of the chosen island: white sails in a dark square, below the panel
    // (centred under it), or beside its right or left edge when the panel had no room above the
    // island (keyboard UI, panel at the top of the screen; then the marker can stand anywhere
    // along the panel's height). Searched in one window around the panel's lower part: at every
    // place the white fill of an inner box the size of the sails and the dark fill of the ring
    // around it, by window sums; the best place wins. Null when no place looks like it (the
    // panel's picture was read from an earlier frame, the marker is off-screen).
    public static Point? Marker(Mat bgr, Box panel)
    {
        int x0 = Math.Max(0, panel.X - Tune.MarkerBeside),
            x1 = Math.Min(bgr.Width, panel.Right + Tune.MarkerBeside),
            y0 = Math.Max(0, panel.Y - Tune.MarkerAbove),
            y1 = Math.Min(bgr.Height, panel.Bottom + Tune.MarkerBelow);
        if (x1 - x0 < Tune.MarkerOuter || y1 - y0 < Tune.MarkerOuter)
            return null;
        using var window = new Mat(bgr, new Rect(x0, y0, x1 - x0, y1 - y0));
        using var gray = new Mat();
        using var hsv = new Mat();
        Cv2.CvtColor(window, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.CvtColor(window, hsv, ColorConversionCodes.BGR2HSV);
        using var saturation = hsv.ExtractChannel(1);
        using var white = new Mat();
        using var pale = new Mat();
        using var dark = new Mat();
        Cv2.InRange(gray, Scalar.All(Tune.SailWhite), Scalar.All(255), white);
        Cv2.InRange(saturation, Scalar.All(0), Scalar.All(Tune.SailGrey), pale);
        Cv2.BitwiseAnd(white, pale, white);
        Cv2.InRange(gray, Scalar.All(0), Scalar.All(Tune.SquareDark), dark);
        var whiteSum = Integral(white);
        var darkSum = Integral(dark);

        int inner = Tune.MarkerInner,
            outer = Tune.MarkerOuter,
            innerArea = inner * inner,
            ringArea = outer * outer - innerArea,
            margin = (outer - inner) / 2;
        double best = 0;
        Point? found = null;
        for (int y = 0; y + outer <= y1 - y0; y++)
        for (int x = 0; x + outer <= x1 - x0; x++)
        {
            int whiteInner = Sum(whiteSum, x + margin, y + margin, inner, inner),
                whiteOuter = Sum(whiteSum, x, y, outer, outer),
                darkOuter = Sum(darkSum, x, y, outer, outer),
                darkInner = Sum(darkSum, x + margin, y + margin, inner, inner);
            double sails = whiteInner / (double)innerArea,
                ringDark = (darkOuter - darkInner) / (double)ringArea,
                ringWhite = (whiteOuter - whiteInner) / (double)ringArea;
            if (sails < Tune.SailsAtLeast || sails > Tune.SailsAtMost || ringDark < Tune.RingDarkAtLeast || ringWhite > Tune.RingWhiteAtMost)
                continue;
            double score = sails + ringDark;
            if (score > best)
            {
                best = score;
                found = new Point(x + margin, y + margin);
            }
        }
        if (found is not { } at)
            return null;
        // The place: the centre of the sails themselves (always the same picture), not of the
        // window — the window's best place slips by up to a dozen pixels with the gold frame
        // the controller UI draws around the marker.
        long sumX = 0,
            sumY = 0,
            count = 0;
        for (int y = at.Y; y < at.Y + inner; y++)
        for (int x = at.X; x < at.X + inner; x++)
            if (white.At<byte>(y, x) != 0)
            {
                sumX += x;
                sumY += y;
                count++;
            }
        return count == 0 ? null : new Point(x0 + (int)Math.Round(sumX / (double)count), y0 + (int)Math.Round(sumY / (double)count));
    }

    // The sea chart around the marker: the dots of the routes and the glows of the waypoints,
    // as offsets from the marker. Hidden: where nothing is read — the game's own bars at the
    // edges and the panel — so that a point another opening has there is not held against
    // this one.
    public static IslandPrint Print(Mat bgr, Box panel, Point marker)
    {
        var hidden = new[]
        {
            new Box(0, 0, bgr.Width, Tune.HudTop),
            new Box(0, Tune.HudBottom, bgr.Width, bgr.Height - Tune.HudBottom),
            new Box(0, 0, Tune.HudLeft, bgr.Height),
            new Box(bgr.Width - Tune.HudRight, 0, Tune.HudRight, bgr.Height),
            new Box(panel.X - Tune.PanelMargin, panel.Y - Tune.PanelMargin, panel.Width + 2 * Tune.PanelMargin, panel.Height + Tune.PanelMargin + Tune.PanelMarginBelow),
        };
        var print = new IslandPrint(marker, [], hidden, new Size(bgr.Width, bgr.Height));
        using var hsv = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
        var points = new List<Point2d>();
        foreach (var (x, y) in Dots(hsv, print).Concat(Glows(hsv, print)))
            points.Add(new Point2d(x - marker.X, y - marker.Y));
        return print with { Points = [.. points] };
    }

    // The dots of the dotted routes: small yellow blobs on dark water. Foliage glints on the
    // islands have the very same colour, so a dot counts only when the ring around it holds
    // neither green nor yellow, and only with another dot a dot's spacing away.
    private static IEnumerable<(double X, double Y)> Dots(Mat hsv, IslandPrint print)
    {
        using var yellow = new Mat();
        using var green = new Mat();
        Cv2.InRange(hsv, new Scalar(Tune.DotHueFrom, Tune.DotSaturation, Tune.DotValue), new Scalar(Tune.DotHueTo, 255, 255), yellow);
        Cv2.InRange(hsv, new Scalar(Tune.LeafHueFrom, Tune.LeafSaturation, Tune.LeafValue), new Scalar(Tune.LeafHueTo, 255, 255), green);
        var candidates = new List<(double X, double Y)>();
        foreach (var (x, y, width, height, area) in Components(yellow, Tune.DotAreaFrom, Tune.DotAreaTo))
        {
            if (width > Tune.DotSize || height > Tune.DotSize || area < Tune.DotFill * width * height)
                continue;
            if (!print.Visible(x, y) || x < Tune.RingTo + 1 || y < Tune.RingTo + 1 || x >= hsv.Width - Tune.RingTo - 1 || y >= hsv.Height - Tune.RingTo - 1)
                continue;
            int ring = 0,
                leafy = 0,
                yellowish = 0;
            int cx = (int)Math.Round(x),
                cy = (int)Math.Round(y);
            for (int dy = -Tune.RingTo; dy <= Tune.RingTo; dy++)
            for (int dx = -Tune.RingTo; dx <= Tune.RingTo; dx++)
            {
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d < Tune.RingFrom || d > Tune.RingTo)
                    continue;
                ring++;
                if (green.At<byte>(cy + dy, cx + dx) != 0)
                    leafy++;
                if (yellow.At<byte>(cy + dy, cx + dx) != 0)
                    yellowish++;
            }
            if (leafy < Tune.RingForeign * ring && yellowish < Tune.RingForeign * ring)
                candidates.Add((x, y));
        }
        foreach (var (x, y) in candidates)
        {
            bool chained = candidates.Any(o =>
            {
                double d = Math.Sqrt((o.X - x) * (o.X - x) + (o.Y - y) * (o.Y - y));
                return d >= Tune.DotSpacingFrom && d <= Tune.DotSpacingTo;
            });
            if (chained)
                yield return (x, y);
        }
    }

    // The glows of the waypoints: saturated, bright, roundish blobs. The water around is dark
    // and the routes' dots are too small.
    private static IEnumerable<(double X, double Y)> Glows(Mat hsv, IslandPrint print)
    {
        using var glowing = new Mat();
        Cv2.InRange(hsv, new Scalar(0, Tune.GlowSaturation, Tune.GlowValue), new Scalar(180, 255, 255), glowing);
        foreach (var (x, y, width, height, area) in Components(glowing, Tune.GlowAreaFrom, Tune.GlowAreaTo))
        {
            double aspect = width / (double)height;
            if (width < Tune.GlowSizeAtLeast || aspect < Tune.GlowAspectFrom || aspect > Tune.GlowAspectTo || area < Tune.GlowFill * width * height)
                continue;
            if (print.Visible(x, y))
                yield return (x, y);
        }
    }

    // Two prints of the same island: at some scale (the map zoomed between the openings), most
    // of the points one print has where the other can see fall on a point of the other, both
    // ways round. Score: the smaller of the two fractions; Evidence: the smaller number of
    // points looked at — few points in common (the map dragged far) say little either way.
    public static (double Score, int Evidence, double Scale) Match(IslandPrint a, IslandPrint b)
    {
        (double Score, int Evidence, double Scale) best = (0, 0, 1);
        double from = Math.Log(Tune.ScaleFrom),
            to = Math.Log(Tune.ScaleTo);
        for (int i = 0; i < Tune.ScaleSteps; i++)
        {
            double scale = Math.Exp(from + (to - from) * i / (Tune.ScaleSteps - 1));
            var (seenA, hitA) = Side(a, b, scale);
            var (seenB, hitB) = Side(b, a, 1 / scale);
            if (seenA == 0 || seenB == 0)
                continue;
            double score = Math.Min(hitA / (double)seenA, hitB / (double)seenB);
            int evidence = Math.Min(seenA, seenB);
            if (score > best.Score || (score == best.Score && evidence > best.Evidence))
                best = (score, evidence, scale);
        }
        return best;
    }

    // A's points brought to B's scale: how many lie where B can see, and how many of those
    // have a point of B within reach.
    private static (int Seen, int Hit) Side(IslandPrint a, IslandPrint b, double scale)
    {
        int seen = 0,
            hit = 0;
        double reach = Tune.Reach * scale;
        foreach (var p in a.Points)
        {
            double x = p.X * scale,
                y = p.Y * scale;
            if (!b.Visible(b.Marker.X + x, b.Marker.Y + y))
                continue;
            seen++;
            foreach (var q in b.Points)
                if (Math.Abs(q.X - x) <= reach && Math.Abs(q.Y - y) <= reach && (q.X - x) * (q.X - x) + (q.Y - y) * (q.Y - y) <= reach * reach)
                {
                    hit++;
                    break;
                }
        }
        return (seen, hit);
    }

    // Centre, size and area of every blob of the mask with an area in the range.
    private static IEnumerable<(double X, double Y, int Width, int Height, int Area)> Components(Mat mask, int areaFrom, int areaTo)
    {
        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        int count = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids, PixelConnectivity.Connectivity4, MatType.CV_32S);
        for (int i = 1; i < count; i++)
        {
            int area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
            if (area < areaFrom || area > areaTo)
                continue;
            yield return (
                centroids.At<double>(i, 0),
                centroids.At<double>(i, 1),
                stats.At<int>(i, (int)ConnectedComponentsTypes.Width),
                stats.At<int>(i, (int)ConnectedComponentsTypes.Height),
                area
            );
        }
    }

    // Window sums of a mask: the count of set pixels in any rectangle from four lookups.
    private static int[,] Integral(Mat mask)
    {
        using var ones = new Mat();
        Cv2.Threshold(mask, ones, 0, 1, ThresholdTypes.Binary);
        using var sums = new Mat();
        Cv2.Integral(ones, sums, MatType.CV_32S);
        sums.GetRectangularArray(out int[,] table);
        return table;
    }

    private static int Sum(int[,] table, int x, int y, int width, int height) => table[y + height, x + width] - table[y, x + width] - table[y + height, x] + table[y, x];
}

// The sea chart around an island's marker (IslandMarks.Print): points as offsets from the
// marker, the places of the picture where nothing was read, and the picture's size.
public sealed record IslandPrint(Point Marker, Point2d[] Points, Box[] Hidden, Size Picture)
{
    // The place (in the picture's pixels) lies where this print could see.
    public bool Visible(double x, double y)
    {
        int px = (int)Math.Round(x),
            py = (int)Math.Round(y);
        if (px < 0 || py < 0 || px >= Picture.Width || py >= Picture.Height)
            return false;
        foreach (var box in Hidden)
            if (box.Holds(px, py))
                return false;
        return true;
    }
}

using System.Text.Json.Serialization;
using OpenCvSharp;
using Tune = Poe2Pricer.Core.Tuning.Rumours;

namespace Poe2Pricer.Core;

// One handwritten line of the rumour panel as its ink: the dark pixels inside their bounding box.
public sealed record LineShape(int Width, int Height, bool[] Ink)
{
    // Share of the ink that two lines have in common (intersection over union), at the best
    // of a few pixels' shift. Measured: the same line .86–1.00 (at another place in the panel
    // too), different lines .14–.27.
    public static double Overlap(LineShape a, LineShape b)
    {
        double best = 0;
        for (int dy = -Tune.ShapeShift; dy <= Tune.ShapeShift; dy++)
        for (int dx = -Tune.ShapeShift; dx <= Tune.ShapeShift; dx++)
        {
            int both = 0,
                either = 0;
            for (int y = Math.Min(0, dy); y < Math.Max(a.Height, b.Height + dy); y++)
            for (int x = Math.Min(0, dx); x < Math.Max(a.Width, b.Width + dx); x++)
            {
                bool inA = x >= 0 && y >= 0 && x < a.Width && y < a.Height && a.Ink[y * a.Width + x];
                int bx = x - dx,
                    by = y - dy;
                bool inB = bx >= 0 && by >= 0 && bx < b.Width && by < b.Height && b.Ink[by * b.Width + bx];
                if (inA && inB)
                    both++;
                if (inA || inB)
                    either++;
            }
            if (either > 0)
                best = Math.Max(best, (double)both / either);
        }
        return best;
    }
}

// The pictures of rumour lines. The panel's handwriting is beyond the OCR (every way of
// reading garbles it differently, and a line may fit none of them); its picture is not: the
// game draws the same rumour the same way every time. Two kinds of pictures name a line:
// - drawn: every rumour text of the game in the game's hand (RumourLineArt) — there from the
//   start. The German lines are drawn at every run with the built-in Caveat Bold; the English
//   ones come as pictures shipped with the program (ShippedLines), drawn once on the
//   development machine, because their font may not be shipped;
// - seen: the line as the game showed it, remembered once it was named — a closer fit than
//   any drawing. Kept in the user's folder, never shipped; file null: for this run only (tests).
// The same idea as the cell memory of the Favours window.
public sealed class RumourShapes
{
    private sealed record Stored(string Name, int Width, int Height, string Ink);

    // The English lines as shipped (data/rumours/en-lines.json): every text drawn with the
    // game's font at the usual size and at Tune.DrawnOtherSizes, by Poe2Pricer.Cli rumour-lines
    // on the development machine. The font itself, Quikhand, is freeware that may not be
    // published, so what ships are pictures of the texts, not the font.
    private sealed record DrawnLine(string Text, double Size, int Width, int Height, string Ink);

    public static string ShippedLines => Path.Combine(AppContext.BaseDirectory, "data", "rumours", "en-lines.json");

    public static (string Text, double Size, LineShape Shape)[] ReadDrawn(string file) =>
        JsonFiles.Read<List<DrawnLine>>(file).Select(l => (l.Text, l.Size, new LineShape(l.Width, l.Height, Unpack(Convert.FromBase64String(l.Ink), l.Width * l.Height)))).ToArray();

    public static void WriteDrawn(string file, IEnumerable<(string Text, double Size, LineShape Shape)> lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        JsonFiles.Write(file, lines.Select(l => new DrawnLine(l.Text, l.Size, l.Shape.Width, l.Shape.Height, Convert.ToBase64String(Pack(l.Shape.Ink)))).ToList());
    }

    // Sets the shipped English lines as the drawn ones; returns how many texts they cover at
    // the usual size. A missing or unreadable file leaves no English line drawn: the lines are
    // then named by the OCR and learned, as before the drawings existed.
    public int LoadDrawn(string file)
    {
        (string Text, double Size, LineShape Shape)[] lines;
        try
        {
            lines = File.Exists(file) ? ReadDrawn(file) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException or InvalidDataException or ArgumentException or OverflowException or NullReferenceException)
        {
            lines = [];
        }
        SetDrawn(lines.Where(l => l.Size == 1).Select(l => (l.Text, l.Shape)), lines.Where(l => l.Size != 1).Select(l => (l.Text, l.Shape)));
        return lines.Where(l => l.Size == 1).Select(l => l.Text).Distinct().Count();
    }

    private readonly string? file;
    private readonly Lock gate = new();
    private List<(string Name, LineShape Shape)> known = [];
    private (string Name, LineShape Shape)[] drawn = [];
    private (string Name, LineShape Shape)[] drawnOtherSizes = [];

    // The same for the other languages of the game, by language (GameLanguage); each line
    // under the English text it stands for.
    private Dictionary<string, ((string Name, LineShape Shape)[] Usual, (string Name, LineShape Shape)[] Others)> drawnBy = [];

    private RumourShapes(string? file) => this.file = file;

    public static RumourShapes InMemory() => new(null);

    public static RumourShapes Load(string file)
    {
        var shapes = new RumourShapes(file);
        if (!File.Exists(file))
            return shapes;
        try
        {
            shapes.known = JsonFiles
                .Read<List<Stored>>(file)
                .Select(s => (s.Name, new LineShape(s.Width, s.Height, Unpack(Convert.FromBase64String(s.Ink), s.Width * s.Height))))
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException or InvalidDataException or ArgumentException or OverflowException or NullReferenceException)
        {
            // An unreadable file only means the lines are learned again. That holds for one
            // with entries that are no line pictures too (null, a negative width); they must
            // not end the program at the start.
            shapes.known = [];
        }
        return shapes;
    }

    public int Count
    {
        get
        {
            lock (gate)
                return known.Count;
        }
    }

    public int Drawn => Volatile.Read(ref drawn).Length;

    // The drawn lines, each under the text it shows; replaces those set before. otherSizes:
    // the same texts drawn a little larger and smaller. At another window height the game sets
    // the handwriting anew, and on the picture brought to reference size it comes out a few
    // percent off (measured: against the drawing in one size the right line overlaps as
    // little as .46, against the best of five sizes .64 as at the original size; a wrong line
    // at most .31).
    public void SetDrawn(IEnumerable<(string Text, LineShape Shape)> lines, IEnumerable<(string Text, LineShape Shape)>? otherSizes = null)
    {
        Volatile.Write(ref drawn, lines.ToArray());
        Volatile.Write(ref drawnOtherSizes, (otherSizes ?? []).ToArray());
    }

    // The drawn lines of another language of the game, each under the English text it stands
    // for (the rumour table knows a rumour by that); replaces those set for it before.
    public void SetDrawn(string language, IEnumerable<(string English, LineShape Shape)> lines, IEnumerable<(string English, LineShape Shape)>? otherSizes = null)
    {
        if (language == GameLanguage.English)
        {
            SetDrawn(lines, otherSizes);
            return;
        }
        var next = new Dictionary<string, ((string, LineShape)[], (string, LineShape)[])>(Volatile.Read(ref drawnBy)) { [language] = (lines.ToArray(), (otherSizes ?? []).ToArray()) };
        Volatile.Write(ref drawnBy, next);
    }

    // Draws the texts of a language of the game in its hand, at every size the hand needs
    // (RumourLineArt.SizesOf) and those again at the other window heights; returns how many
    // texts could be drawn.
    public int Draw(string language)
    {
        if (language == GameLanguage.English)
            return LoadDrawn(ShippedLines);
        var hand = RumourLineArt.HandOf(language);
        var texts = RumourTexts.In(language);
        var shown = texts.Select(t => t.Shown).ToArray();
        IEnumerable<(string, LineShape)> Drawn(double size) => texts.Zip(RumourLineArt.Draw(shown, hand, size)).Where(l => l.Second is not null).Select(l => (l.First.English, l.Second!));
        var sizes = RumourLineArt.SizesOf(hand);
        var usual = sizes.SelectMany(Drawn).ToArray();
        SetDrawn(language, usual, Tune.DrawnOtherSizes.SelectMany(step => sizes.SelectMany(size => Drawn(size * step))));
        return usual.Select(l => l.Item1).Distinct().Count();
    }

    // The rumour this line shows: the best overlap, clearly ahead of every other rumour.
    // nameOf: the rumour a stored label stands for (a drawn line is stored under the game's
    // text, a seen one under the name in the rumour table); null: unknown to the table.
    // language: the language the client runs in; its drawn lines are the ones compared with.
    public string? Match(LineShape shape, Func<string, string?> nameOf, string language = GameLanguage.English)
    {
        var (ofLanguage, others) =
            language == GameLanguage.English ? (Volatile.Read(ref drawn), Volatile.Read(ref drawnOtherSizes))
            : Volatile.Read(ref drawnBy).TryGetValue(language, out var set) ? set
            : ([], []);
        (string Name, LineShape Shape)[] usual;
        lock (gate)
            usual = [.. known, .. ofLanguage];
        // The other sizes only when the usual pictures name nothing: at 1440 px they never do.
        return Named(shape, usual, nameOf) ?? (others.Length > 0 ? Named(shape, [.. usual, .. others], nameOf) : null);
    }

    private static string? Named(LineShape shape, (string Name, LineShape Shape)[] all, Func<string, string?> nameOf)
    {
        var ranked = all
            .Select(k => (Name: nameOf(k.Name), Overlap: LineShape.Overlap(shape, k.Shape)))
            .Where(k => k.Name is not null)
            .GroupBy(k => k.Name!)
            .Select(g => (Name: g.Key, Overlap: g.Max(k => k.Overlap)))
            .OrderByDescending(k => k.Overlap)
            .Take(2)
            .ToArray();
        if (ranked.Length == 0 || ranked[0].Overlap < Tune.SameShape)
            return null;
        return ranked.Length > 1 && ranked[0].Overlap - ranked[1].Overlap < Tune.ShapeLead ? null : ranked[0].Name;
    }

    // Remembers the line as the game showed it under this name, unless that picture is there
    // already (under whatever name).
    public bool Learn(string name, LineShape shape)
    {
        lock (gate)
        {
            if (known.Any(k => LineShape.Overlap(shape, k.Shape) >= Tune.SamePicture))
                return false;
            known.Add((name, shape));
            if (file is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                JsonFiles.Write(file, known.Select(k => new Stored(k.Name, k.Shape.Width, k.Shape.Height, Convert.ToBase64String(Pack(k.Shape.Ink)))).ToList());
            }
            return true;
        }
    }

    private static byte[] Pack(bool[] ink)
    {
        var bytes = new byte[(ink.Length + 7) / 8];
        for (int i = 0; i < ink.Length; i++)
            if (ink[i])
                bytes[i / 8] |= (byte)(1 << (i % 8));
        return bytes;
    }

    private static bool[] Unpack(byte[] bytes, int length)
    {
        if (bytes.Length < (length + 7) / 8)
            throw Texts.Failure(m => new InvalidDataException(m), "RumourLinesDamaged");
        var ink = new bool[length];
        for (int i = 0; i < length; i++)
            ink[i] = (bytes[i / 8] & (1 << (i % 8))) != 0;
        return ink;
    }

    // The lines on the panel, top to bottom: ink is what is darker than half the parchment
    // (its median brightness; Tinte 20–60, Pergament 227–236), inside the parchment only (the
    // frame is as dark as ink), cut into lines where rows without ink lie between.
    public static (double CenterY, LineShape Shape)[] Cut(Mat bgr, RumourPanel panel)
    {
        var area = new Rect(panel.Rows.X + Tune.InkInset, panel.Rows.Y, panel.Rows.Width - 2 * Tune.InkInset, Tune.InkHeight).Intersect(new Rect(0, 0, bgr.Width, bgr.Height));
        if (area.Width <= Tune.MinimumLineWidth || area.Height <= Tune.MinimumLineHeight)
            return [];
        using var crop = new Mat(bgr, area);
        using var gray = new Mat();
        Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
        gray.GetArray(out byte[] pixels);
        var sorted = (byte[])pixels.Clone();
        Array.Sort(sorted);
        double below = sorted[sorted.Length / 2] * Tune.InkBelow;
        int w = area.Width,
            h = area.Height;
        var ink = new bool[w * h];
        var perRow = new int[h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            if (pixels[y * w + x] < below)
            {
                ink[y * w + x] = true;
                perRow[y]++;
            }

        var lines = new List<(double, LineShape)>();
        for (int y = 0; y < h; y++)
        {
            if (perRow[y] == 0)
                continue;
            int top = y,
                empty = 0;
            // A line ends where LineGap rows in a row hold no ink (the dots of "..." and of an
            // "i" leave single empty rows inside a line).
            for (; y < h && empty < Tune.LineGap; y++)
                empty = perRow[y] == 0 ? empty + 1 : 0;
            int bottom = y - empty;
            int left = w,
                right = -1;
            for (int yy = top; yy < bottom; yy++)
            for (int x = 0; x < w; x++)
                if (ink[yy * w + x])
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                }
            int width = right - left + 1,
                height = bottom - top;
            if (height < Tune.MinimumLineHeight || width < Tune.MinimumLineWidth)
                continue;
            var shape = new bool[width * height];
            for (int yy = 0; yy < height; yy++)
                Array.Copy(ink, (top + yy) * w + left, shape, yy * width, width);
            lines.Add((area.Y + (top + bottom) / 2.0, new LineShape(width, height, shape)));
        }
        return lines.ToArray();
    }
}

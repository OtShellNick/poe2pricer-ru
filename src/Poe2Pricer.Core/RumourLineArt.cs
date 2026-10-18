using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tune = Poe2Pricer.Core.Tuning.Rumours;

namespace Poe2Pricer.Core;

// The hand a client writes the rumour lines in.
public enum RumourHand
{
    // The English client: Quikhand, letter beside letter with the font's kerning pairs.
    Quikhand,

    // The German client: Caveat Bold. A handwriting that sets the same letter in another shape
    // depending on its neighbours (contextual alternates) — letter beside letter the drawing
    // runs out of step along the line (measured: the first third of a line overlaps .60, the
    // last .14). So it is set by WPF's text formatter, which applies the font's rules as the
    // game does: the right line then overlaps .56–.77 instead of .45–.71.
    CaveatBold,
}

// Draws a rumour line as the game does: the panel's handwriting is the font Quikhand, version
// 1.00 (the file the game carries, art/2dart/fonts/quikhand.ttf, is byte for byte the freeware
// release by Joanne Taylor; not shipped, see Draw). With the game's texts (RumourTexts) every
// line has its picture from the start, without OCR. Measured against lines cut from the game:
// the right line overlaps .62–.76, the best wrong one .31 (game against game .86–1.00 against
// .27 — the game draws a little thinner). Version 1.10 of the font is spaced differently and
// does not fit (.35).
// The German client writes in Caveat Bold (SIL OFL; data/fonts/Caveat-Bold.ttf is version 2.000
// from github.com/googlefonts/caveat — the game carries 1.500, which the project no longer
// publishes; against the game's lines both versions measure the same).
public static class RumourLineArt
{
    // The hand of a client language.
    public static RumourHand HandOf(string language) => language == GameLanguage.German ? RumourHand.CaveatBold : RumourHand.Quikhand;

    // The sizes a hand is drawn at for a picture of 1440 px height, against its usual size;
    // the first is the usual one.
    public static double[] SizesOf(RumourHand hand) => hand == RumourHand.CaveatBold ? Tune.CaveatSizes.Select(s => s / Tune.CaveatSizes[0]).ToArray() : [1];

    // One shape per text in this hand; null where the font lacks a letter.
    // size: against the hand's usual size at 1440 px height.
    // quikhandFile: the font file for Quikhand (development machine only, see Draw below).
    public static LineShape?[] Draw(IReadOnlyList<string> texts, RumourHand hand, double size = 1, string? quikhandFile = null)
    {
        if (hand == RumourHand.Quikhand)
            return Draw(texts, quikhandFile ?? throw Texts.Failure(m => new InvalidDataException(m), "FontMissing"), size);
        return OnDrawingThread(() =>
        {
            var face = Caveat();
            return texts.Select(t => Set(face, t, Tune.CaveatSizes[0] * size)).ToArray();
        });
    }

    // One shape per text; null where the font lacks a letter.
    // size: against the size the game writes at 1440 px height (Tune.DrawnSize).
    // fontFile: Quikhand 1.00. It is not built in and not shipped: freeware whose terms allow
    // use but not publishing it. It lies on the development machine (ref/fonts, outside the
    // repository); the program itself never draws English
    // lines but loads the pictures drawn here once (Poe2Pricer.Cli rumour-lines,
    // RumourShapes.ShippedLines). The German hand, Caveat Bold, is free (SIL OFL) and built in.
    public static LineShape?[] Draw(IReadOnlyList<string> texts, string fontFile, double size = 1)
    {
        return OnDrawingThread(() =>
        {
            var font = Font.Load(fontFile);
            return texts.Select(t => Draw(font, t, Tune.DrawnSize * size)).ToArray();
        });
    }

    // WPF renders on a single-threaded apartment only.
    private static LineShape?[] OnDrawingThread(Func<LineShape?[]> draw)
    {
        LineShape?[] shapes = [];
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                shapes = draw();
            }
            catch (Exception e)
            {
                failure = e;
            }
            finally
            {
                // WPF gave this thread a dispatcher with the first thing drawn; it is ended
                // with the thread instead of being left behind.
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return failure is null ? shapes : throw Texts.Failure(m => new InvalidDataException(m, failure), "CannotDrawRumourLines", failure.Message);
    }

    // A line set by WPF's text formatter (kerning, ligatures and contextual alternates as the
    // font asks for them); null where the font lacks a letter.
    private static LineShape? Set(Typeface face, string text, double size)
    {
        if (!face.TryGetGlyphTypeface(out var glyphs) || text.Any(c => !glyphs.CharacterToGlyphMap.ContainsKey(c)))
            return null;
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.GetCultureInfo("de-DE"), FlowDirection.LeftToRight, face, size, Brushes.Black, 1.0);
        var line = formatted.BuildGeometry(new Point(Tune.DrawnMargin, Tune.DrawnMargin));
        int width = (int)Math.Ceiling(formatted.WidthIncludingTrailingWhitespace) + 3 * Tune.DrawnMargin,
            height = (int)Math.Ceiling(formatted.Height) + 3 * Tune.DrawnMargin;
        return Ink(line, width, height);
    }

    // The built-in Caveat Bold as WPF reads it: from a file, written out once per user like
    // Quikhand (Font.Load).
    private static Typeface Caveat()
    {
        string file = Written("Caveat-Bold.ttf", "Caveat-Bold-2.000.ttf");
        return new Typeface(new FontFamily(new Uri(Path.GetDirectoryName(file)! + Path.DirectorySeparatorChar), "./#Caveat"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    }

    // WPF reads a font only from a file: the built-in bytes are written out once per user,
    // and again whenever what lies there is not exactly them.
    private static string Written(string resource, string name) => Written(resource, name, out _);

    private static string Written(string resource, string name, out byte[] bytes)
    {
        using var stream = typeof(RumourLineArt).Assembly.GetManifestResourceStream(resource) ?? throw Texts.Failure(m => new InvalidDataException(m), "FontMissing");
        bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        var file = Path.Combine(Path.GetTempPath(), "poe2pricer", name);
        if (!File.Exists(file) || !File.ReadAllBytes(file).AsSpan().SequenceEqual(bytes))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, bytes);
        }
        return file;
    }

    private static LineShape? Draw(Font font, string text, double size)
    {
        double x = Tune.DrawnMargin,
            baseline = Tune.DrawnMargin + size;
        var line = new GeometryGroup { FillRule = FillRule.Nonzero };
        ushort? before = null;
        foreach (char c in text)
        {
            if (!font.Face.CharacterToGlyphMap.TryGetValue(c, out ushort glyph))
                return null;
            if (before is { } left)
                x += font.Kerning.GetValueOrDefault((left, glyph)) * size / font.UnitsPerEm;
            var outline = font.Face.GetGlyphOutline(glyph, size, size).Clone();
            outline.Transform = new TranslateTransform(x, baseline);
            line.Children.Add(outline);
            x += font.Face.AdvanceWidths[glyph] * size;
            before = glyph;
        }
        int width = (int)Math.Ceiling(x) + Tune.DrawnMargin,
            height = (int)Math.Ceiling(size * 2) + 2 * Tune.DrawnMargin;
        return Ink(line, width, height);
    }

    // The outlines filled black on white, as ink inside its bounding box; null: no ink.
    private static LineShape? Ink(Geometry line, int width, int height)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            context.DrawGeometry(Brushes.Black, null, line);
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);

        int leftEdge = width,
            rightEdge = -1,
            top = height,
            bottom = -1;
        var ink = new bool[width * height];
        for (int y = 0; y < height; y++)
        for (int px = 0; px < width; px++)
            // Blue, green and red are equal: black ink on white.
            if (pixels[(y * width + px) * 4] < Tune.DrawnInk)
            {
                ink[y * width + px] = true;
                leftEdge = Math.Min(leftEdge, px);
                rightEdge = Math.Max(rightEdge, px);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        if (rightEdge < 0)
            return null;
        int w = rightEdge - leftEdge + 1,
            h = bottom - top + 1;
        var shape = new bool[w * h];
        for (int y = 0; y < h; y++)
            Array.Copy(ink, (top + y) * width + leftEdge, shape, y * w, w);
        return new LineShape(w, h, shape);
    }

    // The font's outlines and advances (WPF) and its kerning pairs, which WPF does not apply:
    // the old "kern" table, read from the file (format 0: glyph pairs with a shift in font units).
    private sealed record Font(GlyphTypeface Face, int UnitsPerEm, Dictionary<(ushort, ushort), short> Kerning)
    {
        public static Font Load(string file)
        {
            var bytes = File.ReadAllBytes(file);
            int U16(int at) => bytes[at] << 8 | bytes[at + 1];
            int tables = U16(4),
                unitsPerEm = 1000;
            var kerning = new Dictionary<(ushort, ushort), short>();
            for (int i = 0; i < tables; i++)
            {
                int record = 12 + 16 * i;
                string tag = System.Text.Encoding.ASCII.GetString(bytes, record, 4);
                int offset = U16(record + 8) << 16 | U16(record + 10);
                if (tag == "head")
                    unitsPerEm = U16(offset + 18);
                if (tag != "kern" || U16(offset) != 0)
                    continue;
                int subtables = U16(offset + 2),
                    at = offset + 4;
                for (int s = 0; s < subtables; s++)
                {
                    int length = U16(at + 2),
                        coverage = U16(at + 4);
                    // Format 0, horizontal.
                    if (coverage >> 8 == 0 && (coverage & 1) == 1)
                    {
                        int pairs = U16(at + 6);
                        for (int p = 0; p < pairs; p++)
                        {
                            int pair = at + 14 + 6 * p;
                            kerning[((ushort)U16(pair), (ushort)U16(pair + 2))] = (short)U16(pair + 4);
                        }
                    }
                    at += length;
                }
            }
            return new(new GlyphTypeface(new Uri(Path.GetFullPath(file))), unitsPerEm, kerning);
        }
    }
}

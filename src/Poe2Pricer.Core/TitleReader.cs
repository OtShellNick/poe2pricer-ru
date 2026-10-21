using OpenCvSharp;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using Tune = Poe2Pricer.Core.Tuning.Titles;

namespace Poe2Pricer.Core;

// Reads the title bar of a tooltip with Windows OCR. Uniques and rares show their base type
// as a second line, and overlays drawn on top (an FPS graph) add lines of their own, so all
// lines are returned and matched against the catalog.
public sealed class TitleReader
{
    private readonly OcrEngine engine;

    // Windows' recognizer takes one picture at a time ("Another RecognizeAsync operation is
    // already running!"): a diagnosis reading every window while a follow runs would collide.
    private readonly SemaphoreSlim one = new(1, 1);

    // Windows has the text recognition of this language installed.
    public static bool Available(string language) => OcrEngine.IsLanguageSupported(new Language(language));

    public TitleReader(string language = "en-US")
    {
        engine =
            OcrEngine.TryCreateFromLanguage(new Language(language))
            ?? throw Texts.Failure(m => new InvalidOperationException(m), "OcrMissing", language, string.Join(", ", OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag)));
    }

    // popup: the size the game draws its popups at (GameConfig.PopupSize); the title bar grows
    // with it, and the titles read right at every popup size.
    public async Task<string[]> ReadAsync(Mat bgr, Occluder tooltip, double popup = GameConfig.StandardPopup)
    {
        if (!tooltip.Kind.StartsWith("tooltip-"))
            return [];
        bool twoLines = tooltip.Kind is "tooltip-rare" or "tooltip-unique";
        var b = tooltip.Bounds;
        int cap = (int)Math.Round(Tune.CapWidth * popup),
            height = (int)Math.Round((twoLines ? Tune.TwoLines : Tune.OneLine) * popup);
        var area = new Rect(b.X + cap, b.Y, b.Width - 2 * cap, height)
            .Intersect(new Rect(0, 0, bgr.Width, bgr.Height));
        if (area.Width < Tune.MinimumWidth || area.Height < Tune.MinimumHeight)
            return [];
        using var crop = new Mat(bgr, area);
        using var large = new Mat();
        Cv2.Resize(crop, large, new Size(), Tune.Enlarge, Tune.Enlarge, InterpolationFlags.Cubic);
        return await RecognizeAsync(large);
    }

    // Any screen region, enlarged 2× before OCR; returns lines with their vertical centre and
    // where they begin and end (screen coordinates).
    public async Task<(string Text, double CenterY, double Left, double Right)[]> ReadRegionAsync(Mat bgr, Rect region, double scale = 2, bool binarize = false)
    {
        // What of the region lies in the picture; the lines are placed from there, so that a
        // region cut at the left or upper edge places its lines right. A region outside the
        // picture has no lines.
        var area = region.Intersect(new Rect(0, 0, bgr.Width, bgr.Height));
        if (area.Width <= 0 || area.Height <= 0)
            return [];
        using var crop = new Mat(bgr, area);
        using var large = new Mat();
        Cv2.Resize(crop, large, new Size(), scale, scale, InterpolationFlags.Cubic);
        if (binarize)
        {
            using var gray = new Mat();
            Cv2.CvtColor(large, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.Threshold(gray, gray, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            Cv2.CvtColor(gray, large, ColorConversionCodes.GRAY2BGR);
        }
        var result = await TextAsync(large);
        return result
            .Lines.Select(l =>
                (
                    l.Text,
                    area.Y + l.Words.Average(w => w.BoundingRect.Y + w.BoundingRect.Height / 2) / scale,
                    area.X + l.Words.Min(w => w.BoundingRect.X) / scale,
                    area.X + l.Words.Max(w => w.BoundingRect.X + w.BoundingRect.Width) / scale
                )
            )
            .ToArray();
    }

    private async Task<string[]> RecognizeAsync(Mat image) => (await TextAsync(image)).Lines.Select(l => l.Text).ToArray();

    // The picture as Windows' text recognition takes it: by way of a PNG in memory.
    private async Task<OcrResult> TextAsync(Mat image)
    {
        Cv2.ImEncode(".png", image, out byte[] bytes);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        await one.WaitAsync();
        try
        {
            return await engine.RecognizeAsync(bitmap);
        }
        finally
        {
            one.Release();
        }
    }

    // language: the language the client runs in; the title was read in that language and is
    // compared with what the game calls the candidates there (GameNames).
    // Catalog entry whose English name is closest to one of the OCR lines, restricted to the
    // given candidates when the symbol already narrowed it down (e.g. a greyed-out omen
    // group). Only close matches count: a rare's own name ("Blight Tread") lies 6 edits
    // from the nearest catalog name.
    public static (CatalogItem Item, int Distance)? Match(IEnumerable<string> lines, IEnumerable<CatalogItem> candidates, string language = GameLanguage.English)
    {
        var keys = lines.Select(Key).Where(k => k.Length >= Tune.MinimumKeyLength).ToArray();
        if (keys.Length == 0)
            return null;
        var best = candidates
            .Select(c => (Item: c, Name: Key(GameNames.In(language, c.NameEn))))
            .Select(c => (c.Item, c.Name, Distance: keys.Min(k => Distance(k, c.Name))))
            .MinBy(c => c.Distance);
        return best.Item is not null && best.Distance <= Math.Max(1, best.Name.Length / Tune.DistancePerLength)
            ? (best.Item, best.Distance)
            : null;
    }

    public static string Key(string s) => new(s.ToLowerInvariant().Where(char.IsLetter).ToArray());

    // Edit distance; also behind the price names (PriceBook).
    public static int Distance(string a, string b)
    {
        var d = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
            d[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            int prev = d[0];
            d[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int old = d[j];
                d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                prev = old;
            }
        }
        return d[b.Length];
    }
}

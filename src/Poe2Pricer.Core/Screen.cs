using System.Runtime.InteropServices;

namespace Poe2Pricer.Core;

// The game screen as the overlay control sees it: is the game in front, and a picture of it.
// Adapters: WindowsGameScreen (the real game) and, in tests, a sequence of PNG files.
public interface IGameScreen
{
    // Cheap (two system calls), any thread. Our own windows count as the game: showing the
    // overlay must not make the next check hide it again.
    bool InFront { get; }

    // The game window itself is in front, not one of ours. Only then may a picture be saved
    // (the diagnosis): the capture is the game's place on the screen, and with our settings
    // window in front that place shows whatever else lies there.
    bool GameInFront => InFront;

    // Off the UI thread. null: game not found or capture failed. The caller disposes the frame.
    // The picture comes at reference size (GameView). area: only this part of it is needed;
    // the frame keeps its size and is black outside of it (the glance: a part is taken in a
    // third of the time).
    GameCapture.Frame? Capture(Box? area = null);

    // The game's window as it is on the screen, for the diagnosis. The caller disposes it.
    GameCapture.Shot? Snapshot();
}

// The Path of Exile 2 window, looked for every 5 s (the game may start or restart). Every
// picture carries the popup size from the game's settings file, as it stands at that moment.
public sealed class WindowsGameScreen : IGameScreen, IDisposable
{
    private readonly Timer search;
    private readonly GameConfig config;
    private volatile int gameProcess;
    private nint gameWindow;

    public WindowsGameScreen(GameConfig config)
    {
        this.config = config;
        search = new Timer(_ => Search(), null, TimeSpan.Zero, Tuning.Control.GameSearchEvery);
    }

    private void Search()
    {
        var window = GameCapture.FindGame();
        gameWindow = window;
        GetWindowThreadProcessId(window, out var id);
        gameProcess = window == 0 ? 0 : (int)id;
    }

    public bool InFront => Front(ours: true);

    public bool GameInFront => Front(ours: false);

    private bool Front(bool ours)
    {
        int game = gameProcess;
        if (game == 0)
            return false;
        GetWindowThreadProcessId(GetForegroundWindow(), out var id);
        return id == game || ours && id == Environment.ProcessId;
    }

    public GameCapture.Frame? Capture(Box? area = null) => GameCapture.Capture(gameWindow, area, config.PopupSize, config.Language);

    public GameCapture.Shot? Snapshot() => GameCapture.Snapshot(gameWindow, config.PopupSize) is { } shot ? shot with { Language = config.Language, TextSize = config.TextSize } : null;

    public void Dispose() => search.Dispose();

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
}

// A fixed picture of the game, always in front: the golden-master check and --render.
public sealed class StillScreen(OpenCvSharp.Mat image, (int X, int Y)? pointer = null, double popup = GameConfig.StandardPopup, string language = GameLanguage.English) : IGameScreen
{
    public bool InFront => true;

    public GameCapture.Frame? Capture(Box? area = null) => GameCapture.Stored(image, pointer, area, popup, language);

    public GameCapture.Shot? Snapshot() => new(image.Clone(), pointer, popup, language);
}

// Where the mouse pointer was on stored pictures (testdata/pointers.json: picture → [x, y]).
// The pictures themselves do not hold it; those taken before the pointer was recorded carry
// the place worked out from the "ALT Inspect" hint beside it.
public static class StoredPointers
{
    public static IReadOnlyDictionary<string, (int X, int Y)> Load(string testdata)
    {
        var file = Path.Combine(testdata, "pointers.json");
        return File.Exists(file)
            ? JsonFiles.Read<Dictionary<string, int[]>>(file).ToDictionary(p => p.Key, p => (p.Value[0], p.Value[1]))
            : new Dictionary<string, (int, int)>();
    }
}

// The language the game was set to when a stored picture was taken (testdata/languages.json:
// picture → language); pictures not named there were taken on the English client.
public static class StoredLanguages
{
    // The language the client ran in when a stored picture was taken (testdata/languages.json:
    // name below testdata without extension → language); a picture not named there: English.
    public static IReadOnlyDictionary<string, string> Load(string testdata)
    {
        var file = Path.Combine(testdata, "languages.json");
        return File.Exists(file) ? JsonFiles.Read<Dictionary<string, string>>(file) : new Dictionary<string, string>();
    }
}

// The popup size the game was set to when a stored picture was taken (testdata/popups.json:
// picture → value); pictures not named there were taken at the game's standard.
public static class StoredPopups
{
    public static IReadOnlyDictionary<string, double> Load(string testdata)
    {
        var file = Path.Combine(testdata, "popups.json");
        return File.Exists(file) ? JsonFiles.Read<Dictionary<string, double>>(file) : new Dictionary<string, double>();
    }
}

using System.Text.Json;

namespace Poe2Pricer.Core;

// User settings, stored in %LOCALAPPDATA%\poe2pricer\settings.json.
public sealed record Settings
{
    // null: the current softcore league (first non-HC, non-permanent league on poe.ninja).
    public string? League { get; init; }
    public string ToggleKey { get; init; } = "Alt+F";
    public string DiagnoseKey { get; init; } = "Alt+Shift+F";

    // Look every AutoIntervalSeconds for the windows ticked below while none is shown
    // (deliberate load; the key always looks for all). As fast as the control allows by
    // default (Tuning.Control.AutoMinimumSeconds): prices as soon as a window opens. The
    // automatic scan runs as soon as one of the three windows below is ticked; three
    // switches side by side need no main switch.
    public double AutoIntervalSeconds { get; init; } = .2;

    // While a window is shown it is glanced at this often (still there? changed?); as fast as
    // the control allows by default (Tuning.Control.GlanceEvery): prices go with their window.
    // With AutoIntervalSeconds the two things to turn up when the game stutters: that one
    // counts while no window is shown, this one while one is.
    public double GlanceIntervalSeconds { get; init; } = .05;

    // Which windows the automatic scan shows by itself; the key always shows all of them.
    // All on by default, the user switches off.
    public bool AutoRitual { get; init; } = true;
    public bool AutoExpedition { get; init; } = true;
    public bool AutoStash { get; init; } = true;

    // The automatic scan runs: at least one window is ticked.
    [System.Text.Json.Serialization.JsonIgnore]
    public bool AutoAny => AutoRitual || AutoExpedition || AutoStash;

    // Items worth less get no label and do not compete for "most valuable" (default: show all).
    public decimal FloorValue { get; init; }
    public string FloorUnit { get; init; } = "ex";

    // The unit prices are written in below one divine: "ex" or "chaos" (from one divine on it
    // is always div). Late in a league exalts are cheap and the numbers grow long; chaos is
    // then the unit traders use (asked for on reddit, 19.10.2026).
    public string PriceUnit { get; init; } = "ex";

    // Start with Windows (Autostart); off by default.
    public bool StartWithWindows { get; init; }

    // Ask github.com once per start whether a newer release is out (UpdateCheck); on by default,
    // the one request the program makes that is not for prices or rumour ratings.
    public bool CheckForUpdates { get; init; } = true;

    // The language of the program window, the status line and the tray (Texts.Languages);
    // log and diagnostics are English whatever stands here.
    public string Language { get; init; } = Texts.Default;

    // The game's own settings file (GameConfig), read for the popup size; empty: where the
    // game keeps it, in the documents folder. Not everyone has it there.
    public string GameConfigFile { get; init; } = "";

    // Recognition data (catalog, images, recognition model): empty means the copy shipped with
    // the program (data/catalog beside it, DataPath). Set to read another folder in Dev-Jahn's
    // layout.
    public string DataDirectory { get; init; } = "";

    public static string ShippedDataPath => Path.Combine(AppContext.BaseDirectory, "data", "catalog");

    public string DataPath => DataDirectory is { Length: > 0 } ? DataDirectory : ShippedDataPath;

    // POE2PRICER_FOLDER replaces it for the golden-master check (frozen icons and rumour table).
    public static string Folder =>
        Environment.GetEnvironmentVariable("POE2PRICER_FOLDER") is { Length: > 0 } folder
            ? folder
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "poe2pricer");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    // False until the first save: the first start is the one that shows the window by itself.
    public static bool Stored => File.Exists(FilePath);

    public static Settings Load() => Load(FilePath, out _);

    // problem: something the user should know (the file could not be read), else null.
    public static Settings Load(out Told? problem) => Load(FilePath, out problem);

    // An unreadable file must not keep the program from starting: a file cut off while it was
    // written (crash, power loss) would otherwise throw at every start until it is deleted by
    // hand. The standard settings count; the file is kept as a copy beside it before the next
    // save writes over it.
    public static Settings Load(string file, out Told? problem)
    {
        problem = null;
        if (!File.Exists(file))
            return new Settings();
        try
        {
            var text = File.ReadAllText(file);
            var settings = JsonSerializer.Deserialize<Settings>(text, JsonFiles.Options) ?? new Settings();
            return MainSwitchOff(text) ? settings with { AutoRitual = false, AutoExpedition = false, AutoStash = false } : settings;
        }
        catch (JsonException)
        {
            var copy = Path.Combine(Path.GetDirectoryName(file) ?? "", Path.GetFileNameWithoutExtension(file) + ".broken.json");
            try
            {
                File.Copy(file, copy, overwrite: true);
                problem = new("SettingsUnreadableBackup", copy);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                problem = new("SettingsUnreadableNoBackup", file, e.Message);
            }
            return new Settings();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problem = new("SettingsUnreadable", e.Message);
            return new Settings();
        }
    }

    // A file from an earlier version with the automatic scan switched off by its main switch
    // ("autoScan": false): nothing was shown by itself, so none of the three windows is ticked
    // now. The next save writes the file without the old entry, and what is ticked from then
    // on counts.
    private static bool MainSwitchOff(string json) =>
        System.Text.Json.Nodes.JsonNode.Parse(json) is System.Text.Json.Nodes.JsonObject node
        && node.FirstOrDefault(p => p.Key.Equals("autoScan", StringComparison.OrdinalIgnoreCase)).Value is { } old
        && old.GetValueKind() == JsonValueKind.False;

    public void Save() => Save(FilePath);

    // Written next to the file and moved over it: a crash while writing leaves the old file.
    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var part = file + ".part";
        File.WriteAllText(part, JsonSerializer.Serialize(this, JsonFiles.Options));
        File.Move(part, file, overwrite: true);
    }
}

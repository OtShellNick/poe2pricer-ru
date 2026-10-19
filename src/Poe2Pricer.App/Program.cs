using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using Rect = System.Windows.Rect;
using Poe2Pricer.Core;
using Poe2Pricer.Core.Market;
using Poe2Pricer.Ui.ViewModels;
using Poe2Pricer.Ui.Views;

namespace Poe2Pricer.App;

// poe2pricer                       Overlay mit Tastenauslöser, Programmfenster und Infobereich.
// poe2pricer --render-settings <out.png>   Einstellungsfenster als Bild (Prüfung ohne Bildschirm;
//   POE2PRICER_TAB=1: Tab Rumours). Vergleich mit dem Sollbild: tools/fenster_vergleich.py.
// poe2pricer --render <in.png> <out.png> [<vorher.png> ...]
//                                  Vorschau ohne Spiel: erkennt ein Testbild, holt Preise und
//                                  zeichnet die Beschriftungen wie das Overlay in eine Datei.
//                                  vorher: Bilder, die das Fenster davor gezeigt hat (in dieser
//                                  Reihenfolge), etwa die Remnant-Liste vor dem Scrollen.
public static class Program
{
    // Only one instance: a second start brings the running one's window to the front and
    // quits; two instances would both take the key and draw two overlays.
    private const string InstanceName = "poe2pricer.single-instance";

    [STAThread]
    public static int Main(string[] args)
    {
        // First of all: on install, update and uninstall the setup starts the program with
        // arguments of its own, which this answers and ends the process. On uninstall the entry
        // that starts the program with Windows goes too.
        Velopack.VelopackApp.Build().OnBeforeUninstallFastCallback(_ => Autostart.Windows().Apply(false, "")).Run();
        Mutex? single = null;
        EventWaitHandle? showRequest = null;
        if (args.Length == 0)
        {
            single = new Mutex(true, InstanceName, out bool first);
            if (!first)
            {
                FileLog.Write("Started again: the running one is asked to show its window");
                if (EventWaitHandle.TryOpenExisting(InstanceName + ".show", out var other))
                    other.Set();
                return 0;
            }
            showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".show");
            // Which copy runs (several may lie around: the unpacked one, a built one), to
            // read the run from the log, and when it ended.
            FileLog.Write($"Started: {AppInfo.NameAndVersion}, {Environment.ProcessPath}, PID {Environment.ProcessId}");
        }
        try
        {
            int code = Run(args, showRequest);
            if (args.Length == 0)
                FileLog.Write($"Ended with {code}");
            return code;
        }
        // Whatever stops the start before the window and its handlers are there (no English
        // text recognition in Windows, a missing template or font) is said and logged instead
        // of the program just not being there. Not for the calls with arguments: tools run
        // those and must see the failure themselves.
        catch (Exception e) when (args.Length == 0)
        {
            FileLog.Write("Error (start): " + e);
            MessageBox.Show(Texts.Get("StartFailed", Texts.Shown(e), FileLog.Place), "poe2pricer", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
        finally
        {
            single?.ReleaseMutex();
            single?.Dispose();
            showRequest?.Dispose();
        }
    }

    private static int Run(string[] args, EventWaitHandle? showRequest)
    {
        // Before anything saves: no settings file yet means the first start.
        bool firstStart = !Settings.Stored;
        var settings = Poe2Pricer.Ui.HotKey.Usable(Settings.Load(out var settingsProblem), out var replacedKeys);
        Texts.Language = settings.Language;
        if (args.Length == 0 && !TextRecognition.Ensure())
            return 1;
        var ui =Path.Combine(AppContext.BaseDirectory, "data", "ui");
        var rumourShapes = RumourShapes.Load(Path.Combine(Settings.Folder, "rumour-lines.json"));
        // Every rumour text of the game (built in, RumourTexts) in the game's hand: lines are
        // named by their picture from the start. The English lines come as pictures shipped with
        // the program (data/rumours), the German ones are drawn with the built-in Caveat Bold;
        // the drawing takes a moment and must not hold up the start.
        var drawing = Task.Run(() =>
        {
            try
            {
                FileLog.Write($"Rumour lines loaded: {rumourShapes.Draw(GameLanguage.English)} of {RumourTexts.All.Count} texts");
                foreach (string language in new[] { GameLanguage.German })
                    FileLog.Write($"Rumour lines drawn ({language}): {rumourShapes.Draw(language)} of {RumourTexts.All.Count} texts");
            }
            catch (InvalidDataException e)
            {
                FileLog.Write("Error: " + e.Message);
            }
        });
        using var pipeline = new Pipeline(settings.DataPath, ui, rumourShapes: rumourShapes);
        var catalog = pipeline.Recognizer.Catalog.Items.ToDictionary(i => i.Id);
        ImageSource? Orb(string id) =>
            catalog.TryGetValue(id, out var item) ? LabelLayer.LoadImage(Path.Combine(settings.DataPath, item.Image)) : null;

        if (args.Length == 2 && args[0] == "--render-settings")
        {
            // POE2PRICER_LANGUAGE: the window in that language (the picture of a language pack).
            if (Environment.GetEnvironmentVariable("POE2PRICER_LANGUAGE") is { Length: > 0 } shown)
                Texts.Language = shown;
            var shownModel = new MainViewModel(settings, ["Forbidden Rites", "Runes of Aldur", "Standard"], RumourBook.LoadCached());
            // POE2PRICER_WARNINGS: the window with two lasting warnings (how the lines look).
            if (Environment.GetEnvironmentVariable("POE2PRICER_WARNINGS") is { Length: > 0 })
            {
                shownModel.Warn("textsize", new("GameTextSizeNotDefault", "large"));
                shownModel.Warn("gameconfig", new("GameSettingsNotFound", @"C:\Users\Name\Documents\My Games\Path of Exile 2\poe2_production_Config.ini"));
            }
            var w = new MainWindow(shownModel, Path.Combine(AppContext.BaseDirectory, "poe2pricer.ico"));
            int tab = Environment.GetEnvironmentVariable("POE2PRICER_TAB") is { } wanted ? int.Parse(wanted) : 0;
            // POE2PRICER_ROOM: the height of the screen's work area (a low screen, where the tab rolls).
            double room = Environment.GetEnvironmentVariable("POE2PRICER_ROOM") is { Length: > 0 } low ? double.Parse(low, System.Globalization.CultureInfo.InvariantCulture) : double.PositiveInfinity;
            WindowPicture.Save(WindowPicture.Of(w, tab, room), args[1]);
            return 0;
        }
        if (args.Length >= 3 && args[0] == "--render")
            return Render(args[1], args[2], args.Skip(3).ToArray(), pipeline, settings, Orb("Divine_Orb"), Orb("Exalted_Orb"), Orb("Chaos_Orb"));

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var iconPath = Path.Combine(AppContext.BaseDirectory, "poe2pricer.ico");
        var overlay = new OverlayWindow(Orb("Divine_Orb"), Orb("Exalted_Orb"), Orb("Chaos_Orb"));
        var gameConfig = new GameConfig(settings.GameConfigFile);
        using var screen = new WindowsGameScreen(gameConfig);
        using var prices = new NinjaPrices(settings);
        var rumourBook = RumourBook.LoadCached();
        using var engine = new OverlayEngine(GameWindows.Standard(pipeline), screen, prices, () => rumourBook, settings, TimeProvider.System, FileLog.Write);
        // A rumour panel read before the lines were drawn has lines without a name, and on a
        // client that is not English nothing else names them: they are looked at again once
        // the drawing is done (it takes a few seconds).
        _ = drawing.ContinueWith(_ => engine.RumoursChanged(), TaskScheduler.Default);
        var model = new MainViewModel(settings, [], rumourBook);
        var window = new MainWindow(model, iconPath, Path.Combine(AppContext.BaseDirectory, "data", "guide", "README.html"));
        using var tray = new TrayIcon(iconPath);
        // The status line of the program window: what is going on, what went wrong.
        void Told(StatusMessage message) => model.Report(message);
        void Status(string text) => Told(new(text));
        void Done(string text) => Told(StatusMessage.Ok(text));
        void Failed(string text) => Told(StatusMessage.Error(text));
        engine.Status += Told;
        // The league list comes from the cache or a single request, in the background: the
        // window does not wait for it, and a list that failed is asked for again with the next
        // price load. UI thread.
        bool leaguesKnown = false, leaguesLoading = false;
        void LoadLeagues()
        {
            if (leaguesKnown || leaguesLoading)
                return;
            leaguesLoading = true;
            _ = Task.Run(async () =>
            {
                string[] found = [];
                try
                {
                    using var ninja = new NinjaClient();
                    found = await ninja.LeaguesAsync();
                }
                // Whatever goes wrong — no net, or an answer that is no list of leagues — the
                // list is asked for again with the next load; "loading" must not stay set
                // for the rest of the run.
                catch (Exception e)
                {
                    FileLog.Write("League list not loaded: " + e.Message);
                }
                _ = app.Dispatcher.BeginInvoke(() =>
                {
                    leaguesLoading = false;
                    if (found.Length == 0)
                        return;
                    leaguesKnown = true;
                    model.Settings.SetLeagues(found);
                });
            });
        }
        LoadLeagues();
        // Last resort for anything outside the overlay control (which catches its own): logged
        // and shown instead of ending the overlay in the middle of the game.
        app.DispatcherUnhandledException += (_, e) =>
        {
            FileLog.Write("Error (UI): " + e.Exception);
            Failed(Texts.Shown(e.Exception));
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            FileLog.Write("Error (background): " + e.Exception);
            e.SetObserved();
        };
        // The overlay control decides on its own thread; the overlay window lives on the UI thread.
        engine.Draw += scene =>
            app.Dispatcher.BeginInvoke(() =>
            {
                if (scene is null)
                    overlay.Clear();
                else
                    overlay.Show(scene.Screen, scene.Labels, scene.Rumours, scene.Scale);
            });
        // Evidence pictures (a detected reroll in the Ritual window) go beside the diagnoses;
        // the engine writes them only with "Extended diagnostics" on (Settings).
        string diagnosticsFolder = Path.Combine(Settings.Folder, "diagnostics");
        engine.EvidenceFolder = diagnosticsFolder;
        engine.Diagnosed += file =>
        {
            if (file is null)
                Failed(Texts.Get("GameNotFound"));
            else
                Done(Texts.Get("DiagnosticsSaved", Path.GetFileName(file)));
            if (file is not null)
                app.Dispatcher.BeginInvoke(() => tray.Notify(Texts.Get("DiagnosticsSavedTray")));
        };
        // Not while a key field of the program window waits for a key (KeyBox.Listening).
        using var keys = new KeyboardHook(app.Dispatcher) { Active = () => screen.InFront && !KeyBox.Listening };
        using var foreground = new ForegroundWatcher();
        foreground.Changed += engine.ForegroundChanged;
        void Bind(Settings s)
        {
            keys.Clear();
            keys.Bind(s.ToggleKey, engine.Toggle);
            keys.Bind(s.DiagnoseKey, () => engine.Diagnose(diagnosticsFolder));
        }
        Bind(settings);
        // "Mit Windows starten": the entry follows the setting — now, where an entry naming
        // another place is set right, and whenever the settings are saved.
        var autostart = Autostart.Windows();
        void Autostarted(Settings s)
        {
            try
            {
                if (Environment.ProcessPath is { } program && autostart.Apply(s.StartWithWindows, program))
                    FileLog.Write(s.StartWithWindows ? $"Windows startup added: {program}" : "Windows startup removed");
            }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                FileLog.Write("Windows startup not changed: " + e.Message);
                Failed(Texts.Get("StartupNotChanged", e.Message));
            }
        }
        Autostarted(settings);
        // "Check for updates": once per start and once more when it is switched on, in the
        // background (Updates). An installed copy has the newer version fetched: the status line
        // and a tray message say so, a click installs it now, otherwise it is installed when the
        // program quits. An unpacked copy is only told, with the page. A failed request is only
        // logged: the program does not depend on github.com.
        var updates = new Updates();
        bool updatesChecked = false;
        void CheckUpdates()
        {
            updatesChecked = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    var found = await updates.LookAsync(FileLog.Write);
                    FileLog.Write(found is null ? "Update check: none newer" : $"Update check: {found.Version}{(found.Downloaded ? " downloaded" : "")} ({found.Page})");
                    if (found is null)
                        return;
                    await app.Dispatcher.BeginInvoke(() =>
                    {
                        if (found.Downloaded)
                        {
                            Told(StatusMessage.Acting(Texts.Get("UpdateReady", found.Version), updates.ApplyNow));
                            tray.Notify(Texts.Get("UpdateReadyTray", found.Version), updates.ApplyNow);
                        }
                        else
                        {
                            Told(StatusMessage.Linked(Texts.Get("UpdateAvailable", found.Version), found.Page));
                            tray.Notify(Texts.Get("UpdateAvailableTray", found.Version), () => OpenPage(found.Page));
                        }
                    });
                }
                catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or IOException)
                {
                    FileLog.Write("Update check failed: " + e.Message);
                }
            });
        }
        void OpenPage(string page)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(page) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                Status(page);
            }
        }
        if (settings.CheckForUpdates)
            CheckUpdates();
        model.Settings.Saved += s =>
        {
            Texts.Language = s.Language;
            gameConfig.Use(s.GameConfigFile);
            engine.Apply(s);
            Bind(s);
            Autostarted(s);
            if (s.CheckForUpdates && !updatesChecked)
                CheckUpdates();
            if (prices.Apply(s))
            {
                Status(Texts.Get("LoadingPrices"));
                Report(prices.GetAsync());
            }
        };
        tray.OpenSettings += () => { window.Show(); window.Activate(); };
        model.Quit += () => app.Shutdown();
        // The outcome of a load the user waits for; a load replaced by another league says nothing.
        void Report(Task<PriceBook> load) =>
            _ = load.ContinueWith(t =>
            {
                if (t.IsCanceled)
                    return;
                if (t.IsFaulted)
                {
                    // In the log too: the status line is gone with the next message.
                    FileLog.Write("Prices not loaded: " + t.Exception!.GetBaseException().Message);
                    Failed(Texts.Shown(t.Exception!.GetBaseException()));
                }
                else
                {
                    Done(Texts.Get("Ready", Texts.Count("Prices", t.Result.Count), t.Result.League));
                    _ = app.Dispatcher.BeginInvoke(LoadLeagues);
                }
            });
        prices.Failed += e =>
        {
            FileLog.Write("Prices not refreshed: " + e.Message);
            Failed(Texts.Get("NinjaRefreshFailed", Texts.Reason(e) ?? e.Message));
        };
        // Item art for the stash tabs, fetched once (first start, new items); "?" there meanwhile.
        pipeline.Icons.Loading += (done, total) =>
        {
            if (done == total)
                Done(Texts.Get("ItemImagesLoaded"));
            else if (done == 1 || done % 25 == 0)
                Status(Texts.Get("ItemImagesLoading", done, total));
        };
        model.Rumours.Changed += engine.RumoursChanged;
        // asked: "Refresh" in the window — then the outcome is told, so that "Loading
        // community data…" does not stay for good.
        void LoadRumours(bool asked) =>
            _ = Task.Run(async () =>
            {
                bool loaded = false;
                try
                {
                    using var http = new System.Net.Http.HttpClient();
                    loaded = await rumourBook.RefreshAsync(http);
                }
                catch (Exception e)
                {
                    FileLog.Write("Error (community data): " + e);
                }
                if (loaded)
                {
                    // The rows belong to the window's thread; waits until they are built.
                    app.Dispatcher.Invoke(() => model.Rumours.Show(rumourBook));
                    engine.RumoursChanged();
                }
                if (asked && loaded)
                    Done(Texts.Get("CommunityDataLoaded", Texts.Count("Entries", rumourBook.Entries.Count(e => !e.Custom))));
                else if (asked)
                    Failed(Texts.Get("CommunityDataNotLoaded"));
            });
        model.Rumours.Reload += () => { Status(Texts.Get("LoadingCommunityData")); LoadRumours(asked: true); };
        LoadRumours(asked: false);
        // What is saved is applied already (Saved above); the settings file is not read anew
        // here.
        model.Settings.ReloadPrices += () =>
        {
            Status(Texts.Get("LoadingPrices"));
            Report(prices.ReloadAsync());
        };
        Status(Texts.Get("LoadingPrices"));
        Report(prices.GetAsync());
        // Told in the chosen language, logged in English.
        void Warn(Told told)
        {
            FileLog.Write(told.English);
            tray.Notify(told.Shown);
        }
        // What lasts stands in the window too, on a line of its own, until its cause is gone.
        void Lasting(string cause, Told told)
        {
            Warn(told);
            model.Warn(cause, told);
        }
        if (rumourBook.Problem is { } problem)
            Lasting(MainViewModel.RatingsCause, problem);
        if (settingsProblem is not null)
            Lasting(MainViewModel.SettingsCause, settingsProblem);
        if (keys.Error is { } refused)
            Lasting("hotkeys", new("HotkeysUnavailable", refused));
        // One line for the keys: it shares the cause of the settings and goes with the next save.
        foreach (var (stored, used) in replacedKeys)
            Warn(new("KeyUnreadable", stored, used));
        if (settingsProblem is null && replacedKeys.Count > 0)
            model.Warn(MainViewModel.SettingsCause, new("KeyUnreadable", replacedKeys[0].Stored, replacedKeys[0].Used));
        // Without the game's settings file the popup size is taken for the standard; with
        // another size set in the game, tooltips and the rumour panel would not be recognised.
        // Looked at again with every save and with the prices drawn, like the text size below.
        bool toldNotFound = false;
        void CheckGameConfig()
        {
            bool missing = !gameConfig.Found;
            if (missing && !toldNotFound)
                Warn(new("GameSettingsNotFound", gameConfig.File));
            toldNotFound = missing;
            model.Warn("gameconfig", missing ? new("GameSettingsNotFound", gameConfig.File) : null);
        }
        CheckGameConfig();
        model.Settings.Saved += _ => CheckGameConfig();
        engine.Draw += _ => app.Dispatcher.BeginInvoke(CheckGameConfig);
        if (gameConfig.Found)
            FileLog.Write($"Game settings: {gameConfig.File}, language {gameConfig.Language}, UI Text Size {gameConfig.TextSize}, Popup Size {gameConfig.PopupSize.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}");
        // A larger UI Text Size is not supported: said at the start, and whenever another
        // value is found later — the game writes its settings file while it runs, so a size
        // changed in the game's options after the start shows up with the next prices drawn.
        string toldTextSize = GameConfig.StandardTextSize;
        void CheckTextSize()
        {
            string now = gameConfig.TextSize;
            if (!gameConfig.TextSizeSupported && now != toldTextSize)
                Warn(new("GameTextSizeNotDefault", now));
            toldTextSize = now;
            model.Warn("textsize", gameConfig.TextSizeSupported ? null : new("GameTextSizeNotDefault", now));
        }
        CheckTextSize();
        model.Settings.Saved += _ => CheckTextSize();
        engine.Draw += _ => app.Dispatcher.BeginInvoke(CheckTextSize);
        if (showRequest is not null)
        {
            var wait = new Thread(() =>
            {
                while (showRequest.WaitOne())
                    app.Dispatcher.Invoke(() =>
                    {
                        window.Show();
                        if (window.WindowState == WindowState.Minimized)
                            window.WindowState = WindowState.Normal;
                        window.Activate();
                    });
            })
            {
                IsBackground = true,
                Name = "Second start",
            };
            wait.Start();
        }
        // The window opens by itself only at the first start; after that the program starts in
        // the tray and the window comes by the tray icon or a second start. A window opened
        // while Windows signs the user in is not let to the front and ends up behind the
        // desktop widgets, where nobody looks for it; the comparable tools show none either.
        if (firstStart)
            window.Show();
        else
            tray.Notify(Texts.Get("RunningInTray", Poe2Pricer.Ui.HotKey.Shown(settings.ToggleKey)));
        int code = app.Run();
        updates.ApplyOnExit();
        return code;
    }

    private static int Render(string input, string output, string[] before, Pipeline pipeline, Settings settings, ImageSource? divine, ImageSource? exalted, ImageSource? chaos)
    {
        using var frame = Cv2.ImRead(input);
        using var ninja = new NinjaClient();
        var book = ninja.PricesAsync(settings.League ?? NinjaClient.DefaultLeague(ninja.LeaguesAsync().GetAwaiter().GetResult())).GetAwaiter().GetResult();
        pipeline.Icons.EnsureAsync(book.Images.Select(p => (p.Key, p.Value.Path))).GetAwaiter().GetResult();
        using var shot = GameCapture.Stored(frame);
        var context = new LookContext(RumourBook.LoadCached(), settings, Console.WriteLine);
        var windows = GameWindows.Standard(pipeline);
        IOpenWindow? open = null;
        // As the overlay does: the first picture is looked at, each further one glanced at and
        // followed (the rumour panel shows its tiers only once a glance found it at rest).
        foreach (var file in before.Append(input))
        {
            using var image = Cv2.ImRead(file);
            using var seen = GameCapture.Stored(image);
            if (open is not null)
            {
                using var glanced = GameCapture.Stored(image, area: open.GlanceArea);
                open = open.Glance(glanced.Image, book, context) switch
                {
                    Glance.Gone => null,
                    Glance.Reread reread => reread.Next,
                    Glance.Relabel relabel => relabel.Next,
                    _ => open,
                };
            }
            open = open is null ? null : open.FollowAsync(seen, context).GetAwaiter().GetResult();
            open ??= windows.LookAsync(seen, context, _ => true).GetAwaiter().GetResult();
            if (open is not null && file != input)
                open = open.LabelAsync(seen, Task.FromResult(book), context).GetAwaiter().GetResult().Next;
        }
        if (open is null)
        {
            Console.Error.WriteLine(Texts.English("NoWindowDetected"));
            return 1;
        }
        var picture = open.LabelAsync(shot, Task.FromResult(book), context).GetAwaiter().GetResult();
        Console.WriteLine(picture.Status);
        var labels = picture.Labels;
        var rumours = picture.Rumours;

        var background = new BitmapImage(new Uri(Path.GetFullPath(input)));
        // Drawn onto the picture as it is: at the game's part of it, at the picture's scale.
        var layer = new LabelLayer { Labels = labels, Divine = divine, Exalted = exalted, Chaos = chaos, Rumours = rumours, PixelScale = shot.Scale, Origin = new System.Windows.Point(shot.ScreenBounds.X, shot.ScreenBounds.Y) };
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(background, new Rect(0, 0, frame.Width, frame.Height));
        }
        layer.Measure(new System.Windows.Size(frame.Width, frame.Height));
        layer.Arrange(new Rect(0, 0, frame.Width, frame.Height));
        var bitmap = new RenderTargetBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Render(layer);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var written = File.Create(output);
        encoder.Save(written);
        Console.WriteLine($"{labels.Length} labels, {labels.Count(l => l.Text is not null)} with a price → {output}");
        return 0;
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poe2Pricer.Core;

namespace Poe2Pricer.Ui.ViewModels;

// The program window: both tabs, the status line and "Exit". "Save" takes settings and rumour
// ratings together: the settings are written, then the ratings, then the program is told.
// A language of the window in the list at the right of the tabs: its code as the settings
// hold it ("en", "de") and what stands beside the flag ("EN", "DE").
public sealed record LanguageChoice(string Code)
{
    public string Label => Code.ToUpperInvariant();
}

public sealed class MainViewModel : ObservableObject
{
    private StatusMessage status = new("");
    private readonly List<(string Cause, Told Told)> warnings = [];
    private readonly Action<string> storeLanguage;
    private LanguageChoice language;

    // store: writes the settings; the tests pass their own, the program writes settings.json.
    // storeLanguage: writes the chosen language at once; the program writes it into
    // settings.json without touching what else stands there.
    public MainViewModel(Settings settings, IEnumerable<string> leagues, RumourBook rumours, Action<Settings>? store = null, Action<string>? storeLanguage = null)
    {
        this.storeLanguage = storeLanguage ?? (code => (Core.Settings.Load() with { Language = code }).Save());
        language = Languages.FirstOrDefault(l => l.Code == Texts.Language) ?? Languages[0];
        Rumours = new RumoursViewModel(rumours);
        Settings = new SettingsViewModel(
            settings,
            leagues,
            next =>
            {
                (store ?? (s => s.Save()))(next);
                var notSaved = Rumours.Save();
                // Written anew: what could not be read at the start is no longer the matter.
                Warn(SettingsCause, null);
                if (notSaved is null)
                    Warn(RatingsCause, null);
                return notSaved;
            },
            Report
        );
        QuitCommand = new RelayCommand(() => Quit?.Invoke());
        Texts.Changed += () => OnPropertyChanged(nameof(Warnings));
    }

    // What stays to be said as long as its cause lasts, each on a line of its own above the
    // status line: the settings or the own ratings could not be read, the game's settings file
    // is not found, the game's text size is not supported, the keys are not available. A line
    // of its own for each, not a passing notice in the tray. Window thread.
    // cause: one line per cause, a new text replaces the old one; told null: the cause is gone.
    // Warnings that go with the next save carry SettingsCause or RatingsCause.
    public const string SettingsCause = "settings", RatingsCause = "ratings";

    public void Warn(string cause, Told? told)
    {
        int at = warnings.FindIndex(w => w.Cause == cause);
        if (told is null && at < 0)
            return;
        if (told is null)
            warnings.RemoveAt(at);
        else if (at < 0)
            warnings.Add((cause, told));
        else
            warnings[at] = (cause, told);
        OnPropertyChanged(nameof(Warnings));
    }

    public IReadOnlyList<string> Warnings => warnings.Select(w => w.Told.Shown).ToArray();

    // The languages of the window, each with its flag and country code (the list at the right
    // of the tabs). Choosing one counts at once and is saved at once, without "Save": every
    // text of the window follows, the status line with its next message.
    public IReadOnlyList<LanguageChoice> Languages { get; } = Texts.Languages.Select(code => new LanguageChoice(code)).ToArray();

    public LanguageChoice Language
    {
        get => language;
        set
        {
            if (value is null || !SetProperty(ref language, value))
                return;
            Texts.Language = value.Code;
            Settings.SetLanguage(value.Code);
            storeLanguage(value.Code);
        }
    }

    public SettingsViewModel Settings { get; }
    public RumoursViewModel Rumours { get; }
    public IRelayCommand QuitCommand { get; }

    // Behind the name in the title bar.
    public string Version { get; } = "v" + AppInfo.Version;

    public event Action? Quit;

    // The status line: its text and what it says about the program (the dot before it, red
    // text for an error). May be set from any thread: the overlay control reports from its own.
    public void Report(StatusMessage message)
    {
        status = message;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusKind));
        OnPropertyChanged(nameof(StatusLink));
        OnPropertyChanged(nameof(StatusClickable));
    }

    // Whether the line is a link: it leads to a page or does something.
    public bool StatusClickable => status.Link is not null || status.Act is not null;

    // A click on the line: does what it does, else opens its page (open: how).
    public void StatusClicked(Action<Uri> open)
    {
        if (status.Act is { } act)
            act();
        else if (StatusLink is { } link)
            open(link);
    }

    public string Status => status.Text;
    public StatusKind StatusKind => status.Kind;

    // The page the line leads to; null: plain text. A Uri, as the link in the window wants
    // it: a null string could not be converted to one.
    public Uri? StatusLink => status.Link is { } link ? new Uri(link) : null;
}

using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poe2Pricer.Core;
using Poe2Pricer.Core.Market;
using Poe2Pricer.Core.Tablets;
using Poe2Pricer.Core.Waystones;

namespace Poe2Pricer.Ui.ViewModels;

// A mod in the lists of the Waystones and Tablets tabs: prefix or suffix, its text as the
// game writes it (ModText, English: the name a profile keeps), the span of its number; Text is
// the line shown in the list, in the words of the game client (Shown, German for a German
// client: the list says what the tooltip says).
public sealed record ModChoice(string Kind, string ModText, bool HasValue, int? Min, int? Max, string? Shown = null)
{
    public string Text => (Kind == "prefix" ? "P · " : "S · ") + (Shown ?? ModText) + (HasValue ? $"  ({Min}–{Max})" : "");

    public override string ToString() => Text;

    public static readonly IReadOnlyList<ModChoice> Waystones = Ordered(WaystoneMods.All.Select(m => new ModChoice(m.Kind, m.Text, m.HasValue, m.Min, m.Max)));

    private static readonly IReadOnlyList<ModChoice> WaystonesDe = Ordered(WaystoneMods.All.Select(m => new ModChoice(m.Kind, m.Text, m.HasValue, m.Min, m.Max, m.De)));

    public static IReadOnlyList<ModChoice> ForWaystones(string language) => language == GameLanguage.German ? WaystonesDe : Waystones;

    // The lines a tablet of this kind can carry: the generic ones and the kind's own.
    public static IReadOnlyList<ModChoice> Tablets(TabletKind kind, string language = GameLanguage.English) =>
        Ordered(TabletMods.For(kind).Select(m => new ModChoice(m.Kind, m.Text, m.HasValue, m.Min, m.Max, language == GameLanguage.German ? m.De : null)));

    private static ModChoice[] Ordered(IEnumerable<ModChoice> choices) => choices.OrderBy(c => c.Kind == "prefix" ? 0 : 1).ThenBy(c => c.Shown ?? c.ModText).ToArray();
}

// The kind of a group of mod rows in the window's language (Core.Market.GroupKind).
public sealed record GroupKindOption(GroupKind Value, string Text)
{
    public override string ToString() => Text;

    public static IReadOnlyList<GroupKindOption> All() =>
        [new(GroupKind.And, Texts.Get("RuleAnd")), new(GroupKind.Or, Texts.Get("RuleOr")), new(GroupKind.Not, Texts.Get("RuleNot"))];
}

// One row of a group: the mod, picked from the list narrowed by what is typed into the
// filter beside it; for a mod with a number outside a "not" group, the value at least this
// high (the field shows the mod's span while empty).
// all: the mods the row can take (a waystone's, or a kind of tablet's).
public sealed class ModRuleViewModel(Action changed, IReadOnlyList<ModChoice> all) : ObservableObject
{
    private ModChoice? mod;
    private string min = "";
    private string filter = "";
    private bool minInvalid;
    private bool excludes;
    private bool dropDownOpen;

    public ModChoice? Mod
    {
        get => mod;
        set
        {
            if (SetProperty(ref mod, value))
            {
                OnPropertyChanged(nameof(HasValue));
                OnPropertyChanged(nameof(Range));
                OnPropertyChanged(nameof(ShowRange));
                changed();
            }
        }
    }

    // The text of the list's field: what is typed narrows the list to mods whose text holds
    // it (case does not matter) and opens the list; a chosen mod puts its own text here, and
    // then the whole list shows again. Emptied, the choice goes.
    public string Filter
    {
        get => filter;
        set
        {
            if (!SetProperty(ref filter, value))
                return;
            OnPropertyChanged(nameof(Choices));
            if (value.Trim().Length == 0)
                Mod = null;
            else if (Mod is null || value != Mod.Text)
                DropDownOpen = true;
        }
    }

    public bool DropDownOpen
    {
        get => dropDownOpen;
        set => SetProperty(ref dropDownOpen, value);
    }

    // Mods another row of the same group has chosen (set by the group): a line stands on an
    // item once, so the same mod twice in a group would only repeat it (21.10.2026).
    // Across groups it may come again: "A or B" and "A or C" is a question of its own.
    public Func<ModChoice, bool>? Taken { get; set; }

    // The chosen mod stays in the list whatever is typed: taken out, the list would drop the
    // choice and with it the text just typed.
    public IReadOnlyList<ModChoice> Choices
    {
        get
        {
            string wanted = filter.Trim();
            bool every = wanted.Length == 0 || (Mod is not null && wanted == Mod.Text.Trim());
            return all.Where(c => c == Mod || ((every || c.ModText.Contains(wanted, StringComparison.OrdinalIgnoreCase) || (c.Shown?.Contains(wanted, StringComparison.OrdinalIgnoreCase) ?? false)) && Taken?.Invoke(c) != true)).ToList();
        }
    }

    // Another row of the group chose or let go of a mod.
    public void Retaken() => OnPropertyChanged(nameof(Choices));

    // The list in other words (the game client's language changed): the same mod, its line in
    // the new words in the field; what was typed or entered otherwise stays.
    public void Reword(IReadOnlyList<ModChoice> choices)
    {
        all = choices;
        if (mod is { } was)
        {
            mod = choices.FirstOrDefault(c => c.ModText == was.ModText);
            if (mod is not null)
                filter = mod.Text;
        }
        OnPropertyChanged(nameof(Choices));
        OnPropertyChanged(nameof(Mod));
        OnPropertyChanged(nameof(Filter));
        OnPropertyChanged(nameof(Range));
    }

    // The row stands in a "not" group: no value (set by its group).
    public bool Excludes
    {
        get => excludes;
        set
        {
            if (SetProperty(ref excludes, value))
            {
                OnPropertyChanged(nameof(HasValue));
                OnPropertyChanged(nameof(ShowRange));
            }
        }
    }

    public string Min
    {
        get => min;
        set
        {
            SetProperty(ref min, value);
            MinInvalid = false;
            OnPropertyChanged(nameof(ShowRange));
            changed();
        }
    }

    public bool MinInvalid
    {
        get => minInvalid;
        set => SetProperty(ref minInvalid, value);
    }

    // The field takes a number only on a mod with a number outside a "not" group.
    public bool HasValue => !Excludes && Mod?.HasValue == true;

    // The mod's span, shown in the empty field.
    public string Range => Mod is { HasValue: true } m ? $"{m.Min}–{m.Max}" : "";

    public bool ShowRange => HasValue && Min.Trim().Length == 0;

    // The row as text and minimum; null: no mod chosen. invalid: the minimum is no number.
    public (string Text, int? Min)? Rule(out bool invalid)
    {
        invalid = false;
        if (Mod is null)
            return null;
        int? minimum = null;
        if (HasValue && Min.Trim().Length > 0)
        {
            if (int.TryParse(Min.Trim(), out int n))
                minimum = n;
            else
                invalid = true;
        }
        return (Mod.ModText, minimum);
    }
}

// A group of mod rows: its title ("Group 2", numbered by the tab), its kind (and, or, not),
// its rows. The groups of a profile are joined by "and".
public sealed class GroupViewModel : ObservableObject
{
    private readonly Action changed;
    private readonly Func<ModRuleViewModel> newRow;
    private GroupKindOption kind;
    private int number;

    public GroupViewModel(GroupKind kind, Action changed, Func<ModRuleViewModel> newRow, Func<GroupViewModel, bool> canAdd, Action<GroupViewModel> remove, Action<GroupViewModel> addAfter)
    {
        this.changed = changed;
        this.newRow = newRow;
        Kinds = GroupKindOption.All();
        this.kind = Kinds.First(k => k.Value == kind);
        AddRuleCommand = new RelayCommand(() => Rules.Add(Row()), () => canAdd(this));
        RemoveRowCommand = new RelayCommand<ModRuleViewModel>(row =>
        {
            if (row is not null && Rules.Remove(row))
                changed();
        });
        RemoveGroupCommand = new RelayCommand(() => remove(this));
        AddGroupCommand = new RelayCommand(() => addAfter(this));
        Rules.CollectionChanged += (_, e) =>
        {
            foreach (ModRuleViewModel row in e.NewItems ?? Array.Empty<ModRuleViewModel>())
            {
                row.Taken = c => Rules.Any(other => other != row && other.Mod == c);
                row.PropertyChanged += (_, p) =>
                {
                    if (p.PropertyName == nameof(ModRuleViewModel.Mod))
                        Retake(row);
                };
            }
            Retake(null);
            changed();
        };
    }

    // The other rows' lists after a row's choice changed (all rows: one came or went).
    private void Retake(ModRuleViewModel? chosen)
    {
        foreach (var row in Rules)
            if (row != chosen)
                row.Retaken();
    }

    public ObservableCollection<ModRuleViewModel> Rules { get; } = [];
    public IRelayCommand AddRuleCommand { get; }
    public IRelayCommand<ModRuleViewModel> RemoveRowCommand { get; }
    public IRelayCommand RemoveGroupCommand { get; }

    // A new "or" group right after this one.
    public IRelayCommand AddGroupCommand { get; }
    public IReadOnlyList<GroupKindOption> Kinds { get; private set; }

    public int Number
    {
        get => number;
        set
        {
            if (SetProperty(ref number, value))
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(FollowsAnother));
            }
        }
    }

    public string Title => Texts.Get("RuleGroup", Number);

    // A line above it parts it from the group before; the first group has none.
    public bool FollowsAnother => Number > 1;

    public GroupKindOption Kind
    {
        get => kind;
        set
        {
            if (value is null || !SetProperty(ref kind, value))
                return;
            foreach (var row in Rules)
                row.Excludes = value.Value == GroupKind.Not;
            changed();
        }
    }

    public GroupKind Value
    {
        get => kind.Value;
        set => Kind = Kinds.First(k => k.Value == value);
    }

    public ModRuleViewModel Row()
    {
        var row = newRow();
        row.Excludes = Value == GroupKind.Not;
        return row;
    }

    public void Reword()
    {
        var value = kind.Value;
        Kinds = GroupKindOption.All();
        OnPropertyChanged(nameof(Kinds));
        kind = Kinds.First(k => k.Value == value);
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(Title));
    }
}

// What the Waystones and the Tablets tab share: the mod groups (and, or, not), the tolerance
// of the live price (switched in the settings), the search text for the game's stash search
// (copied with a click), the profile's price on the trade site (asked only with "Refresh
// price"), and the save of the tab's own settings (✓). What the fields hold is written into
// the profile at once (Store); leaving a profile with changes, or the program with "Exit",
// asks to save or throw them away.
public abstract class ProfileTabViewModel<TProfile, TSettings> : ObservableObject
    where TProfile : notnull
{
    protected readonly Action<StatusMessage> Report;
    private readonly Action<TSettings>? store;
    protected readonly TimeProvider Clock;
    private ITimer? priceTimer;
    private string tolerance;
    private bool toleranceInvalid;
    private string search = "";
    private string profilePrice = "";
    private string profilePriceNote = "";
    private object? profilePriceIcon;
    private bool pricing;
    // Counts the profiles loaded into the tab: an answer for one shown before is dropped.
    private int shownProfile;
    private Func<TProfile, Task<(string Price, string Unit, int Listings)?>>? priceLookup;

    public static readonly TimeSpan RatesRetry = TimeSpan.FromSeconds(5);

    public const int SearchLimit = SearchRegex.Limit;

    // store: writes the tab's settings on the profile's own save (✓), apart from the program
    // settings; null: nothing is written (tests).
    protected ProfileTabViewModel(Action<StatusMessage> report, TimeProvider? clock, Action<TSettings>? store, int tolerance)
    {
        Report = report;
        this.store = store;
        Clock = clock ?? TimeProvider.System;
        this.tolerance = tolerance.ToString();
        AddGroupCommand = new RelayCommand(() => WithRow(AddGroup(Groups.Count == 0 ? GroupKind.And : GroupKind.Or)));
        // The last group's rows, and a row out of any group: what the tests and a keyboard
        // user without the group buttons reach.
        AddRuleCommand = new RelayCommand(() => LastGroup().Rules.Add(LastGroup().Row()), () => Groups.Count == 0 || CanAddRule(Groups[^1]));
        RemoveRowCommand = new RelayCommand<ModRuleViewModel>(row =>
        {
            if (row is not null && Groups.FirstOrDefault(g => g.Rules.Contains(row)) is { } group)
                group.Rules.Remove(row);
        });
        SaveProfileCommand = new RelayCommand(() => SaveProfile());
        CopyCommand = new RelayCommand(() =>
        {
            if (Search.Length == 0)
                return;
            (CopyToClipboard ?? (text => System.Windows.Clipboard.SetText(text)))(Search);
            Report(new(Texts.Get("WaystoneSearchCopied")));
        });
        PriceCommand = new AsyncRelayCommand(PriceAsync, () => !pricing);
        Groups.CollectionChanged += (_, _) => Changed();
        Texts.Changed += Reworded;
    }

    // Loading a profile into the fields: changes do not write back or ask for a price.
    protected bool Loading { get; set; }

    // The groups of mod rows, joined by "and"; one at least.
    public ObservableCollection<GroupViewModel> Groups { get; } = [];
    public IRelayCommand AddGroupCommand { get; }

    // Every row of every group, in order.
    public IReadOnlyList<ModRuleViewModel> Rules => Groups.SelectMany(g => g.Rules).ToList();
    public IRelayCommand AddRuleCommand { get; }
    public IRelayCommand<ModRuleViewModel> RemoveRowCommand { get; }
    public IRelayCommand SaveProfileCommand { get; }
    public IRelayCommand CopyCommand { get; }
    public IAsyncRelayCommand PriceCommand { get; }

    // The settings were written; the program applies them to the overlay.
    public event Action<TSettings>? Saved;

    // The window's clipboard; the tests pass their own.
    public Action<string>? CopyToClipboard { get; set; }

    // The trade site, asked for the profile's price with "Refresh price": the price as text,
    // its unit and how many listings there are, null for none; not asked, thrown with the
    // reason (TradeNotAskedException). Set by the program after the window is built.
    // IconFor: the orb of a unit ("div", "ex", "chaos"), drawn before the price.
    // PricesReady: whether poe.ninja's rates are loaded; until they are, a "Refresh price"
    // asks again every RatesRetry.
    public Func<TProfile, Task<(string Price, string Unit, int Listings)?>>? PriceLookup
    {
        get => priceLookup;
        set => priceLookup = value;
    }

    public Func<bool>? PricesReady { get; set; }

    // Where the tab's price asking is logged (the program's log file); null: nowhere.
    public Action<string>? Log { get; set; }

    public Func<string, object?>? IconFor
    {
        get => iconFor;
        set
        {
            iconFor = value;
            OnPropertyChanged(nameof(ProfilePriceIcon));
        }
    }

    private Func<string, object?>? iconFor;

    // The asking touches the window's bindings, so it runs on the window's thread: the
    // program hands over its dispatcher. (The thread's SynchronizationContext is not set
    // yet when the model is built; the timer then fired on its own thread and the
    // command's NotifyCanExecuteChanged threw, 20.10.2026.)
    public Action<Action> Dispatch { get; set; } = run => run();

    public string Tolerance { get => tolerance; set { SetProperty(ref tolerance, value); ToleranceInvalid = false; } }

    public bool ToleranceInvalid { get => toleranceInvalid; protected set => SetProperty(ref toleranceInvalid, value); }

    // The search text for the game's stash search, from the fields as they stand.
    public string Search
    {
        get => search;
        private set
        {
            if (SetProperty(ref search, value))
            {
                OnPropertyChanged(nameof(SearchLength));
                OnPropertyChanged(nameof(SearchTooLong));
            }
        }
    }

    public string SearchLength => Texts.Get("WaystoneSearchLength", Search.Length, SearchLimit);

    public bool SearchTooLong => Search.Length > SearchLimit;

    // The cheapest item the profile describes, big; the note beside it says how many
    // listings, or that the site is being asked.
    public string ProfilePrice
    {
        get => profilePrice;
        protected set
        {
            if (SetProperty(ref profilePrice, value))
                OnPropertyChanged(nameof(ProfilePriceIcon));
        }
    }

    public string ProfilePriceNote
    {
        get => profilePriceNote;
        protected set => SetProperty(ref profilePriceNote, value);
    }

    // The orb before the price; while there is none, an Exalted Orb beside the hint to ask.
    public object? ProfilePriceIcon
    {
        get => profilePriceIcon ?? (ProfilePrice.Length == 0 ? IconFor?.Invoke("ex") : null);
        protected set => SetProperty(ref profilePriceIcon, value);
    }

    // The profile as the fields describe it.
    public abstract TProfile Profile();

    // What "Save" takes; Valid false: a field holds no number, it is marked, nothing is saved.
    public abstract (bool Valid, TSettings Settings) Current();

    // The profile in the list follows the fields.
    protected abstract void Store();

    protected abstract string BuildSearch(TProfile profile);

    // The language of the game client (GameLanguage, from its settings file): the stash search
    // matches the words the client shows, so the search text is built in them.
    public string GameLanguage
    {
        get => gameLanguage;
        set
        {
            if (value == gameLanguage)
                return;
            gameLanguage = value;
            // The mod lists in the new words; each row keeps its mod and what was entered (review
            // before 1.2.0: rebuilt rows lost an empty row, a minimum not yet a number and the price).
            foreach (var group in Groups)
                foreach (var row in group.Rules)
                    row.Reword(Choices);
            ClientWorded();
            Search = BuildSearch(Profile());
        }
    }

    // A change of the game client's language (not the window's, Reworded) for what else the
    // tab shows in the client's words: the kinds of tablets.
    protected virtual void ClientWorded() { }

    private string gameLanguage = Core.GameLanguage.English;

    // The mods a new row can take.
    protected abstract IReadOnlyList<ModChoice> Choices { get; }

    // Whether a group takes another row.
    protected virtual bool CanAddRule(GroupViewModel group) => true;

    // The status line after a save.
    protected abstract string SavedMessage();

    // The tolerance from its field (per cent, 0–99), the default when it is no number (Current marks it).
    protected int ToleranceValue()
    {
        ToleranceInvalid = !int.TryParse(Tolerance.Trim(), out int tol) || tol < 0 || tol > 99;
        return ToleranceInvalid ? Poe2Pricer.Core.Market.Tolerance.Default : tol;
    }

    // Every row's minimum checked and marked; true: one is no number.
    protected bool RowsInvalid()
    {
        bool rows = false;
        foreach (var row in Rules)
        {
            row.Rule(out bool invalid);
            row.MinInvalid = invalid;
            rows |= invalid;
        }
        return rows;
    }

    // The groups as the profile keeps them: rows as text and minimum (none in a "not" group),
    // empty groups left out.
    protected ModGroup<TRule>[] GroupsAs<TRule>(Func<string, int?, TRule> make) =>
        Groups.Select(g => new ModGroup<TRule>(g.Value, g.Rules.Select(r => r.Rule(out _)).Where(r => r is not null)
                .Select(r => make(r!.Value.Text, g.Value == GroupKind.Not ? null : r.Value.Min)).ToArray()))
            .Where(g => g.Rules.Length > 0).ToArray();

    protected ModRuleViewModel Row((string Text, int? Min)? rule = null)
    {
        var row = new ModRuleViewModel(Changed, Choices);
        if (rule is { } r)
        {
            row.Mod = Choices.FirstOrDefault(c => c.ModText == r.Text);
            // A line the game's list no longer holds (renamed by a patch): said, not dropped
            // without a word (review before 1.2.0).
            if (row.Mod is null)
                Report(StatusMessage.Error(Texts.Get("ModUnknown", r.Text)));
            row.Min = r.Min?.ToString() ?? "";
        }
        return row;
    }

    // A group at the end, or right after another one.
    private GroupViewModel AddGroup(GroupKind kind, GroupViewModel? after = null)
    {
        var group = new GroupViewModel(kind, () =>
        {
            NotifyRules();
            Changed();
        }, () => Row(), CanAddRule, RemoveGroup, g => WithRow(AddGroup(GroupKind.Or, g)));
        if (after is not null && Groups.IndexOf(after) is var at and >= 0)
            Groups.Insert(at + 1, group);
        else
            Groups.Add(group);
        Renumber();
        return group;
    }

    // A group made with a click comes with its first row: nobody adds an empty group. A
    // loaded profile gets no empty row.
    private void WithRow(GroupViewModel group) => group.Rules.Add(group.Row());

    private GroupViewModel LastGroup() => Groups.Count > 0 ? Groups[^1] : AddGroup(GroupKind.And);

    // A group goes; the last one stays, emptied and "and" again (the first group's default).
    private void RemoveGroup(GroupViewModel group)
    {
        if (Groups.Count == 1)
        {
            group.Rules.Clear();
            group.Value = GroupKind.And;
        }
        else
            Groups.Remove(group);
        Renumber();
        NotifyRules();
    }

    private void Renumber()
    {
        for (int i = 0; i < Groups.Count; i++)
            Groups[i].Number = i + 1;
    }

    private void NotifyRules()
    {
        AddRuleCommand.NotifyCanExecuteChanged();
        foreach (var g in Groups)
            g.AddRuleCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(Rules));
    }

    // The profile's groups into the tab; none: one empty "and" group.
    protected void LoadGroups(IEnumerable<(GroupKind Kind, IEnumerable<(string Text, int? Min)> Rules)> groups)
    {
        shownProfile++;
        Groups.Clear();
        foreach (var (kind, rules) in groups)
        {
            var group = AddGroup(kind);
            foreach (var rule in rules)
            {
                var row = Row(rule);
                row.Excludes = kind == GroupKind.Not;
                group.Rules.Add(row);
            }
        }
        if (Groups.Count == 0)
            AddGroup(GroupKind.And);
        NotifyRules();
        ProfilePrice = "";
        ProfilePriceNote = "";
        ProfilePriceIcon = null;
    }

    // Every change: the profile in the list follows, the search text is built anew, and a
    // price shown no longer fits the fields (it is asked for with "Refresh price" only).
    protected void Changed()
    {
        if (Loading)
            return;
        Store();
        Search = BuildSearch(Profile());
        ShowPrice();
    }

    // The search text and the price from the fields as they stand, without writing the
    // profile (a kind of tablet just looked at has none yet).
    protected void Refresh()
    {
        Search = BuildSearch(Profile());
        ShowPrice();
    }

    // Prices asked for in this run, by profile as it was asked: shown again when the profile
    // is chosen again unchanged; anything else shows the hint to ask. (21.10.2026: the
    // trade site is asked only on "Refresh price" — choosing, saving or opening asks
    // nothing; the site's limit is shared with the live price in the game.)
    private readonly Dictionary<TProfile, (string Price, string Unit, int Listings, string When)?> asked = [];

    private void ShowPrice()
    {
        if (asked.TryGetValue(Profile(), out var known))
        {
            ProfilePrice = known?.Price ?? "–";
            ProfilePriceIcon = known is { } q ? IconFor?.Invoke(q.Unit) : null;
            ProfilePriceNote = known is { } k ? Texts.Count("WaystoneListings", k.Listings) + " · " + k.When : Texts.Get("WaystoneProfileNoPrice");
            return;
        }
        ProfilePrice = "";
        ProfilePriceIcon = null;
        ProfilePriceNote = Texts.Get("ProfilePriceAsk");
        OnPropertyChanged(nameof(ProfilePriceIcon));
    }

    // "✓": the tab's settings written on their own; then the price of the profile is asked for.
    // false: nothing was written (a field holds no number, or the file could not be written).
    private bool SaveProfile()
    {
        var (valid, settings) = Current();
        if (!valid)
        {
            Report(StatusMessage.Error(Texts.Get("InvalidNotANumber")));
            return false;
        }
        return Persist(settings);
    }

    // The tab's settings written on their own and taken as the saved state. false: the file could
    // not be written (the status line says why).
    protected bool Persist(TSettings settings, string? told = null)
    {
        try
        {
            store?.Invoke(settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Report(StatusMessage.Error(Texts.Get("SettingsNotSaved", e.Message)));
            return false;
        }
        Accept(settings);
        Saved?.Invoke(settings);
        Report(StatusMessage.Ok(told ?? SavedMessage()));
        return true;
    }

    // Asked when another profile is chosen while this one has changes not saved: true save
    // them, false throw them away, null stay on this profile. The program asks in its own
    // window (AskWindow); null (tests that do not set it): throw them away.
    public Func<string, bool?>? AskSave { get; set; }

    // The same question on "Exit" (worded for leaving the program); null: throw them away.
    // Not on changing tabs: the changes stay in the tab, nothing is lost (21.10.2026).
    public Func<string, bool?>? AskQuit { get; set; }

    // The profile the tab shows (its name, or the tablet's kind); null: none.
    protected abstract string? ShownName { get; }

    // Before the program ends: true, go ahead (saved, thrown away, or nothing to save);
    // false, the program stays (cancelled, or the save failed).
    public bool Quitting() => ShownName is not { } name || Leave(name, AskQuit);

    // What was saved last (this tab's ✓ or the settings' Save): the profiles changes are
    // measured against.
    public abstract void Accept(TSettings settings);

    // Whether the profile shown differs from its saved state.
    protected abstract bool Unsaved();

    // The profile shown back to its saved state.
    protected abstract void Revert();

    // The fields anew from the profile as it stands (after Revert: the saved state, not what
    // was typed; review before 1.2.0).
    protected abstract void Reshow();

    // The tolerance as saved: a change of it counts as not saved, and Discard takes it back.
    private int savedTolerance = Core.Market.Tolerance.Default;

    protected void AcceptTolerance(int value) => savedTolerance = value;

    protected bool ToleranceChanged => !int.TryParse(tolerance.Trim(), out int now) || now != savedTolerance;

    protected void RevertTolerance() => Tolerance = savedTolerance.ToString();

    // Before another profile is chosen: true, go ahead (saved, thrown away, or nothing to
    // save); false, stay (cancelled, or the save failed).
    protected bool Leave(string name) => Leave(name, AskSave);

    private bool Leave(string name, Func<string, bool?>? asking)
    {
        if (!Unsaved())
            return true;
        bool? answer = asking is { } ask ? ask(name) : false;
        if (answer is null)
            return false;
        if (answer.Value)
            return SaveProfile();
        Revert();
        Reshow();
        return true;
    }

    // "Refresh price" pressed before poe.ninja's rates were there: asked again after a while;
    // the timer fires on its own thread, the asking runs on the window's.
    private void RetryPrice(TimeSpan after)
    {
        priceTimer?.Dispose();
        Log?.Invoke($"profile price: asking again in {after.TotalSeconds:0.#} s");
        // For the profile it was asked for: another one shown meanwhile ends the waiting, else the
        // timer asked the trade site for whatever profile stood there then (review before 1.2.0).
        int shown = shownProfile;
        priceTimer = Clock.CreateTimer(_ => Dispatch(() =>
        {
            if (shown == shownProfile)
                _ = PriceAsync();
        }), null, after, Timeout.InfiniteTimeSpan);
    }

    private async Task PriceAsync()
    {
        if (PriceLookup is null || pricing)
            return;
        if (PricesReady is { } ready && !ready())
        {
            Log?.Invoke("profile price: rates not loaded yet");
            ProfilePriceNote = Texts.Get("WaystonePricesLoading");
            RetryPrice(RatesRetry);
            return;
        }
        Log?.Invoke("profile price: asking");
        pricing = true;
        PriceCommand.NotifyCanExecuteChanged();
        ProfilePriceNote = Texts.Get("WaystonePriceAsking");
        int shown = shownProfile;
        var profile = Profile();
        try
        {
            var answer = await PriceLookup(profile);
            Log?.Invoke(answer is { } got ? $"profile price: {got.Price} {got.Unit}, {got.Listings} listings" : "profile price: none");
            string when = Clock.GetLocalNow().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            asked[profile] = answer is { } q ? (q.Price, q.Unit, q.Listings, when) : null;
            // Another profile came meanwhile: its own price (or the hint) stays.
            if (shown != shownProfile)
                return;
            ShowPrice();
        }
        catch (Exception e) when (Poe2Pricer.Core.Market.TradeClient.IsFault(e))
        {
            // Not asked (the site's limit, no rates yet) or failed: the reason instead of a
            // price, nothing kept.
            Log?.Invoke("profile price: " + e.Message);
            if (shown != shownProfile)
                return;
            ProfilePrice = "–";
            ProfilePriceIcon = null;
            ProfilePriceNote = e.Message;
        }
        finally
        {
            pricing = false;
            PriceCommand.NotifyCanExecuteChanged();
        }
    }

    protected virtual void Reworded()
    {
        foreach (var group in Groups)
            group.Reword();
        OnPropertyChanged(nameof(SearchLength));
    }

    protected static string Shown(int? value) => value?.ToString() ?? "";

    protected static int? Int(string text) => int.TryParse(text.Trim(), out int n) ? n : null;

    protected static bool Bad(string text) => text.Trim().Length > 0 && !int.TryParse(text.Trim(), out _);
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poe2Pricer.Core;
using Poe2Pricer.Core.Tablets;

namespace Poe2Pricer.Ui.ViewModels;

// A kind of tablet in the list of the Tablets tab, marked when it has a profile.
public sealed class KindChoice(string name) : ObservableObject
{
    private bool hasProfile;
    private string? shown;

    public string Name { get; } = name;

    // The kind in the words of the game client ("Bestrahlte Tafel"); null: Name.
    public string? Shown
    {
        get => shown;
        set
        {
            if (SetProperty(ref shown, value))
            {
                OnPropertyChanged(nameof(Text));
                OnPropertyChanged(nameof(Label));
            }
        }
    }

    public bool HasProfile
    {
        get => hasProfile;
        set
        {
            if (SetProperty(ref hasProfile, value))
            {
                OnPropertyChanged(nameof(Text));
                OnPropertyChanged(nameof(Marker));
            }
        }
    }

    public string Text => (HasProfile ? "● " : "    ") + (Shown ?? Name);

    // The list draws the mark in a column of its own (MainWindow.xaml), so that the names line
    // up with and without one: four spaces are not as wide as "● " (OFFEN Z7).
    public string Marker => HasProfile ? "●" : "";
    public string Label => Shown ?? Name;

    public override string ToString() => Text;
}

// The Tablets tab: one profile per kind of tablet, the kind list choosing it; uses left at
// least this many, the mods wanted (four at most, as many as a tablet carries) and not
// wanted; from them the search text, the profile's price and the live price at a tablet's
// tooltip (ProfileTabViewModel). A kind has a profile as long as a field of it is set; ✕
// clears the fields, and its tablets are then asked for exactly as they are.
public sealed class TabletsViewModel : ProfileTabViewModel<TabletProfile, TabletSettings>
{
    private readonly Dictionary<string, TabletProfile> profiles = [];
    private Dictionary<string, TabletProfile> saved = [];
    private string selectedKind = "";
    private string usesMin = "";
    private bool usesMinInvalid;

    public TabletsViewModel(TabletSettings settings, Action<StatusMessage> report, TimeProvider? clock = null, Action<TabletSettings>? store = null)
        : base(report, clock, store, settings.Tolerance)
    {
        foreach (var profile in settings.Profiles)
            if (TabletMods.KindNamed(profile.Kind) is { } kind && profile.ConditionCount() > 0)
                profiles[kind.Name] = profile with { Kind = kind.Name };
        Kinds = TabletMods.Kinds.Select(k => new KindChoice(k.Name) { HasProfile = profiles.ContainsKey(k.Name) }).ToArray();
        Accept(settings);
        RemoveProfileCommand = new RelayCommand(() =>
        {
            Loading = true;
            UsesMin = "";
            LoadGroups([]);
            Loading = false;
            Changed();
        });
        SelectedKind = profiles.Keys.FirstOrDefault() ?? Kinds[0].Name;
    }

    public IReadOnlyList<KindChoice> Kinds { get; private set; }
    public IRelayCommand RemoveProfileCommand { get; }

    public TabletKind Kind => TabletMods.KindNamed(selectedKind) ?? TabletMods.Kinds[0];

    // Whether the chosen kind has a profile (a field set).
    public bool KindHasProfile => profiles.ContainsKey(selectedKind);

    protected override IReadOnlyList<ModChoice> Choices => ModChoice.Tablets(Kind, GameLanguage);

    protected override void ClientWorded()
    {
        foreach (var choice in Kinds)
            choice.Shown = GameLanguage == Core.GameLanguage.German ? TabletMods.KindNamed(choice.Name)?.De : null;
        // In the order of the names shown: German names in English order looked unsorted.
        Kinds = Kinds.OrderBy(k => k.Shown ?? k.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        OnPropertyChanged(nameof(Kinds));
        OnPropertyChanged(nameof(SelectedKind));
    }

    protected override string BuildSearch(TabletProfile profile) => TabletSearch.Build(profile, GameLanguage);

    protected override string SavedMessage() => Texts.Get("TabletProfileSaved", selectedKind);

    // Four rows a group at most: a tablet carries four mods. Groups as many as wanted (an "or"
    // group of four and a "not" group beside it make sense together).
    protected override bool CanAddRule(GroupViewModel group) => group.Rules.Count < TabletProfile.MaxRules;

    public string SelectedKind
    {
        get => selectedKind;
        set
        {
            if (value is null || !Kinds.Any(k => k.Name == value) || value == selectedKind)
                return;
            if (selectedKind.Length > 0 && !Leave(selectedKind))
            {
                Dispatch(() => OnPropertyChanged(nameof(SelectedKind)));
                return;
            }
            SetProperty(ref selectedKind, value);
            Load(profiles.GetValueOrDefault(value));
        }
    }

    public string UsesMin { get => usesMin; set { SetProperty(ref usesMin, value); UsesMinInvalid = false; Changed(); } }

    public bool UsesMinInvalid { get => usesMinInvalid; private set => SetProperty(ref usesMinInvalid, value); }

    public override TabletProfile Profile() =>
        new()
        {
            Kind = selectedKind,
            UsesMin = Int(UsesMin),
            Groups = GroupsAs((text, min) => new TabletModRule(text, min)),
        };

    public override (bool Valid, TabletSettings Settings) Current()
    {
        UsesMinInvalid = Bad(UsesMin) || Int(UsesMin) is < 1 or > 10;
        int tol = ToleranceValue();
        bool rows = RowsInvalid();
        Store();
        return (!(UsesMinInvalid || ToleranceInvalid || rows), new TabletSettings { Profiles = profiles.Values.ToArray(), Tolerance = tol });
    }

    private void Load(TabletProfile? profile)
    {
        Loading = true;
        UsesMin = Shown(profile?.UsesMin);
        LoadGroups((profile?.Groups ?? []).Select(g => (g.Kind, g.Rules.Select(r => (r.Text, r.Min)))));
        Loading = false;
        Refresh();
    }

    // The kind's profile follows the fields: one with a condition stays, an empty one goes.
    protected override void Store()
    {
        var profile = Profile();
        if (profile.ConditionCount() > 0)
            profiles[selectedKind] = profile;
        else
            profiles.Remove(selectedKind);
        Kinds.First(k => k.Name == selectedKind).HasProfile = profile.ConditionCount() > 0;
        OnPropertyChanged(nameof(KindHasProfile));
    }

    public override void Accept(TabletSettings settings)
    {
        saved = settings.Profiles.Where(p => p.ConditionCount() > 0 && TabletMods.KindNamed(p.Kind) is not null)
            .GroupBy(p => TabletMods.KindNamed(p.Kind)!.Name).ToDictionary(g => g.Key, g => g.Last() with { Kind = g.Key });
        AcceptTolerance(settings.Tolerance);
    }

    protected override void Reshow() => Load(profiles.GetValueOrDefault(selectedKind));

    protected override string? ShownName => selectedKind.Length > 0 ? selectedKind : null;

    protected override bool Unsaved()
    {
        var now = Profile();
        return !Equals(now.ConditionCount() > 0 ? now : null, saved.GetValueOrDefault(selectedKind)) || ToleranceChanged;
    }

    protected override void Revert()
    {
        if (saved.TryGetValue(selectedKind, out var before))
            profiles[selectedKind] = before;
        else
            profiles.Remove(selectedKind);
        Kinds.First(k => k.Name == selectedKind).HasProfile = profiles.ContainsKey(selectedKind);
        RevertTolerance();
        OnPropertyChanged(nameof(KindHasProfile));
    }

}

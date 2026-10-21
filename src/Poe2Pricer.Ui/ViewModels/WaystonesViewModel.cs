using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Poe2Pricer.Core;
using Poe2Pricer.Core.Waystones;

namespace Poe2Pricer.Ui.ViewModels;

public sealed record CorruptedOption(CorruptedChoice Value, string Text)
{
    public override string ToString() => Text;
}

// The Waystones tab: profiles, the summary values at least this high,
// tier and revives, corrupted or not, the mods not wanted and wanted; from them the search
// text, the profile's price and the live price at the tooltip (ProfileTabViewModel).
public sealed class WaystonesViewModel : ProfileTabViewModel<WaystoneProfile, WaystoneSettings>
{
    private readonly Dictionary<string, WaystoneProfile> profiles = [];
    private Dictionary<string, WaystoneProfile> saved = [];
    private string? selected;
    private string profileText = "";
    private string tierMin = "", tierMax = "", revivesMin = "", revivesMax = "";
    private string itemRarity = "", packSize = "", monsterRarity = "", effectiveness = "", dropChance = "";
    private bool tierMinInvalid, tierMaxInvalid, revivesMinInvalid, revivesMaxInvalid, itemRarityInvalid, packSizeInvalid, monsterRarityInvalid, effectivenessInvalid, dropChanceInvalid;
    private CorruptedOption corrupted;

    public WaystonesViewModel(WaystoneSettings settings, Action<StatusMessage> report, TimeProvider? clock = null, Action<WaystoneSettings>? store = null)
        : base(report, clock, store, settings.Tolerance)
    {
        foreach (var profile in settings.Profiles)
            profiles[profile.Name] = profile;
        if (profiles.Count == 0)
            profiles["default"] = new WaystoneProfile { Name = "default" };
        ProfileNames = new ObservableCollection<string>(profiles.Keys);
        CorruptedOptions = Options();
        corrupted = CorruptedOptions[0];
        RemoveProfileCommand = new RelayCommand(RemoveProfile);
        Accept(settings);
        SelectedProfile = settings.Active is { } active && profiles.ContainsKey(active) ? active : ProfileNames[0];
    }

    public ObservableCollection<string> ProfileNames { get; }
    public IReadOnlyList<CorruptedOption> CorruptedOptions { get; private set; }
    public IRelayCommand RemoveProfileCommand { get; }

    protected override IReadOnlyList<ModChoice> Choices => ModChoice.ForWaystones(GameLanguage);

    protected override string BuildSearch(WaystoneProfile profile) => SearchRegex.Build(profile, GameLanguage);

    // Eight rows a group at most: a waystone carries eight mods.
    protected override bool CanAddRule(GroupViewModel group) => group.Rules.Count < WaystoneProfile.MaxRules;

    protected override string SavedMessage() => Texts.Get("WaystoneProfileSaved", selected ?? "");

    public string? SelectedProfile
    {
        get => selected;
        set
        {
            if (value is null || !profiles.ContainsKey(value) || value == selected)
                return;
            if (selected is not null && !Leave(selected))
            {
                // Stay: the list shows the profile again (after the list has let go of the click).
                Dispatch(() => OnPropertyChanged(nameof(SelectedProfile)));
                return;
            }
            SetProperty(ref selected, value);
            Load(profiles[value]);
        }
    }

    // The text of the profile list: a saved name (chosen from the list) or a new one typed
    // in, which becomes a profile with the fields as they stand when the settings are saved
    // (Current).
    public string ProfileText
    {
        get => profileText;
        set => SetProperty(ref profileText, value);
    }

    // A typed name that is not a profile yet: made one from the fields, and chosen. The
    // profile the fields came from keeps what was saved of it: "save as" (21.10.2026).
    private void AdoptTyped()
    {
        string typed = ProfileText.Trim();
        if (typed.Length == 0 || profiles.ContainsKey(typed) || selected is null)
            return;
        Store();
        profiles[typed] = profiles[selected] with { Name = typed };
        if (saved.TryGetValue(selected, out var before))
            profiles[selected] = before;
        ProfileNames.Add(typed);
        selected = typed;
        OnPropertyChanged(nameof(SelectedProfile));
        RemoveProfileCommand.NotifyCanExecuteChanged();
    }

    public string TierMin { get => tierMin; set { SetProperty(ref tierMin, value); TierMinInvalid = false; Changed(); } }
    public string TierMax { get => tierMax; set { SetProperty(ref tierMax, value); TierMaxInvalid = false; Changed(); } }
    public string RevivesMin { get => revivesMin; set { SetProperty(ref revivesMin, value); RevivesMinInvalid = false; Changed(); } }
    public string RevivesMax { get => revivesMax; set { SetProperty(ref revivesMax, value); RevivesMaxInvalid = false; Changed(); } }
    public string ItemRarity { get => itemRarity; set { SetProperty(ref itemRarity, value); ItemRarityInvalid = false; Changed(); } }
    public string PackSize { get => packSize; set { SetProperty(ref packSize, value); PackSizeInvalid = false; Changed(); } }
    public string MonsterRarity { get => monsterRarity; set { SetProperty(ref monsterRarity, value); MonsterRarityInvalid = false; Changed(); } }
    public string Effectiveness { get => effectiveness; set { SetProperty(ref effectiveness, value); EffectivenessInvalid = false; Changed(); } }
    public string DropChance { get => dropChance; set { SetProperty(ref dropChance, value); DropChanceInvalid = false; Changed(); } }

    public bool TierMinInvalid { get => tierMinInvalid; private set => SetProperty(ref tierMinInvalid, value); }
    public bool TierMaxInvalid { get => tierMaxInvalid; private set => SetProperty(ref tierMaxInvalid, value); }
    public bool RevivesMinInvalid { get => revivesMinInvalid; private set => SetProperty(ref revivesMinInvalid, value); }
    public bool RevivesMaxInvalid { get => revivesMaxInvalid; private set => SetProperty(ref revivesMaxInvalid, value); }
    public bool ItemRarityInvalid { get => itemRarityInvalid; private set => SetProperty(ref itemRarityInvalid, value); }
    public bool PackSizeInvalid { get => packSizeInvalid; private set => SetProperty(ref packSizeInvalid, value); }
    public bool MonsterRarityInvalid { get => monsterRarityInvalid; private set => SetProperty(ref monsterRarityInvalid, value); }
    public bool EffectivenessInvalid { get => effectivenessInvalid; private set => SetProperty(ref effectivenessInvalid, value); }
    public bool DropChanceInvalid { get => dropChanceInvalid; private set => SetProperty(ref dropChanceInvalid, value); }

    public CorruptedOption Corrupted
    {
        get => corrupted;
        set
        {
            if (value is not null && SetProperty(ref corrupted, value))
                Changed();
        }
    }

    // The profile as the fields describe it; a field that is not a number counts as empty
    // here and is marked when saving (Current).
    public override WaystoneProfile Profile() =>
        new()
        {
            Name = selected ?? "default",
            TierMin = Int(TierMin), TierMax = Int(TierMax), RevivesMin = Int(RevivesMin), RevivesMax = Int(RevivesMax),
            ItemRarity = Int(ItemRarity), PackSize = Int(PackSize), MonsterRarity = Int(MonsterRarity), Effectiveness = Int(Effectiveness), DropChance = Int(DropChance),
            Corrupted = Corrupted.Value,
            Groups = GroupsAs((text, min) => new WaystoneModRule(text, min)),
        };

    // What "Save" takes: every profile, the one in use, the tooltip's price and tolerance.
    public override (bool Valid, WaystoneSettings Settings) Current()
    {
        TierMinInvalid = Bad(TierMin);
        TierMaxInvalid = Bad(TierMax);
        RevivesMinInvalid = Bad(RevivesMin);
        RevivesMaxInvalid = Bad(RevivesMax);
        ItemRarityInvalid = Bad(ItemRarity);
        PackSizeInvalid = Bad(PackSize);
        MonsterRarityInvalid = Bad(MonsterRarity);
        EffectivenessInvalid = Bad(Effectiveness);
        DropChanceInvalid = Bad(DropChance);
        int tol = ToleranceValue();
        bool rows = RowsInvalid();
        bool valid = !(TierMinInvalid || TierMaxInvalid || RevivesMinInvalid || RevivesMaxInvalid || ItemRarityInvalid || PackSizeInvalid || MonsterRarityInvalid || EffectivenessInvalid || DropChanceInvalid || ToleranceInvalid || rows);
        // A new name typed takes the fields only when they are valid: else the save fails and
        // the profile the fields came from stays as it was (review before 1.2.0).
        if (valid)
            AdoptTyped();
        Store();
        return (valid, new WaystoneSettings { Profiles = profiles.Values.ToArray(), Active = selected, Tolerance = tol });
    }

    private void Load(WaystoneProfile profile)
    {
        Loading = true;
        ProfileText = profile.Name;
        TierMin = Shown(profile.TierMin);
        TierMax = Shown(profile.TierMax);
        RevivesMin = Shown(profile.RevivesMin);
        RevivesMax = Shown(profile.RevivesMax);
        ItemRarity = Shown(profile.ItemRarity);
        PackSize = Shown(profile.PackSize);
        MonsterRarity = Shown(profile.MonsterRarity);
        Effectiveness = Shown(profile.Effectiveness);
        DropChance = Shown(profile.DropChance);
        Corrupted = CorruptedOptions.First(o => o.Value == profile.Corrupted);
        LoadGroups(profile.Groups.Select(g => (g.Kind, g.Rules.Select(r => (r.Text, r.Min)))));
        Loading = false;
        Changed();
    }

    protected override void Store()
    {
        if (selected is not null)
            profiles[selected] = Profile();
    }

    public override void Accept(WaystoneSettings settings)
    {
        saved = settings.Profiles.GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.Last());
        AcceptTolerance(settings.Tolerance);
    }

    protected override string? ShownName => selected;

    // A profile never saved (the "default" of a first start, a new name) counts as changed
    // only once something is filled in: an empty one is nothing to lose.
    protected override bool Unsaved() =>
        selected is not null && !Equals(Profile(), saved.GetValueOrDefault(selected) ?? new WaystoneProfile { Name = selected }) || ToleranceChanged;

    // A profile never saved goes back to empty: Discard kept what was typed (review before 1.2.0).
    protected override void Revert()
    {
        if (selected is not null)
            profiles[selected] = saved.TryGetValue(selected, out var before) ? before : new WaystoneProfile { Name = selected };
        RevertTolerance();
    }

    protected override void Reshow()
    {
        if (selected is not null && profiles.TryGetValue(selected, out var profile))
            Load(profile);
    }

    // The chosen profile goes; the last one gone leaves an empty "default".
    // Removing is written at once, as the saved profiles without this one: a profile removed and
    // not saved came back at the next start, and "Exit" did not ask (review before 1.2.0).
    // Changes not saved to other profiles stay unsaved.
    private void RemoveProfile()
    {
        if (selected is null)
            return;
        string gone = selected;
        Persist(new WaystoneSettings { Profiles = saved.Values.Where(p => p.Name != gone).ToArray(), Active = null, Tolerance = ToleranceValue() }, Texts.Get("ProfileRemoved", gone));
        profiles.Remove(selected);
        ProfileNames.Remove(selected);
        selected = null;
        if (ProfileNames.Count == 0)
        {
            profiles["default"] = new WaystoneProfile { Name = "default" };
            ProfileNames.Add("default");
        }
        SelectedProfile = ProfileNames[0];
    }

    protected override void Reworded()
    {
        var value = Corrupted.Value;
        CorruptedOptions = Options();
        OnPropertyChanged(nameof(CorruptedOptions));
        corrupted = CorruptedOptions.First(o => o.Value == value);
        OnPropertyChanged(nameof(Corrupted));
        base.Reworded();
    }

    private static CorruptedOption[] Options() =>
        [new(CorruptedChoice.Any, Texts.Get("WaystoneCorruptedAny")), new(CorruptedChoice.Yes, Texts.Get("WaystoneCorruptedYes")), new(CorruptedChoice.No, Texts.Get("WaystoneCorruptedNo"))];
}

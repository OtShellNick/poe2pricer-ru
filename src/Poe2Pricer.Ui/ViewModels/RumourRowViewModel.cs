using CommunityToolkit.Mvvm.ComponentModel;
using Poe2Pricer.Core;

namespace Poe2Pricer.Ui.ViewModels;

// One row per rumour: name, map, mods, community rating, own rating ("–" = community counts),
// note. In a row of the player's own, name, map and mods are free as well.
public sealed class RumourRowViewModel : ObservableObject
{
    public const string Community = "–";
    public static IReadOnlyList<string> OwnChoices { get; } = [Community, .. RumourBook.Ratings];

    private readonly Rumour entry;
    private string name;
    private string map;
    private string mods;
    private string own;
    private string note;

    public RumourRowViewModel(Rumour entry)
    {
        this.entry = entry;
        name = entry.Name;
        map = entry.Map;
        mods = entry.Mods;
        own = entry.Own.Length > 0 ? entry.Own : Community;
        note = entry.Note;
    }

    public bool Custom => entry.Custom;
    public string Rating => entry.Rating;

    // The remark on the rating, shown as its tooltip.
    public string Remark => entry.Remark;
    public string? RemarkTip => entry.Remark.Length > 0 ? entry.Remark : null;

    // The name's tooltip: the warning where there is one, else the whole name.
    public string NameTip => HasWarning ? entry.Warning : entry.Name;

    // Red name with the warning as tooltip, like the source shows it.
    public string Warning => entry.Warning;
    public bool HasWarning => entry.Warning.Length > 0;

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public string Map
    {
        get => map;
        set => SetProperty(ref map, value);
    }

    public string Mods
    {
        get => mods;
        set => SetProperty(ref mods, value);
    }

    public string Own
    {
        get => own;
        set => SetProperty(ref own, value);
    }

    public string Note
    {
        get => note;
        set => SetProperty(ref note, value);
    }

    // The entry as the row stands now. Null: a row of the player's own without a name — it
    // goes when the ratings are saved.
    public Rumour? Read()
    {
        var next = entry with { Own = Own is null or Community ? "" : Own, Note = Note.Trim() };
        if (entry.Custom)
            next = next with { Name = Name.Trim(), Map = Map.Trim(), Mods = Mods.Trim() };
        return !entry.Custom || next.Name.Length > 0 ? next : null;
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poe2Pricer.Core;

namespace Poe2Pricer.Ui.ViewModels;

// The Rumours tab: the rumour list with the player's own ratings and notes, and the community
// sheet it comes from. Own rows can be added; they
// are kept when the sheet drops a rumour. The rows belong to the window's thread: Show and the
// commands are called there.
public sealed class RumoursViewModel : ObservableObject
{
    private RumourBook book;
    private string source = "";

    public RumoursViewModel(RumourBook book)
    {
        this.book = book;
        ReloadCommand = new RelayCommand(() => Reload?.Invoke());
        AddRowCommand = new RelayCommand(AddRow);
        RemoveRowCommand = new RelayCommand<RumourRowViewModel>(row => Rows.Remove(row!), row => row is { Custom: true });
        Fill();
        Weights = new ObservableCollection<TierWeightViewModel>(RumourBook.Ratings.Select(t => new TierWeightViewModel(t, book.Weights.Of(t))));
        // The source line in the new language; the rows stay (what was typed must not go).
        Texts.Changed += Sourced;
    }

    // The weight of each tier for the expedition tier (TierWeights), one field per tier.
    public ObservableCollection<TierWeightViewModel> Weights { get; }

    public ObservableCollection<RumourRowViewModel> Rows { get; } = [];
    public IRelayCommand ReloadCommand { get; }
    public IRelayCommand AddRowCommand { get; }

    // The "✕" at a row of the player's own: the row goes from the list, and from the book with
    // the next save, like everything on this page. Rows of the sheet
    // cannot be removed.
    public IRelayCommand<RumourRowViewModel> RemoveRowCommand { get; }

    // Reload: the community sheet is to be fetched again. Changed: the own ratings were
    // saved and count in the game from now on. RowAdded: the list should show its end.
    public event Action? Reload;
    public event Action? Changed;
    public event Action? RowAdded;

    // Where the sheet comes from, its date and the number of its entries.
    public string Source
    {
        get => source;
        private set => SetProperty(ref source, value);
    }

    // Another table (loaded anew): every row is built again, what was typed and not saved goes.
    public void Show(RumourBook next)
    {
        book = next;
        Fill();
    }

    private void Fill()
    {
        Sourced();
        Rows.Clear();
        foreach (var e in book.Entries)
            Rows.Add(new RumourRowViewModel(e));
    }

    private void Sourced()
    {
        Source = Texts.Get(
            "Source",
            Texts.Get(book.Source.Length > 0 ? book.Source : "SourceNone"),
            book.SheetDate is { } d ? Texts.Get("SourceUpdated", d) : Texts.Get("SourceNotLoaded"),
            Texts.Count("Entries", book.Entries.Count(e => !e.Custom))
        );
    }

    // Own ratings, notes and rows into the book. The rows stay as they stand: an own row
    // without a name is still shown until the list is built again.
    // Returns why they did not reach the file, if they did not; they count either way.
    public Told? Save()
    {
        var notWritten = book.SetOwn(Rows.Select(r => r.Read()).OfType<Rumour>().ToList());
        // A weight that is not a number: the ratings are saved, the weights stay as they were,
        // the field turns red and the status line says so.
        var read = new Dictionary<string, double>();
        foreach (var w in Weights)
        {
            w.Invalid = !w.Read(out double value);
            if (!w.Invalid)
                read[w.Tier] = value;
        }
        // The weights are applied and written whatever became of the ratings (review 19.10.:
        // a ratings file that could not be written used to drop the weights without a word).
        Told? weightsNotWritten = read.Count < Weights.Count ? new("InvalidNotANumber") : book.SetWeights(new TierWeights(read));
        Changed?.Invoke();
        return notWritten ?? weightsNotWritten;
    }

    // An empty row of the player's own at the end of the list; it goes into the book with the
    // next save, once it has a name. Adding a row saves nothing by itself: the list is not
    // built anew from the book, so what was typed so far is kept.
    private void AddRow()
    {
        Rows.Add(new RumourRowViewModel(new Rumour("", "", "", "", Custom: true)));
        RowAdded?.Invoke();
    }
}

// One tier and its weight as typed; Invalid after a save that could not read it.
public sealed class TierWeightViewModel(string tier, double weight) : ObservableObject
{
    private string text = weight.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private bool invalid;

    public string Tier { get; } = tier;

    public string Text
    {
        get => text;
        set
        {
            if (SetProperty(ref text, value))
                Invalid = false;
        }
    }

    public bool Invalid
    {
        get => invalid;
        set => SetProperty(ref invalid, value);
    }

    public bool Read(out double value) => Numbers.TryParse(Text, out value);
}

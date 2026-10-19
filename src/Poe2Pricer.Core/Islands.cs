using Tune = Poe2Pricer.Core.Tuning.Islands;

namespace Poe2Pricer.Core;

// The rumours of an uncharted island, collected over the openings of its panel: the game shows
// three of them at a time and rolls them anew when an item is moved in the inventory, so
// the player closes and opens the panel to see more. Which island a panel belongs to is told
// by the sea chart around it (IslandMarks). Kept while the program runs, nothing is written:
// an island worth remembering is bookmarked in the game.
public sealed class Island
{
    internal Island(IslandPrint print)
    {
        Print = print;
    }

    // The chart the next opening is matched against: as last seen (the map most likely stands
    // as it did), unless that reading was thin (Follow); within one opening the reading with
    // the most points (Keep).
    public IslandPrint Print { get; private set; }

    // The same opening read again: the print with more points stays — a print read while
    // the panel flies in or a tooltip lies over the water shows less of the same chart.
    internal void Keep(IslandPrint print)
    {
        if (print.Points.Length >= Print.Points.Length)
            Print = print;
    }

    // A new opening: the view may have moved a little (a step of zoom, a short drag), and the
    // next opening is more like this one than like the first — the print follows, so that
    // small moves do not add up against a fixed reference (review 19.10.). A thin reading
    // (fewer than ThinPrint of the stored points) does not replace a rich one (review 19.10.).
    internal void Follow(IslandPrint print)
    {
        if (print.Points.Length >= Tune.ThinPrint * Print.Points.Length)
            Print = print;
    }

    // How often the panel was rolled at this island: openings whose lines differ from the
    // last reading. Closing and opening the panel without moving an item shows the same three
    // in the same order (diagnoses of 19.10.2026, three openings in a row); a roll that by
    // chance repeats the order goes uncounted, which errs toward "not complete yet".
    public int Rolls { get; internal set; }
    private string?[] lastRows = [];

    // Every rumour read here, by the name the table has for it, in the order they came.
    public IReadOnlyList<string> Seen => seen;
    private readonly List<string> seen = [];

    // The rumours the last roll showed for the first time at this island.
    public IReadOnlyList<string> New { get; internal set; } = [];

    // The panel showed fewer than three rumours: the island has no more than those.
    public bool Complete { get; internal set; }

    // Rolls in a row that showed nothing new.
    public int SinceNew { get; internal set; }

    // Probably every rumour of the island is seen: with three of N drawn alike at every opening,
    // an unseen one would have come up by now with at least 1 − HiddenRumourAtMost — the chance
    // that one more exists and never showed in SinceNew rolls is ((M−2)/(M+1))^SinceNew for
    // M seen. Sure only below three lines (Complete).
    public bool ProbablyComplete =>
        Complete || (seen.Count >= 3 && SinceNew > 0 && Math.Pow((seen.Count - 2) / (double)(seen.Count + 1), SinceNew) <= Tune.HiddenRumourAtMost);

    // rows: the panel's lines in order, null where a line matched no rumour of the table.
    internal void Take(IReadOnlyList<string?> rows, bool opened)
    {
        var fresh = new List<string>();
        foreach (var name in rows)
            if (name is not null && !seen.Contains(name))
            {
                seen.Add(name);
                fresh.Add(name);
            }
        bool rolled = opened && !rows.SequenceEqual(lastRows);
        lastRows = [.. rows];
        if (rolled)
        {
            Rolls++;
            New = fresh;
            SinceNew = fresh.Count > 0 ? 0 : SinceNew + 1;
        }
        else
        {
            New = [.. New.Concat(fresh)];
            if (fresh.Count > 0)
                SinceNew = 0;
        }
    }

    // What the overlay shows about the island: the expedition tier (TierWeights) over every
    // rumour seen, the counts, and the rumours seen here that the panel does not show now
    // ("rotated out"), with their current ratings (book: the table as it stands now, own
    // ratings before the community's).
    public IslandSummary Summary(IEnumerable<string> shown, RumourBook book)
    {
        var entries = seen.Select(name => book.Entries.FirstOrDefault(e => e.Name == name)).OfType<Rumour>().ToArray();
        var showing = shown.ToHashSet();
        return new IslandSummary(
            book.Weights.Tier(entries.Select(e => e.Shown)),
            Rolls,
            seen.Count,
            Complete,
            ProbablyComplete,
            [.. entries.Where(e => !showing.Contains(e.Name))],
            [.. New]
        );
    }
}

// Tier: null when none of the rumours has a rating in the table. Rotated: rumours seen at the
// island that the panel does not show now. New: names shown for the first time at this island
// with this opening.
public sealed record IslandSummary(string? Tier, int Rolls, int Rumours, bool Complete, bool ProbablyComplete, Rumour[] Rotated, string[] New);

// The islands seen since the program started, the one seen last first. A panel is placed at
// the island whose chart fits best (IslandMarks.Match from SameIsland on, with MinimumEvidence
// points in common), else at a new island. Where the panel stands says nothing: in the
// controller UI it always stands in the middle (review 19.10.).
public sealed class IslandBook
{
    private readonly List<Island> islands = [];
    private readonly object gate = new();

    public IReadOnlyList<Island> All
    {
        get
        {
            lock (gate)
                return [.. islands];
        }
    }

    // rows: the panel's lines in order (null: no rumour of the table). opened: the panel
    // appeared after being closed — a roll when its lines differ from the last reading; false:
    // the same opening read again. previous: the island the same opening was placed at before.
    public Island Place(IslandPrint print, IReadOnlyList<string?> rows, bool opened, Island? previous = null)
    {
        lock (gate)
        {
            var found = Match(print);
            if (found is null)
            {
                found = new Island(print);
                if (islands.Count >= Tune.MostIslands)
                    islands.RemoveAt(islands.Count - 1);
            }
            else
            {
                islands.Remove(found);
                found.Follow(print);
            }
            islands.Insert(0, found);
            found.Take(rows, opened && found != previous);
            // Fewer than three rows: the island has no more. Three again later: a row had been
            // lost to the text recognition, not to the island (review 19.10.).
            found.Complete = rows.Count is > 0 and < 3;
            return found;
        }
    }

    // The island the panel stands at, as the book has it, or null: nothing is recorded (the
    // diagnosis, LookContext.Observe).
    public Island? Find(IslandPrint print)
    {
        lock (gate)
            return Match(print);
    }

    // The island whose print matches best, by evidence only; null: none. (Until 19.10. a print
    // without evidence either way fell back to the island seen last when the panel stood where
    // it stood — in the controller UI the panel always stands in the middle, so a thin first
    // picture of the next island was merged into the previous one. Now a print too thin to
    // judge is not placed at all, RumourGameWindow.Place.)
    private Island? Match(IslandPrint print)
    {
        Island? found = null;
        double best = 0;
        foreach (var island in islands)
        {
            var (score, evidence, _) = IslandMarks.Match(print, island.Print);
            if (evidence >= Tune.MinimumEvidence && score >= Tune.SameIsland && score > best)
                (found, best) = (island, score);
        }
        return found;
    }

    // The same opening read again at the same island: the rows as read now, nothing counted.
    // print: the chart as read now (Island.Keep).
    public void Update(Island island, IReadOnlyList<string?> rows, IslandPrint? print = null)
    {
        lock (gate)
        {
            island.Take(rows, opened: false);
            island.Complete = rows.Count is > 0 and < 3;
            if (print is not null)
                island.Keep(print);
        }
    }
}

// The expedition tier: the mean of the weights of the island's rumours, by their ratings
// (own before community), given back as the highest tier whose weight the mean reaches. The
// weights are the player's (Rumours tab); the starting values follow the community overlay
// (S+ 6 … D 2), with S and F filled in between and below.
public sealed record TierWeights(IReadOnlyDictionary<string, double> Weights)
{
    public static readonly TierWeights Standard = new(
        new Dictionary<string, double>
        {
            ["S+"] = 6,
            ["S"] = 5.5,
            ["A+"] = 5,
            ["A"] = 4,
            ["B+"] = 3.5,
            ["B"] = 3,
            ["C"] = 2.5,
            ["D"] = 2,
            ["F"] = 1,
        }
    );

    public double Of(string tier) => Weights.TryGetValue(tier, out double w) ? w : Standard.Weights.GetValueOrDefault(tier, 0);

    // Null without a rated rumour. Ratings the table does not know ("?") do not count.
    public string? Tier(IEnumerable<string> ratings)
    {
        var known = ratings.Where(r => RumourBook.Ratings.Contains(r)).ToArray();
        if (known.Length == 0)
            return null;
        double mean = known.Average(Of);
        return RumourBook.Ratings.OrderByDescending(Of).FirstOrDefault(t => Of(t) <= mean) ?? RumourBook.Ratings[^1];
    }
}

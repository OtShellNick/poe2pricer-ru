using Tune = Poe2Pricer.Core.Tuning.Islands;

namespace Poe2Pricer.Core;

// The rumours of an uncharted island, collected over the openings of its panel: the game shows
// three of them at a time and rolls them anew when an item is moved in the inventory, so
// the player closes and opens the panel to see more. Which island a panel belongs to is told
// by the sea chart around it (IslandMarks). Kept while the program runs, nothing is written:
// an island worth remembering is bookmarked in the game.
public sealed class Island
{
    internal Island(IslandPrint print, Box panel)
    {
        Print = print;
        Panel = panel;
    }

    // The chart as last seen (the next opening most likely shows the map as it is now) and
    // where the panel stood.
    public IslandPrint Print { get; internal set; }
    public Box Panel { get; internal set; }

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
// points in common); with too few points in common for any judgement, at the island seen last
// when the panel stands where it stood; else at a new island.
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
    public Island Place(IslandPrint print, Box panel, IReadOnlyList<string?> rows, bool opened, Island? previous = null)
    {
        lock (gate)
        {
            var found = Match(print, panel);
            if (found is null)
            {
                found = new Island(print, panel);
                if (islands.Count >= Tune.MostIslands)
                    islands.RemoveAt(islands.Count - 1);
            }
            else
                islands.Remove(found);
            islands.Insert(0, found);
            found.Print = print;
            found.Panel = panel;
            found.Take(rows, opened && found != previous);
            if (rows.Count is > 0 and < 3)
                found.Complete = true;
            return found;
        }
    }

    // The island the panel stands at, as the book has it, or null: nothing is recorded (the
    // diagnosis, LookContext.Observe).
    public Island? Find(IslandPrint print, Box panel)
    {
        lock (gate)
            return Match(print, panel);
    }

    // The island whose print matches best; else, without evidence either way, the last one
    // when the panel stands where it stood; null: none.
    private Island? Match(IslandPrint print, Box panel)
    {
        Island? found = null;
        double best = 0;
        bool enough = false;
        foreach (var island in islands)
        {
            var (score, evidence, _) = IslandMarks.Match(print, island.Print);
            enough |= evidence >= Tune.MinimumEvidence;
            if (evidence >= Tune.MinimumEvidence && score >= Tune.SameIsland && score > best)
                (found, best) = (island, score);
        }
        if (found is null && !enough && islands.FirstOrDefault() is { } last && Math.Abs(last.Panel.X - panel.X) <= Tune.SamePanelPlace && Math.Abs(last.Panel.Y - panel.Y) <= Tune.SamePanelPlace)
            found = last;
        return found;
    }

    // The same opening read again at the same island: the rows as read now, nothing counted.
    public void Update(Island island, IReadOnlyList<string?> rows)
    {
        lock (gate)
        {
            island.Take(rows, opened: false);
            if (rows.Count is > 0 and < 3)
                island.Complete = true;
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

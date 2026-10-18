using OpenCvSharp;
using Poe2Pricer.Core.Market;

namespace Poe2Pricer.Core;

// The rumour panel on the atlas: lines read by OCR and matched against the rumour table; the
// overlay draws their ratings beside the panel, no prices.
public sealed class RumourGameWindow(Pipeline pipeline) : IGameWindow
{
    internal Pipeline Pipeline { get; } = pipeline;

    public WindowProfile Profile { get; } = new("Rumours", AutoSwitch.Expedition, GameWindows.FollowEvery);

    public bool IsThere(GameCapture.Frame frame) => Pipeline.Rumours.Locate(frame.Image, popup: frame.Popup) is not null;

    public async Task<IOpenWindow?> LookAsync(GameCapture.Frame frame, LookContext context)
    {
        if (Pipeline.Rumours.Locate(frame.Image, popup: frame.Popup) is not { } panel)
            return null;
        var lines = await Pipeline.Rumours.ReadAsync(frame.Image, panel, context.Rumours, frame.Popup, frame.Language);
        return new Open(this, panel, lines, RumourReader.Signature(frame.Image, panel), frame.Popup);
    }

    // Stale: the glance saw that what was read no longer holds; the follow reads again, and
    // until it has, the glance has nothing more to say.
    // Resting: the panel was seen twice at the same place. In the controller UI it flies in,
    // up to about 100 px within the first tenth of a second, and ratings drawn where the first
    // picture had it would slip into place a moment later. They are drawn once the panel rests.
    // Resized: the game runs at another height than the readers are measured at (GameView).
    // Popup: the game's popup size when the panel was read (the glance gets a picture without it).
    private sealed record Open(RumourGameWindow Kind, RumourPanel Panel, RumourLine[] Lines, double[] Signature, double Popup, bool Stale = false, bool Resting = false) : IOpenWindow
    {
        public IGameWindow Window => Kind;

        // The panel with room to move: in the controller UI it does not stand still at once but
        // drifts a few pixels after it opens; without the room it would be read anew several
        // times in the first half second.
        public Box? GlanceArea
        {
            get
            {
                var seen = Panel.Panel.Union(Panel.Rows);
                int m = Tuning.Rumours.TrackMargin;
                return new Box(seen.X - m, seen.Y - m, seen.Width + 2 * m, seen.Height + 2 * m);
            }
        }

        // Still near its place with the same lines, only moved: the ratings move with it,
        // nothing is cleared or read. No title near its place: the panel is closed or stands at
        // another island — the ratings go at once, the follow looks where it is now. Other
        // lines: read again at once.
        public Glance Glance(Mat frame, PriceBook? ready, LookContext context)
        {
            if (Stale)
                return Core.Glance.Unchanged;
            if (Kind.Pipeline.Rumours.Locate(frame, GlanceArea, Popup) is not { } now)
                return new Glance.Reread(this with { Stale = true }, ClearNow: true);
            if (RumourReader.Changed(RumourReader.Signature(frame, now), Signature))
                return new Glance.Reread(this with { Stale = true }, ClearNow: false);
            int dx = now.Title.X - Panel.Title.X,
                dy = now.Title.Y - Panel.Title.Y;
            // On a resized picture the title is found to a pixel more or less (it is looked
            // for in steps of a hundredth of its size): that is the same place.
            if (Math.Abs(dx) <= Tuning.Rumours.SamePlace && Math.Abs(dy) <= Tuning.Rumours.SamePlace)
            {
                if (Resting)
                    return Core.Glance.Unchanged;
                context.Log("Rumour window at rest, tiers shown");
                return new Glance.Relabel(this with { Resting = true });
            }
            context.Log($"Rumour window moved by {dx}, {dy}");
            return new Glance.Relabel(this with { Panel = now, Lines = Lines.Select(l => l with { CenterY = l.CenterY + dy }).ToArray() });
        }

        // Panel moved (other island) or changed (other rumours) → read again.
        public async Task<IOpenWindow?> FollowAsync(GameCapture.Frame frame, LookContext context)
        {
            var panel = Kind.Pipeline.Rumours.Locate(frame.Image, popup: frame.Popup);
            if (panel is null)
            {
                context.Log("hidden: Rumour window no longer detected");
                return null;
            }
            if (!Stale && Math.Abs(panel.Title.X - Panel.Title.X) <= Tuning.Rumours.SamePlace && Math.Abs(panel.Title.Y - Panel.Title.Y) <= Tuning.Rumours.SamePlace && !RumourReader.Changed(RumourReader.Signature(frame.Image, Panel), Signature))
                return this;
            var lines = await Kind.Pipeline.Rumours.ReadAsync(frame.Image, panel, context.Rumours, frame.Popup, frame.Language);
            context.Log("Rumour window rescanned");
            // Other lines on a panel that rests where it stood: shown at once.
            return new Open(Kind, panel, lines, RumourReader.Signature(frame.Image, panel), frame.Popup, Resting: Resting && panel.Title == Panel.Title);
        }

        // Own ratings changed in the settings window: shown at once. A line without an entry may
        // have one in the changed table (the community table arrived after the panel was read,
        // at the first start; an own row was added for it): the panel is read again with the
        // next follow, so that such lines do not keep their "?" until the panel is closed.
        public IOpenWindow Rebind(LookContext context) =>
            this with
            {
                Lines = Lines.Select(l => l with { Entry = l.Entry is null ? null : context.Rumours.Entries.FirstOrDefault(e => e.Name == l.Entry.Name) ?? l.Entry }).ToArray(),
                Stale = Stale || Lines.Any(l => l.Entry is null),
            };

        public Task<Picture> LabelAsync(GameCapture.Frame frame, Task<PriceBook> prices, LookContext context) =>
            Task.FromResult(
                new Picture(
                    this,
                    [],
                    Resting ? (Panel.Panel, Lines) : null,
                    Texts.Get("RumourStatus", string.Join(", ", Lines.Select(l => l.Entry is { } e ? $"{e.Name} {e.Shown}" : "?"))),
                    $"Rumour window, {Lines.Length} rows, {Lines.Count(l => l.Entry is not null)} matched{(Resting ? "" : " (read; shown once the window is at rest)")}",
                    new
                    {
                        Window = "rumour",
                        Panel,
                        // Only name and shown rating of the matched entry: the table itself stays out of the repo.
                        Lines = Lines.Select(l => new { l.Text, l.CenterY, Entry = l.Entry?.Name, Shown = l.Entry?.Shown }),
                    }
                )
            );
    }
}

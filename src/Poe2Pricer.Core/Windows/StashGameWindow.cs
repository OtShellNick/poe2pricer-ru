using OpenCvSharp;
using Poe2Pricer.Core.Market;
using static System.FormattableString;

namespace Poe2Pricer.Core;

// A special stash tab (Orbs, Essence, Delirium, Breach, Ritual, Abyss, Expedition, …): filled
// slots named by the tab layout or against the poe.ninja item art, covered slots left out.
public sealed class StashGameWindow(Pipeline pipeline) : IGameWindow
{
    internal Pipeline Pipeline { get; } = pipeline;

    public WindowProfile Profile { get; } = new("Stash", AutoSwitch.Stash, GameWindows.FollowEvery);

    private PriceBook? iconsFor;

    public bool IsThere(GameCapture.Frame frame) => Pipeline.Stash.Locate(frame.Image) is not null;

    public Task<IOpenWindow?> LookAsync(GameCapture.Frame frame, LookContext context)
    {
        if (Pipeline.Stash.Locate(frame.Image, StashTabReader.MinimumScore) is not { } tab)
            return Task.FromResult<IOpenWindow?>(null);
        var slots = Read(frame.Image, tab, frame.Popup);
        context.Log(Invariant($"stash tab {tab.Kind} {tab.AnchorScore:0.00}, {slots.Count(s => s.Present)} occupied, {slots.Count(s => s.Covered)} covered"));
        return Task.FromResult<IOpenWindow?>(new Open(this, tab, slots));
    }

    // The slots, and besides what the reader sees itself (a popup's dark body): a slot counts
    // as covered when a key hint ("ALT Inspect" beside the item under the mouse) or a tooltip's
    // title bar lies where its label goes. Neither is dark, so they are found by their look
    // (OccluderDetector: the hint's key, the title bar's end caps). A title bar that only grazes
    // the label does not cover it, as in the Favours grid: in the controller UI the tooltip can
    // start right at the lower edge of the row above (a few px into the spot leave the label
    // above the bar; deeper in, the price would stand on the title).
    private StashSlot[] Read(Mat frame, StashTabWindow tab, double popup)
    {
        var slots = Pipeline.Stash.Read(frame, tab);
        var area = Pipeline.Stash.LookArea(tab);
        var hints = Pipeline.Occluders.Hints(frame, area);
        var tooltips = Pipeline.Occluders.TooltipsOver(frame, area, popup);
        if (hints.Length + tooltips.Length == 0)
            return slots;
        return slots.Select(s => s.Present && (hints.Any(p => p.Intersects(LabelSpot(s.Bounds))) || tooltips.Any(p => p.Overlaps(LabelSpot(s.Bounds), Tuning.Occluders.HideOverlap))) ? s with { Covered = true } : s).ToArray();
    }

    // Where the label of a slot is drawn: centred on its lower edge, inside the slot.
    private static Box LabelSpot(Box slot) =>
        new(slot.X + (slot.Width - Tuning.Stash.LabelSpotWidth) / 2, slot.Bottom - Tuning.Stash.LabelSpotHeight, Tuning.Stash.LabelSpotWidth, Tuning.Stash.LabelSpotHeight);

    // Item art for the symbol comparison, fetched once per price book in the background
    // (~640 images; slots whose art is still missing stay unnamed until the next check).
    private void EnsureIcons(PriceBook book)
    {
        if (ReferenceEquals(iconsFor, book))
            return;
        iconsFor = book;
        _ = Task.Run(() => Pipeline.Icons.EnsureAsync(book.Images.Select(p => (p.Key, p.Value.Path))))
            .ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted); // observed; items count as tried
    }

    private sealed record Open(StashGameWindow Kind, StashTabWindow Tab, StashSlot[] Slots) : IOpenWindow
    {
        public IGameWindow Window => Kind;

        public Box? GlanceArea => Kind.Pipeline.Stash.LookArea(Tab);

        // A tooltip over the tab lowers the score, so a lower bar than for the first look. Another
        // tab counts at once: otherwise every glance found "another tab" again and cancelled the
        // read running for it, and nothing was ever read.
        public Glance Glance(Mat frame, PriceBook? ready, LookContext context)
        {
            var now = Kind.Pipeline.Stash.Locate(frame, StashTabReader.MinimumScore);
            if (now is null)
                return new Glance.Gone("stash tab no longer detected");
            if (now.Kind == Tab.Kind && now.Mode == Tab.Mode && now.OffsetY == Tab.OffsetY)
                return Core.Glance.Unchanged;
            context.Log($"different stash tab ({now.Kind}), rescanning");
            return new Glance.Reread(this with { Tab = now, Slots = [] }, ClearNow: true);
        }

        // Reading all slots costs a few milliseconds, cheaper than deciding whether to.
        public Task<IOpenWindow?> FollowAsync(GameCapture.Frame frame, LookContext context)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var tab = Kind.Pipeline.Stash.Locate(frame.Image, StashTabReader.MinimumScore);
            if (tab is null)
            {
                context.Log("hidden: stash tab no longer detected");
                return Task.FromResult<IOpenWindow?>(null);
            }
            var slots = Kind.Read(frame.Image, tab, frame.Popup);
            context.Log(Invariant($"Time: stash tab {watch.ElapsedMilliseconds} ms, {tab.Kind} {tab.AnchorScore:0.00}, {slots.Count(s => s.Present)} occupied, {slots.Count(s => s.Covered)} covered"));
            return Task.FromResult<IOpenWindow?>(this with { Tab = tab, Slots = slots });
        }

        public async Task<Picture> LabelAsync(GameCapture.Frame frame, Task<PriceBook> prices, LookContext context)
        {
            var book = await prices;
            Kind.EnsureIcons(book);
            var named = Kind.Pipeline.Identifier.Identify(frame.Image, Tab, Slots, book, Kind.Pipeline.Stash.CategoriesOf(Tab.Kind));
            foreach (var s in named.Where(s => s.Present && s.NamedBy is not (null or StashIdentifier.ByTable)))
                context.Log($"stash {Tab.Kind} slot {s.Index}: {s.Name ?? "-"} ({s.NamedBy})");
            var settings = context.Settings;
            var labels = Labels.ForStash(named.Where(s => !s.Covered), book, Labels.FloorInDivine(settings.FloorValue, settings.FloorUnit, book), settings.PriceUnit);
            int present = named.Count(s => s.Present);
            return new Picture(
                this with { Slots = named },
                labels,
                null,
                labels.All(l => l.Text is null) && present > 0 && settings.FloorValue > 0
                    ? Texts.Count("StashAllBelowThreshold", present, settings.FloorValue, settings.FloorUnit, Tab.Kind)
                    : Texts.Get("StashStatus", Tab.Kind, Texts.Count("Items", present), Texts.Count("Prices", labels.Count(l => l.Text is not null)), book.League),
                $"{Tab.Kind} tab ({Tab.Mode}), {present} occupied, {labels.Length} labels",
                new { Window = "stash", Tab, Slots = named, Labels = labels }
            );
        }
    }
}

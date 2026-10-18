namespace Poe2Pricer.Core;

// Settles a greyed-out group omen ("!") with the name read from its own tooltip. Only the
// hovered cell may be settled: the name of any other item of the same group (a coloured
// Resurgence next to a grey Sinistral omen) would otherwise be taken for the grey one.
// Keyboard UI: the item under the mouse pointer, as Windows reports it.
// Controller UI: the item under the cursor comes from SelectionDetector (caret below it).
public static class TooltipResolver
{
    public static Recognition Resolve(
        Recognition recognition,
        IReadOnlyList<(Occluder Tooltip, string[] Lines)> titles,
        Catalog catalog,
        ItemMatch? controllerSelected = null,
        (int X, int Y)? pointer = null,
        string language = GameLanguage.English
    )
    {
        if (titles.Count == 0)
            return recognition;
        var items = recognition.Items.ToArray();
        int hovered;
        if (controllerSelected is not null)
            hovered = Array.IndexOf(items, controllerSelected);
        else if (pointer is { } p)
            hovered = Array.FindIndex(items, i => i.Bounds.Holds(p.X, p.Y));
        else
            return recognition;
        if (hovered < 0 || items[hovered].Group is not { } group || items[hovered].ResolvedBy is not null)
            return recognition;
        var byId = catalog.Items.ToDictionary(i => i.Id);
        // The item's own tooltip is the left one; a comparison tooltip only follows equipment.
        var own = titles.MinBy(t => t.Tooltip.Bounds.X);
        if (TitleReader.Match(own.Lines, group.Select(id => byId[id]), language) is not { } match)
            return recognition;
        items[hovered] = items[hovered] with
        {
            CatalogId = match.Item.Id,
            Name = match.Item.NameEn,
            Uncertain = false,
            ResolvedBy = "tooltip",
        };
        return recognition with { Items = items };
    }
}

namespace Poe2Pricer.Core;

// What a line of the status line says about the program: Ok — done, loaded, saved (green dot);
// Error — something went wrong or an input was refused (red dot, red text); Neutral —
// everything else, also while something is loading.
public enum StatusKind
{
    Neutral,
    Ok,
    Error,
}

// Link: a page the line leads to (a newer version to get); Act: what a click on the line does
// (install the fetched version). Either makes the line a link.
public readonly record struct StatusMessage(string Text, StatusKind Kind = StatusKind.Neutral, string? Link = null, Action? Act = null)
{
    public static StatusMessage Ok(string text) => new(text, StatusKind.Ok);

    public static StatusMessage Error(string text) => new(text, StatusKind.Error);

    public static StatusMessage Linked(string text, string link) => new(text, StatusKind.Neutral, link);

    public static StatusMessage Acting(string text, Action act) => new(text, StatusKind.Neutral, null, act);
}

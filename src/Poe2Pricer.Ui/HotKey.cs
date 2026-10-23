using System.Runtime.InteropServices;
using System.Windows.Input;
using Poe2Pricer.Core;

namespace Poe2Pricer.Ui;

// A key or combination, as the settings hold it ("Alt+F", "Alt+Shift+F") and as the window
// shows it. Here, not in the program itself, so that the view models can check the keys; the
// hook binds by it.
// Shown is what stands on the keyboard, in the language of the window: Windows' own names are
// not ("Next" for Page Down, "D1" for 1, "Oem3" for the key that types ö on a German
// keyboard). Stored is one fixed wording, whatever the language and the keyboard; read is
// every wording the program ever showed or stored.
public sealed record HotKey(bool Alt, bool Ctrl, bool Shift, Key Key)
{
    // Keys with a name of their own in the language of the window (Texts).
    private static readonly (Key Key, string Name)[] Named =
    [
        (Key.Capital, "KeyCapsLock"),
        (Key.Next, "KeyPageDown"),
        (Key.Prior, "KeyPageUp"),
        (Key.Return, "KeyEnter"),
        (Key.Back, "KeyBackspace"),
        (Key.Escape, "KeyEsc"),
        (Key.Space, "KeySpace"),
        (Key.Delete, "KeyDelete"),
        (Key.Insert, "KeyInsert"),
        (Key.Home, "KeyHome"),
        (Key.End, "KeyEnd"),
        (Key.Left, "KeyLeft"),
        (Key.Right, "KeyRight"),
        (Key.Up, "KeyUp"),
        (Key.Down, "KeyDown"),
        (Key.Snapshot, "KeyPrintScreen"),
        (Key.Scroll, "KeyScrollLock"),
        (Key.Pause, "KeyPause"),
        (Key.NumLock, "KeyNumLock"),
    ];

    // Keys named the same in every language: the digits and the number pad.
    private static readonly (Key Key, string Text)[] Fixed =
    [
        .. Enumerable.Range(0, 10).Select(d => (Key.D0 + d, d.ToString())),
        .. Enumerable.Range(0, 10).Select(d => (Key.NumPad0 + d, "Num " + d)),
        (Key.Multiply, "Num *"),
        (Key.Add, "Num +"),
        (Key.Subtract, "Num -"),
        (Key.Divide, "Num /"),
        (Key.Decimal, "Num ."),
    ];

    public static HotKey Parse(string text)
    {
        // The key is what follows the last "+" — unless the key itself is one ("Ctrl++", "Num +").
        string all = (text ?? throw new FormatException()).Trim();
        int cut = all.EndsWith("Num +", StringComparison.OrdinalIgnoreCase) ? all.Length - 5 : all.EndsWith('+') ? all.Length - 1 : all.LastIndexOf('+') + 1;
        string key = all[cut..].Trim();
        var parts = all[..cut].Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        bool Modifier(string part, string name) => Texts.Languages.Any(l => part.Equals(Texts.In(l, name), StringComparison.OrdinalIgnoreCase));
        // A part that is no modifier of the program ("Control", "Win") is not dropped: "Win+F"
        // would bind the bare F and take it from the game.
        if (parts.Any(p => !p.Equals("Alt", StringComparison.OrdinalIgnoreCase) && !Modifier(p, "KeyCtrl") && !Modifier(p, "KeyShift")))
            throw new FormatException();
        return new(parts.Contains("Alt", StringComparer.OrdinalIgnoreCase), parts.Any(p => Modifier(p, "KeyCtrl")), parts.Any(p => Modifier(p, "KeyShift")), KeyOf(key));
    }

    // The key a name stands for: a name of the program in any language, the sign the key types
    // on this keyboard, or Windows' own name (how keys are stored, and were once shown).
    private static Key KeyOf(string name)
    {
        if (name.Length == 0)
            throw new FormatException();
        foreach (var (key, text) in Fixed)
            if (name.Equals(text, StringComparison.OrdinalIgnoreCase))
                return key;
        foreach (var (key, textName) in Named)
            if (Texts.Languages.Any(l => name.Equals(Texts.In(l, textName), StringComparison.OrdinalIgnoreCase)))
                return key;
        foreach (var key in Enum.GetValues<Key>().Distinct())
            if (Typed(key) is { } sign && name.Equals(sign, StringComparison.OrdinalIgnoreCase))
                return key;
        return (Key)new KeyConverter().ConvertFromInvariantString(name)!;
    }

    // As shown in the settings window; Parse reads it back.
    public string Text => Joined(Texts.Get("KeyCtrl"), Texts.Get("KeyShift"), Name(Key));

    // As written to the settings file: the same on every keyboard and in every language.
    public string Stored => Joined("Ctrl", "Shift", new KeyConverter().ConvertToInvariantString(Key));

    private string Joined(string ctrl, string shift, string? key) =>
        string.Join("+", new[] { Ctrl ? ctrl : null, Alt ? "Alt" : null, Shift ? shift : null, key }.Where(part => part is not null));

    private static string? Name(Key key)
    {
        foreach (var (fixedKey, text) in Fixed)
            if (key == fixedKey)
                return text;
        foreach (var (named, textName) in Named)
            if (key == named)
                return Texts.Get(textName);
        return Typed(key) ?? new KeyConverter().ConvertToInvariantString(key);
    }

    // The sign a key outside letters and digits types on the keyboard in use ("Ö", "#", "<"):
    // Windows names such keys by their place ("Oem3"). null: a key with a name (letters,
    // function keys) or one that types nothing.
    private static string? Typed(Key key)
    {
        if (!key.ToString().StartsWith("Oem", StringComparison.Ordinal))
            return null;
        // MAPVK_VK_TO_CHAR; the highest bit marks a dead key (´, ^), the sign stands below it.
        uint sign = MapVirtualKeyW((uint)KeyInterop.VirtualKeyFromKey(key), 2) & 0x7FFF;
        return sign > ' ' ? char.ToUpperInvariant((char)sign).ToString() : null;
    }

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint code, uint mapType);

    // What a key text cannot be read for: the same kinds the window reports as "Invalid input".
    public static bool Unreadable(Exception e) => e is FormatException or ArgumentException or NotSupportedException or IndexOutOfRangeException or ArgumentOutOfRangeException;

    // The keys the program binds at start. A stored key that cannot be read (a settings file
    // changed by hand) gives way to the standard one, named in Replaced with what was stored,
    // instead of ending the program at the start without a word.
    public static Settings Usable(Settings s, out IReadOnlyList<(string Stored, string Used)> replaced)
    {
        var standard = new Settings();
        var bad = new List<(string, string)>();
        string Kept(string? saved, string fallback)
        {
            try
            {
                Parse(saved!);
                return saved!;
            }
            catch (Exception e) when (Unreadable(e))
            {
                bad.Add((saved ?? "", fallback));
                return fallback;
            }
        }
        string toggle = Kept(s.ToggleKey, standard.ToggleKey), diagnose = Kept(s.DiagnoseKey, standard.DiagnoseKey);
        // The standard key of the one may be what the other is set to: then both are the standard.
        if (bad.Count == 1 && Parse(toggle) == Parse(diagnose))
        {
            bad = [(s.ToggleKey ?? "", standard.ToggleKey), (s.DiagnoseKey ?? "", standard.DiagnoseKey)];
            (toggle, diagnose) = (standard.ToggleKey, standard.DiagnoseKey);
        }
        // The exact-price key may be empty (not bound). Unreadable, it gives way to its
        // standard; the key of one of the two, it stays empty rather than taking some other key
        // another program may hold (an older settings file has none, so Alt+D comes in; one of
        // the two may be Alt+D already: review before 1.2.0, 21.10.2026). Used "": off.
        string exact = string.IsNullOrWhiteSpace(s.ExactKey) ? "" : Kept(s.ExactKey, standard.ExactKey);
        if (exact.Length > 0 && (Parse(exact) == Parse(toggle) || Parse(exact) == Parse(diagnose)))
        {
            // Named as the file has it, not as its replacement ("Alt+D" after an unreadable
            // key; OFFEN Z7).
            bad.RemoveAll(b => b.Item2 == standard.ExactKey);
            bad.Add((s.ExactKey!, ""));
            exact = "";
        }
        replaced = bad;
        // An exact key of null (a hand-edited file) comes back as "", else the start fell over it.
        return bad.Count == 0 && exact == s.ExactKey ? s : s with { ToggleKey = toggle, DiagnoseKey = diagnose, ExactKey = exact };
    }

    // A saved key in the window's own wording ("Alt+Shift+Next" → "Alt+Shift+Page Down"); what
    // cannot be read is shown as it is.
    public static string Shown(string saved)
    {
        try
        {
            return Parse(saved).Text;
        }
        catch (Exception e) when (Unreadable(e))
        {
            return saved;
        }
    }
}

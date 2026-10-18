using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Poe2Pricer.Ui.Views;

// A key field: click it, then press the key or combination; it is not typed. Tab moves on as
// everywhere. While it listens its frame is brass, like every field with the focus (the Input
// style).
public sealed class KeyBox : TextBox
{
    private static volatile bool listening;

    // A key field is waiting for a key. The keyboard hook must leave every key alone meanwhile:
    // it also works while the program's own window is in front, and it swallowed the keys that
    // are bound already — pressed in a key field, F9 asked for a diagnosis and Alt+F for the
    // prices, and the field would take nothing. Read from the hook's thread.
    public static bool Listening => listening;

    public KeyBox()
    {
        IsReadOnly = true;
        Cursor = Cursors.Hand;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        listening = true;
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        listening = false;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        // With Alt held (and for F10) Windows reports a system key.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab)
            return;
        e.Handled = true;
        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed or Key.None)
            return;
        var held = Keyboard.Modifiers;
        var pressed = new HotKey(held.HasFlag(ModifierKeys.Alt), held.HasFlag(ModifierKeys.Control), held.HasFlag(ModifierKeys.Shift), key);
        // Keeps the binding: the view model gets the new key. A key the other field holds is
        // taken too — the view model swaps the two (SettingsViewModel), so that the keys can
        // be exchanged without a third.
        SetCurrentValue(TextProperty, pressed.Text);
    }
}

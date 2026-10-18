using System.Windows.Input;

namespace Poe2Pricer.Ui;

// One press of a bound key counts once. Windows repeats a key that is held, and the keyboard
// hook sees every repeat as a key going down: a key held a little too long would show and
// hide the overlay in turn and leave it in whichever state the last repeat made, and the
// diagnosis key would save several diagnoses.
// A release that never arrives (the lock screen takes the keyboard) must not leave the key dead:
// repeats follow each other within a second at the slowest setting of Windows, so a key going
// down later than Lost after the last time is a new press.
public sealed class HeldKey(TimeProvider time)
{
    public static readonly TimeSpan Lost = TimeSpan.FromSeconds(1.5);

    private Key? held;
    private long last;

    // The key went down: true for a new press, false for a repeat of the one held.
    public bool Pressed(Key key)
    {
        long now = time.GetTimestamp();
        bool repeat = held == key && time.GetElapsedTime(last, now) < Lost;
        held = key;
        last = now;
        return !repeat;
    }

    public void Released(Key key)
    {
        if (held == key)
            held = null;
    }
}

using System.Runtime.InteropServices;

namespace Poe2Pricer.App;

// Reports every change of the foreground window at once (EVENT_SYSTEM_FOREGROUND). The
// once-a-second check alone would leave the overlay on the desktop for up to 3 s after
// Alt+Tab, and missing for as long on the way back. Out-of-context events are
// delivered through the message loop of the thread that set the hook, here the UI thread.
public sealed class ForegroundWatcher : IDisposable
{
    private readonly WinEventProc proc;
    private readonly nint hook;

    public event Action? Changed;

    public ForegroundWatcher()
    {
        proc = (_, _, _, _, _, _, _) => Changed?.Invoke();
        hook = SetWinEventHook(0x0003, 0x0003, 0, proc, 0, 0, 0); // EVENT_SYSTEM_FOREGROUND, WINEVENT_OUTOFCONTEXT
    }

    public void Dispose() => UnhookWinEvent(hook);

    private delegate void WinEventProc(nint hook, uint ev, nint hwnd, int obj, int child, uint thread, uint time);

    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc proc, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
}

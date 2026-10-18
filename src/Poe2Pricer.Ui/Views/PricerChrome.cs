using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Poe2Pricer.Ui.Views;

// The window draws its own frame and title bar (WindowChrome): this wires the buttons of the
// title bar, switches off the round corners Windows 11 forces on a window, and keeps a
// maximized window inside the screen. There is no button to maximize, but Windows still
// maximizes on a double click on the title bar or when the window is dragged to the top edge.
public static class PricerChrome
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DONOTROUND = 1;
    private const int SM_CXSIZEFRAME = 32;
    private const int SM_CXPADDEDBORDER = 92;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    // frame: what holds everything the window shows; it gets the margin of a maximized window.
    public static void Attach(Window window, FrameworkElement frame)
    {
        window.CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (_, _) => SystemCommands.MinimizeWindow(window)));
        // Calls Window.Close(); the Closing handler (hide to the notification area) still applies.
        window.CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => SystemCommands.CloseWindow(window)));
        window.StateChanged += (_, _) => frame.Margin = window.WindowState == WindowState.Maximized ? Overhang(window) : default;
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            DisableRoundCorners(window);
        else
            window.SourceInitialized += (_, _) => DisableRoundCorners(window);
    }

    // A maximized window without the system's frame reaches beyond the screen by the width of
    // that frame on every side.
    private static Thickness Overhang(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        uint dpi = handle == IntPtr.Zero ? 96 : GetDpiForWindow(handle);
        double pixels = GetSystemMetricsForDpi(SM_CXSIZEFRAME, dpi) + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, dpi);
        return new Thickness(pixels * 96 / dpi);
    }

    // For a window with the system's own title bar (WaitWindow): the title bar in Windows' dark
    // colours, like the rest of the window. Unknown under Windows 10 before 20H1: the result is
    // ignored on purpose, the title bar stays light there.
    public static void DarkTitleBar(Window window)
    {
        void Apply()
        {
            var handle = new WindowInteropHelper(window).Handle;
            int dark = 1;
            _ = DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        }
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            Apply();
        else
            window.SourceInitialized += (_, _) => Apply();
    }

    private static void DisableRoundCorners(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        int preference = DWMWCP_DONOTROUND;
        // Unknown under Windows 10: the result is ignored on purpose.
        _ = DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }
}

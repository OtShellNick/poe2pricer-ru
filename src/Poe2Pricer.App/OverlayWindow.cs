using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Poe2Pricer.Core;
using Poe2Pricer.Core.Market;

namespace Poe2Pricer.App;

// Transparent, click-through, never focused, always on top, and left out of screen captures
// (SetWindowDisplayAffinity 0x11), so the tool never sees its own labels. Window styles as
// in Dev-Jahn/poe2-ritual-checker Overlay.cs (MIT).
public sealed class OverlayWindow : Window
{
    private readonly LabelLayer layer = new();

    public OverlayWindow(ImageSource? divine, ImageSource? exalted)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;
        Content = layer;
        layer.Divine = divine;
        layer.Exalted = exalted;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW
            SetWindowLongPtrW(hwnd, -20, GetWindowLongPtrW(hwnd, -20) | 0x08000000 | 0x20 | 0x80);
            SetWindowDisplayAffinity(hwnd, 0x11);
        };
    }

    // scale: screen pixels per pixel of the labels' coordinates (the game runs at another
    // height than the readers are measured at).
    public void Show(Box screen, Label[] labels, (Box Panel, RumourLine[] Lines)? rumours = null, double scale = 1)
    {
        layer.Rumours = rumours;
        if (!IsVisible)
            Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, -1, screen.X, screen.Y, screen.Width, screen.Height, 0x10);
        layer.PixelScale = scale / VisualTreeHelper.GetDpi(this).DpiScaleX;
        layer.Labels = labels;
        layer.InvalidateVisual();
    }

    // Nothing to show: the window stays and draws nothing. A hidden window keeps its last
    // picture, and Windows shows that picture the moment the window is shown again, before the
    // new labels are drawn — the old prices of the last window would flash up on the next one.
    public void Clear()
    {
        layer.Rumours = null;
        layer.Labels = [];
        layer.InvalidateVisual();
    }

    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
}

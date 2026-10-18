using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Poe2Pricer.Ui.Views;

// The program window's content as a picture, without a screen: the measure for "looks as
// before" when the window is changed. Two runs give the same picture bit for bit. Not in it:
// the title bar, which Windows draws.
public static class WindowPicture
{
    // As the window shows a tab: it opens sized to the Settings tab and keeps that size on
    // every tab. Laid out at its own unlimited size, the rumour list would stand at full
    // height without scrolling, and a row with long texts would push its columns out of line
    // — neither is what the window shows.
    // room: the height of the screen's work area (none: enough for everything).
    public static BitmapSource Of(MainWindow window, int tab, double room = double.PositiveInfinity)
    {
        var tabs = (TabControl)window.FindName("tabs");
        tabs.SelectedIndex = 0;
        window.FixSize();
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        // The look lies in the window's resources and the view model in its data context;
        // outside of the window the content needs both.
        var host = new Border
        {
            Background = window.Background,
            Child = content,
            Resources = window.Resources,
            DataContext = window.DataContext,
            UseLayoutRounding = window.UseLayoutRounding,
            SnapsToDevicePixels = window.SnapsToDevicePixels,
        };
        TextElement(host, window);
        Bound(host);
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = window.Fitted(host.DesiredSize, room);
        tabs.SelectedIndex = tab;
        Bound(host);
        // The warnings above the foot wrap at the width of the window and then need more
        // height than they asked for without a limit; the window itself grows by the
        // difference (MainWindow), and so does the picture.
        var warnings = (FrameworkElement)window.FindName("warnings");
        double unlimited = warnings.DesiredSize.Height;
        host.Measure(size);
        host.Arrange(new Rect(size));
        host.UpdateLayout();
        if (warnings.DesiredSize.Height > unlimited)
        {
            size = new Size(size.Width, Math.Min(room, size.Height + warnings.DesiredSize.Height - unlimited));
            host.Measure(size);
        }
        host.Arrange(new Rect(size));
        host.UpdateLayout();
        var picture = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        picture.Render(host);
        picture.Freeze();
        return picture;
    }

    // Bindings take their values in a later turn of the dispatcher, and no window is shown
    // here that would run it: wait until that turn is through.
    private static void Bound(Border host) => host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

    // Font and text colour come from the window; outside of it the content would fall back to
    // the system's.
    private static void TextElement(Border host, Window window)
    {
        System.Windows.Documents.TextElement.SetFontFamily(host, window.FontFamily);
        System.Windows.Documents.TextElement.SetFontSize(host, window.FontSize);
        System.Windows.Documents.TextElement.SetForeground(host, window.Foreground);
    }

    public static void Save(BitmapSource picture, string file)
    {
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(picture));
        using var stream = System.IO.File.Create(file);
        png.Save(stream);
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Poe2Pricer.Ui.Views;

// A window that says what the program is waiting for, while it waits: the installation of the
// text recognition takes minutes, and something has to be seen in that time. An ordinary
// window — title bar, in the taskbar, not above everything — so that it can be moved, put
// aside or minimised while Windows works; the time it has been running is counted up, as
// Windows gives no progress for the installation. It goes when the work is done. "Cancel" (or
// closing it) is the way out when the work hangs: the waiting ends, not the work — an
// installation running with administrator rights is not the program's to stop.
public sealed class WaitWindow : Window
{
    private bool done;

    // elapsed: a text with {0} for the time the work has been running (m:ss); null — not shown.
    public WaitWindow(string text, string cancel, string? elapsed = null)
    {
        var look = PricerTheme.Load();
        Resources.MergedDictionaries.Add(look);
        Title = "poe2pricer";
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanMinimize;
        ShowInTaskbar = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = false;
        PricerChrome.DarkTitleBar(this);
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        Background = (Brush)look["Back"];
        var button = new Button { Content = cancel, Style = (Style)look["PlainButton"], HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), IsCancel = true };
        button.Click += (_, _) => Close();
        var panel = new StackPanel
        {
            Width = 400,
            Children =
            {
                new TextBlock { Text = text, Foreground = (Brush)look["Text"], TextWrapping = TextWrapping.Wrap },
                new ProgressBar
                {
                    IsIndeterminate = true,
                    Height = 6,
                    Margin = new Thickness(0, 16, 0, 0),
                    BorderThickness = new Thickness(0),
                    Background = (Brush)look["Field"],
                    Foreground = (Brush)look["Accent"],
                },
            },
        };
        if (elapsed is not null)
        {
            var clock = new TextBlock { Foreground = (Brush)look["Muted"], Margin = new Thickness(0, 8, 0, 0) };
            var started = DateTime.Now;
            void Tick() => clock.Text = string.Format(elapsed, (DateTime.Now - started).ToString(@"m\:ss"));
            Tick();
            // At normal priority, so that the bar's animation does not hold it back; started
            // once the window stands there — started in the constructor, the first tick stayed
            // away for seconds (measured in the tests). The first tick may still come late.
            var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) => Tick();
            Loaded += (_, _) => timer.Start();
            Closed += (_, _) => timer.Stop();
            panel.Children.Add(clock);
        }
        panel.Children.Add(button);
        Content = new Border { Padding = new Thickness(24, 20, 24, 20), Child = panel };
    }

    // Shows the window until the work is done (true) or the user gives up waiting (false).
    // On the thread of the windows.
    public static bool While(string text, string cancel, Task work, string? elapsed = null)
    {
        var window = new WaitWindow(text, cancel, elapsed);
        work.ContinueWith(_ =>
            window.Dispatcher.BeginInvoke(() =>
            {
                window.done = true;
                window.Close();
            })
        );
        window.ShowDialog();
        return window.done;
    }
}

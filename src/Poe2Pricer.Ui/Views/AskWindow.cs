using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Poe2Pricer.Ui.Views;

// A question in the program's own look instead of Windows' message box (21.10.2026: the
// box "does not match the program's style"): a box with its caption, the text, and the
// answers as the program's buttons, the main one in brass at the right end as "Save" in the
// window's foot. No title bar of its own; it is dragged by any free spot. Enter takes the main
// answer, Esc and closing the last one (stay).
public sealed class AskWindow : Window
{
    private int chosen = -1;

    private AskWindow(Window? owner, string caption, string text, string[] answers)
    {
        var look = PricerTheme.Load();
        Resources.MergedDictionaries.Add(look);
        Title = "poe2pricer";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        if (owner is { IsVisible: true })
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Background = (Brush)look["Back"];
        Foreground = (Brush)look["Text"];
        Content = Body(look, caption, text, answers, i =>
        {
            chosen = i;
            Close();
        });
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };
    }

    // answers: from the main one (Enter) to the one that changes nothing (Esc, closing).
    // Returns the index of the answer.
    public static int Ask(Window? owner, string caption, string text, params string[] answers)
    {
        var window = new AskWindow(owner, caption, text, answers);
        window.ShowDialog();
        return window.chosen < 0 ? answers.Length - 1 : window.chosen;
    }

    // The question as a picture, without a screen (poe2pricer --render-settings with
    // POE2PRICER_ASK; how it looks before it is shown to anyone).
    public static BitmapSource Picture(string caption, string text, params string[] answers)
    {
        var look = PricerTheme.Load();
        var body = Body(look, caption, text, answers, _ => { });
        body.Resources = look;
        System.Windows.Documents.TextElement.SetFontFamily(body, new FontFamily("Segoe UI"));
        System.Windows.Documents.TextElement.SetFontSize(body, 13);
        System.Windows.Documents.TextElement.SetForeground(body, (Brush)look["Text"]);
        body.UseLayoutRounding = true;
        body.SnapsToDevicePixels = true;
        body.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = body.DesiredSize;
        body.Arrange(new Rect(size));
        body.UpdateLayout();
        var picture = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        picture.Render(body);
        picture.Freeze();
        return picture;
    }

    // The window's frame as the program window's: the dark ground, a line round it, inside a
    // box with the corner fittings.
    private static Border Body(ResourceDictionary look, string caption, string text, string[] answers, Action<int> answered)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        // The main answer at the right end, the others before it in their order.
        for (int i = 1; i < answers.Length; i++)
            buttons.Children.Add(Answer(look, answers[i], i, "PlainButton", answered, cancel: i == answers.Length - 1));
        buttons.Children.Add(Answer(look, answers[0], 0, "SaveButton", answered, main: true));
        var box = new GroupBox
        {
            Header = caption,
            Style = (Style)look["Box"],
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Width = 400, Foreground = (Brush)look["Text"] },
                    buttons,
                },
            },
        };
        return new Border
        {
            Background = (Brush)look["Back"],
            BorderBrush = (Brush)look["Line"],
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Child = box,
        };
    }

    private static Button Answer(ResourceDictionary look, string text, int index, string style, Action<int> answered, bool main = false, bool cancel = false)
    {
        var button = new Button { Content = text, Style = (Style)look[style], IsDefault = main, IsCancel = cancel, Margin = new Thickness(10, 0, 0, 0) };
        button.Click += (_, _) => answered(index);
        return button;
    }
}

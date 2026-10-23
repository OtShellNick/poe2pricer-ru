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

    private AskWindow(Window? owner, string caption, string text, double width, string[] answers)
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
        Content = Body(look, caption, text, width, answers, i =>
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
    // Returns the index of the answer. width: of the text; the usual question fits 400, the
    // notice before the first start wants more.
    public static int Ask(Window? owner, string caption, string text, params string[] answers) =>
        Ask(owner, caption, text, TextWidth, answers);

    public static int Ask(Window? owner, string caption, string text, double width, params string[] answers)
    {
        var window = new AskWindow(owner, caption, text, width, answers);
        window.ShowDialog();
        return window.chosen < 0 ? answers.Length - 1 : window.chosen;
    }

    private const double TextWidth = 400;

    // The question as a picture, without a screen (poe2pricer --render-settings with
    // POE2PRICER_ASK; how it looks before it is shown to anyone).
    public static BitmapSource Picture(string caption, string text, params string[] answers) =>
        Picture(caption, text, TextWidth, answers);

    public static BitmapSource Picture(string caption, string text, double width, params string[] answers)
    {
        var look = PricerTheme.Load();
        var body = Body(look, caption, text, width, answers, _ => { });
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
    private static Border Body(ResourceDictionary look, string caption, string text, double width, string[] answers, Action<int> answered)
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
                    Paragraphs(text, width, (Brush)look["Text"]),
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

    // The text as paragraphs (blank line between them), each a block with room after it; a
    // paragraph of lines starting with "• " becomes a list with hanging indent, one row a
    // line. A text without blank lines is one block, as before.
    private static StackPanel Paragraphs(string text, double width, Brush brush)
    {
        var panel = new StackPanel { Width = width };
        var paragraphs = text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        for (int p = 0; p < paragraphs.Length; p++)
        {
            double after = p < paragraphs.Length - 1 ? 10 : 0;
            var lines = paragraphs[p].Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.All(l => l.StartsWith("• ")))
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    var row = new Grid { Margin = new Thickness(0, 0, 0, i < lines.Length - 1 ? 5 : after) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    var bullet = new TextBlock { Text = "•", Foreground = brush, LineHeight = 19 };
                    var body = new TextBlock { Text = lines[i][2..], TextWrapping = TextWrapping.Wrap, Foreground = brush, LineHeight = 19 };
                    Grid.SetColumn(body, 1);
                    row.Children.Add(bullet);
                    row.Children.Add(body);
                    panel.Children.Add(row);
                }
            }
            else
                panel.Children.Add(new TextBlock { Text = string.Join('\n', lines), TextWrapping = TextWrapping.Wrap, Foreground = brush, LineHeight = 19, Margin = new Thickness(0, 0, 0, after) });
        }
        return panel;
    }

    private static Button Answer(ResourceDictionary look, string text, int index, string style, Action<int> answered, bool main = false, bool cancel = false)
    {
        var button = new Button { Content = text, Style = (Style)look[style], IsDefault = main, IsCancel = cancel, Margin = new Thickness(10, 0, 0, 0) };
        button.Click += (_, _) => answered(index);
        return button;
    }
}

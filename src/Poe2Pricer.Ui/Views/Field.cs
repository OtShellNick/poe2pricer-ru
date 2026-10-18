using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Poe2Pricer.Ui.Views;

// What a field can be told besides its text.
public static class Field
{
    // The field holds something that could not be read when saving: its frame turns red (the
    // Input style) until the text is changed.
    public static readonly DependencyProperty InvalidProperty = DependencyProperty.RegisterAttached("Invalid", typeof(bool), typeof(Field), new PropertyMetadata(false));

    public static bool GetInvalid(DependencyObject field) => (bool)field.GetValue(InvalidProperty);

    public static void SetInvalid(DependencyObject field, bool value) => field.SetValue(InvalidProperty, value);
}

// A tooltip that repeats the text of its own text block is shown only while that text is cut
// off ("Monster effective…"); where everything can be read it would say the same again.
public static class TrimmedTip
{
    public static readonly DependencyProperty OnlyProperty = DependencyProperty.RegisterAttached("Only", typeof(bool), typeof(TrimmedTip), new PropertyMetadata(false, Changed));

    public static bool GetOnly(DependencyObject text) => (bool)text.GetValue(OnlyProperty);

    public static void SetOnly(DependencyObject text, bool value) => text.SetValue(OnlyProperty, value);

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not TextBlock text)
            return;
        text.ToolTipOpening -= Opening;
        if ((bool)e.NewValue)
            text.ToolTipOpening += Opening;
    }

    private static void Opening(object sender, ToolTipEventArgs e)
    {
        var text = (TextBlock)sender;
        if (GetOnly(text) && !Trimmed(text))
            e.Handled = true;
    }

    // The whole text is wider than the place the text block has.
    public static bool Trimmed(TextBlock text)
    {
        var whole = new FormattedText(
            text.Text,
            CultureInfo.CurrentUICulture,
            text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
            text.FontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(text).PixelsPerDip
        );
        return whole.Width > text.ActualWidth + .5;
    }
}

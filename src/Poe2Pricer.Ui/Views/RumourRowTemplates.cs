using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Poe2Pricer.Ui.ViewModels;

namespace Poe2Pricer.Ui.Views;

// A row of the sheet shows name, map and mods as text; in a row of the player's own they are
// fields.
public sealed class RumourRowTemplates : DataTemplateSelector
{
    public DataTemplate? Sheet { get; set; }
    public DataTemplate? Own { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item is RumourRowViewModel { Custom: true } ? Own : Sheet;
}

// The colour of a rating's letters (RatingColours).
public sealed class RatingBrush : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => RatingColours.Of(value as string ?? "");

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

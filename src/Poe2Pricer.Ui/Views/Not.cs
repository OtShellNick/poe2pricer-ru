using System.Globalization;
using System.Windows.Data;

namespace Poe2Pricer.Ui.Views;

// The opposite of a bool, both ways: two radio buttons on one flag ("any of them" is the
// flag off, "all of them" the flag on).
public sealed class Not : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : value;
}

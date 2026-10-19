using System.Globalization;

namespace Poe2Pricer.Ui;

// A number as typed into a field: "," as well as "." for the decimal point, nothing else
// local; the same reading for every field of the window.
public static class Numbers
{
    public static bool TryParse(string text, out double value) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    public static bool TryParse(string text, out decimal value) =>
        decimal.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}

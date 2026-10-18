using System.Windows;

namespace Poe2Pricer.Ui;

// The window's look as a resource dictionary (Themes/Theme.xaml). The program has no App.xaml,
// and the window is also drawn without an Application object (as a picture, in tests): the
// window lays the dictionary into its own resources. So its styles for combo boxes and scroll
// bars reach this window only.
public static class PricerTheme
{
    // Works without an Application object too (rendering the window to a picture, tests).
    public static ResourceDictionary Load() =>
        (ResourceDictionary)Application.LoadComponent(new Uri("/Poe2Pricer.Ui;component/Themes/Theme.xaml", UriKind.Relative));
}

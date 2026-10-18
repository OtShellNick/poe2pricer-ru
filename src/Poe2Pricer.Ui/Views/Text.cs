using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;
using Poe2Pricer.Core;

namespace Poe2Pricer.Ui.Views;

// A text of the window by its name in the language file: Text="{views:Text Save}". Bound, not
// copied: when the language changes, every text of the open window follows (Texts.Changed).
[MarkupExtensionReturnType(typeof(object))]
public sealed class TextExtension(string key) : MarkupExtension
{
    public string Key { get; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = TextSource.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}

// What the bindings of TextExtension read from.
public sealed class TextSource : INotifyPropertyChanged
{
    public static TextSource Instance { get; } = new();

    private TextSource() => Texts.Changed += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));

    public string this[string key] => Texts.Get(key);

    public event PropertyChangedEventHandler? PropertyChanged;
}

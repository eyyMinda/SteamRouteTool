using System.Windows.Data;
using System.Windows.Markup;

namespace SteamRouteTool.Localization;

/// <summary>
/// A translated text in XAML: <c>Text="{l:Tr Routes.Title}"</c>. It's a binding to <see cref="Loc"/>, so it updates when the
/// language changes. Works on any dependency property, including data grid column headers and tooltips.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new Binding("[" + Key + "]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
    }
}

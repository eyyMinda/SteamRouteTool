using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SteamRouteTool.Converters;

/// <summary>Visible for true / non-null / non-empty values, collapsed otherwise. Pass "Invert" as the parameter to flip it.</summary>
public sealed class VisibleWhenConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool visible = value switch
        {
            bool flag => flag,
            string text => text.Length > 0,
            null => false,
            _ => true,
        };
        if (parameter as string == "Invert") { visible = !visible; }
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

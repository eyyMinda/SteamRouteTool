using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SteamRouteTool;

/// <summary>
/// Applies the light/dark theme and the app's own colors on top of it. The Windows accent color is kept for buttons,
/// checkboxes and navigation, but not for selected rows: the theme fills them with the accent (e.g. bright pink), which
/// made grey, muted and red text unreadable. Selected rows use a neutral highlight instead, and ping colors have their own
/// palette per theme with enough contrast on both the card and the highlight.
/// </summary>
public static class AppTheme
{
    public static void Apply(string theme)
    {
        Application.Current.ThemeMode = theme switch
        {
            "Light" => ThemeMode.Light,
            "Dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
        UpdateColors();
    }

    /// <summary>Keeps the colors right when Windows switches between light and dark mode while the theme is "System".</summary>
    public static void FollowSystemChanges()
    {
        SystemEvents.UserPreferenceChanged += (sender, e) =>
        {
            // WPF swaps the theme on the same notification; update after it has.
            if (e.Category == UserPreferenceCategory.General)
            {
                Application.Current.Dispatcher.BeginInvoke(UpdateColors, DispatcherPriority.Background);
            }
        };
    }

    static void UpdateColors()
    {
        ResourceDictionary resources = Application.Current.Resources;
        bool dark = Application.Current.TryFindResource("TextFillColorPrimaryBrush") is SolidColorBrush { Color.R: > 128 };

        // Overrides the theme's accent-filled selection (the DataGrid row style reads these keys).
        resources["DataGridRowSelectedBackgroundThemeBrush"] = CreateBrush(dark ? "#2EFFFFFF" : "#17000000");
        resources["DataGridRowSelectedForegroundThemeBrush"] = CreateBrush(dark ? "#FFFFFFFF" : "#E4000000");

        resources["PingGoodBrush"] = CreateBrush(dark ? "#FF6CCB5F" : "#FF0F7B0F");
        resources["PingFairBrush"] = CreateBrush(dark ? "#FFF2C94C" : "#FF9D5D00");
        resources["PingPoorBrush"] = CreateBrush(dark ? "#FFFF7B66" : "#FFC42B1C");
    }

    static SolidColorBrush CreateBrush(string color)
    {
        SolidColorBrush brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}

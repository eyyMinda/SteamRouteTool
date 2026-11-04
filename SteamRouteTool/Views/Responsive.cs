using System.Windows;

namespace SteamRouteTool.Views;

/// <summary>
/// Compact mode for small windows: set on a page, inherited by everything inside it (including user controls and
/// templates), so parts can switch with the FullOnly / CompactOnly styles or their own triggers.
/// </summary>
public static class Responsive
{
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.RegisterAttached(
        "IsCompact", typeof(bool), typeof(Responsive), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsCompact(DependencyObject element) => (bool)element.GetValue(IsCompactProperty);

    public static void SetIsCompact(DependencyObject element, bool value) => element.SetValue(IsCompactProperty, value);
}

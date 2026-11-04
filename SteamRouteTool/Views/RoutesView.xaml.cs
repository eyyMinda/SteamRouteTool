using System.Windows;
using System.Windows.Controls;

namespace SteamRouteTool.Views;

public partial class RoutesView : UserControl
{
    // Below this space the page switches to its compact layout (see Responsive): presets as a dropdown, a slim
    // Auto select, a region list instead of chips.
    const double CompactWidth = 820;
    const double CompactHeight = 560;

    // The game tabs' natural width, measured while they're shown (game names aren't translated, so it doesn't change).
    double gameTabsWidth;

    public RoutesView()
    {
        InitializeComponent();
    }

    void RoutesView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Responsive.SetIsCompact(this, ActualWidth < CompactWidth || ActualHeight < CompactHeight);
    }

    // The clickable game tabs stay while they fit beside the headline numbers; otherwise a dropdown takes their place.
    void Header_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (GameTabsList.Visibility == Visibility.Visible)
        {
            GameTabsList.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            gameTabsWidth = Math.Max(gameTabsWidth, GameTabsList.DesiredSize.Width);
        }
        bool tabsFit = gameTabsWidth + StatsPanel.ActualWidth + StatsPanel.Margin.Left <= Header.ActualWidth;
        GameTabsList.Visibility = tabsFit ? Visibility.Visible : Visibility.Collapsed;
        GamePicker.Visibility = tabsFit ? Visibility.Collapsed : Visibility.Visible;
    }
}

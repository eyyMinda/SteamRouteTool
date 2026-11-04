using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SteamRouteTool.Views;

public partial class RoutesView : UserControl
{
    // Below this space the page switches to its compact layout (see Responsive): slim presets and Auto select,
    // a region list instead of chips.
    const double CompactWidth = 820;
    const double CompactHeight = 560;

    public RoutesView()
    {
        InitializeComponent();
    }

    void RoutesView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Responsive.SetIsCompact(this, ActualWidth < CompactWidth || ActualHeight < CompactHeight);
    }

    // The mouse wheel scrolls a sideways strip (game tabs, compact presets) sideways.
    void GameTabs_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ScrollViewer strip = (ScrollViewer)sender;
        if (strip.ScrollableWidth <= 0) { return; }
        strip.ScrollToHorizontalOffset(strip.HorizontalOffset - e.Delta / 2.0);
        e.Handled = true;
    }
}

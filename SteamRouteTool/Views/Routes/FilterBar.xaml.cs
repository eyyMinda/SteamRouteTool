using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SteamRouteTool.Views.Routes;

public partial class FilterBar : UserControl
{
    public FilterBar()
    {
        InitializeComponent();
    }

    // The menu opens on a normal click, under the button (it takes the page's DataContext from its target).
    void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        ContextMenu menu = MoreButton.ContextMenu;
        menu.PlacementTarget = MoreButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}

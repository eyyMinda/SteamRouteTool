using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using SteamRouteTool.Converters;

namespace SteamRouteTool.Views;

public partial class RoutesView : UserControl
{
    static readonly VisibleWhenConverter VisibleWhen = new VisibleWhenConverter();

    public RoutesView()
    {
        InitializeComponent();
        AutoSelectPopup.CustomPopupPlacementCallback = AlignRightBelow;
    }

    // Opens the Auto select options under the split button, right edges aligned (it sits at the right of the page).
    static CustomPopupPlacement[] AlignRightBelow(Size popupSize, Size targetSize, Point offset)
    {
        return [new CustomPopupPlacement(new Point(targetSize.Width - popupSize.Width, targetSize.Height + 4), PopupPrimaryAxis.Horizontal)];
    }

    // The Route column takes whatever width the other columns and the scrollbar leave.
    void RoutesGrid_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        double others = RoutesGrid.Columns.Where(column => column != RouteColumn).Sum(column => column.ActualWidth);
        double available = RoutesGrid.ActualWidth - others - SystemParameters.VerticalScrollBarWidth - 4;
        RouteColumn.Width = new DataGridLength(Math.Max(RouteColumn.MinWidth, available));
    }

    // Bound per row instead of through a row style, so the rows keep the theme's own style.
    void RoutesGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        e.Row.SetBinding(DataGridRow.DetailsVisibilityProperty,
            new Binding("IsExpanded") { Converter = VisibleWhen, Mode = BindingMode.OneWay });
    }
}

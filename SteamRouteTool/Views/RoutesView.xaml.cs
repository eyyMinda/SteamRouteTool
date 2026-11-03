using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using SteamRouteTool.Converters;
using SteamRouteTool.ViewModels;

namespace SteamRouteTool.Views;

public partial class RoutesView : UserControl
{
    static readonly VisibleWhenConverter VisibleWhen = new VisibleWhenConverter();

    public RoutesView()
    {
        InitializeComponent();
        AutoSelectPopup.CustomPopupPlacementCallback = AlignRightBelow;
        PresetsPopup.CustomPopupPlacementCallback = AlignRightBelow;
    }

    // Opens a panel under its button, right edges aligned (the buttons sit at the right of the page).
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

    // Enter in the name box saves the preset.
    void NewPresetName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not RoutesViewModel routes) { return; }
        ((TextBox)sender).GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (routes.SavePresetCommand.CanExecute(null)) { routes.SavePresetCommand.Execute(null); }
        e.Handled = true;
    }

    // Enter in a preset's name box renames it (leaving the box does too).
    void PresetName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) { return; }
        ((TextBox)sender).GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }

    // Closing the panel while a name is being edited keeps the edit; the text box doesn't lose focus on its own then.
    void PresetsPopup_Closed(object? sender, EventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox box && box.IsDescendantOf(PresetsPopup.Child))
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }
    }
}

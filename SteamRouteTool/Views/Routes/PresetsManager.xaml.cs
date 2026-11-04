using System.Windows.Controls;
using System.Windows.Input;
using SteamRouteTool.ViewModels;

namespace SteamRouteTool.Views.Routes;

public partial class PresetsManager : UserControl
{
    public PresetsManager()
    {
        InitializeComponent();
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

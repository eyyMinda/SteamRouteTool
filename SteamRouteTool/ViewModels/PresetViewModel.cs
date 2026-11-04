using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamRouteTool.Services;

namespace SteamRouteTool.ViewModels;

/// <summary>A preset in the list on the Routes page. Editing <see cref="Name"/> renames it.</summary>
public sealed partial class PresetViewModel : ObservableObject
{
    readonly RoutesViewModel owner;

    public PresetViewModel(RoutesViewModel owner, GamePreset model)
    {
        this.owner = owner;
        Model = model;
    }

    public GamePreset Model { get; }

    public string Name
    {
        get => Model.Name;
        set
        {
            owner.RenamePreset(this, value);
            // Show the saved name: trimmed, or the old one if the new name was refused. Raised after the binding's own
            // update has finished, so the text box picks it up.
            Application.Current.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(Name)));
        }
    }

    /// <summary>E.g. "12 routes and 2 relays blocked".</summary>
    public string Description => PresetLogic.Describe(Model);

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
    }

    [RelayCommand]
    void Delete() => owner.DeletePreset(this);

    // The preset list's selection box shows this.
    public override string ToString() => Name;
}

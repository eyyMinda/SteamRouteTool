using CommunityToolkit.Mvvm.ComponentModel;
using SteamRouteTool.Services;

namespace SteamRouteTool.ViewModels;

/// <summary>A page in the navigation bar. <see cref="Glyph"/> is a Segoe Fluent Icons character.</summary>
public sealed record NavigationItem(string Title, string Glyph, object Content)
{
    // Screen readers (UI Automation) name list items by ToString().
    public override string ToString() => Title;
}

/// <summary>Marker for the static "How to use" page.</summary>
public sealed class HowToViewModel
{
    public string SettingsFolder => SettingsStore.DefaultDirectory;
}

public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(RoutesViewModel routes, SettingsViewModel settings, AboutViewModel about)
    {
        Routes = routes;
        About = about;
        Pages =
        [
            new NavigationItem("Routes", "", routes),
            new NavigationItem("How to use", "", new HowToViewModel()),
            new NavigationItem("Settings", "", settings),
            new NavigationItem("About", "", about),
        ];
        selectedPage = Pages[0];
    }

    public RoutesViewModel Routes { get; }
    public AboutViewModel About { get; }
    public IReadOnlyList<NavigationItem> Pages { get; }
    public string VersionText => "Version " + AppInfo.VersionText;

    [ObservableProperty]
    NavigationItem selectedPage;
}

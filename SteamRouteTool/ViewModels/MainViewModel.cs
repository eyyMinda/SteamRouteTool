using CommunityToolkit.Mvvm.ComponentModel;
using SteamRouteTool.Services;

namespace SteamRouteTool.ViewModels;

/// <summary>A page in the navigation bar. <see cref="Glyph"/> is a Segoe Fluent Icons character.</summary>
public sealed partial class NavigationItem : ObservableObject
{
    public NavigationItem(string title, string glyph, object content)
    {
        Title = title;
        Glyph = glyph;
        Content = content;
    }

    public string Title { get; }
    public string Glyph { get; }
    public object Content { get; }

    /// <summary>Pages are created once and only shown or hidden, so they keep their scroll position, sort and expanded rows.</summary>
    [ObservableProperty]
    bool isSelected;

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
        selectedPage.IsSelected = true;
    }

    public RoutesViewModel Routes { get; }
    public AboutViewModel About { get; }
    public IReadOnlyList<NavigationItem> Pages { get; }
    public string VersionText => "Version " + AppInfo.VersionText;

    [ObservableProperty]
    NavigationItem selectedPage;

    partial void OnSelectedPageChanged(NavigationItem value)
    {
        foreach (NavigationItem page in Pages) { page.IsSelected = page == value; }
    }
}

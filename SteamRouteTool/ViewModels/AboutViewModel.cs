using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamRouteTool.Localization;
using SteamRouteTool.Services;

namespace SteamRouteTool.ViewModels;

public sealed partial class AboutViewModel : ObservableObject
{
    public AboutViewModel()
    {
        Loc.Instance.Changed += () => OnPropertyChanged(nameof(VersionLine));
    }

    /// <summary>E.g. "Version 2.0.9 · .NET 10.0.0".</summary>
    public string VersionLine => Loc.T("About.Version", AppInfo.VersionText, ".NET " + Environment.Version.ToString(3));

    [RelayCommand]
    void OpenLink(string url)
    {
        Shell.Open(url);
    }
}

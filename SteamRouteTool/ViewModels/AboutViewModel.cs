using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamRouteTool.Services;

namespace SteamRouteTool.ViewModels;

public sealed partial class AboutViewModel : ObservableObject
{
    readonly UpdateChecker updateChecker;

    public AboutViewModel(UpdateChecker updateChecker)
    {
        this.updateChecker = updateChecker;
    }

    public string VersionText => AppInfo.VersionText;
    public string RuntimeText => ".NET " + Environment.Version.ToString(3);

    [ObservableProperty]
    string updateStatus = "Not checked yet.";

    /// <summary>The release page of a newer version, if there is one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateAvailable))]
    string? updateUrl;

    public bool IsUpdateAvailable => UpdateUrl != null;

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        UpdateStatus = "Checking for updates…";
        UpdateUrl = null;
        ReleaseInfo? latest = await updateChecker.GetLatestReleaseAsync();
        if (latest == null)
        {
            UpdateStatus = "Couldn't check for updates. You can look for new versions on the releases page.";
        }
        else if (latest.Version > AppInfo.Version)
        {
            UpdateStatus = $"Version {latest.Version.ToString(3)} is available.";
            UpdateUrl = latest.Url;
        }
        else
        {
            UpdateStatus = "You're on the latest version.";
        }
    }

    [RelayCommand]
    void OpenLink(string url)
    {
        Shell.Open(url);
    }
}

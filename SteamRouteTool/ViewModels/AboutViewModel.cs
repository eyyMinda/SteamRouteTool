using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamRouteTool.Localization;
using SteamRouteTool.Services;

namespace SteamRouteTool.ViewModels;

public sealed partial class AboutViewModel : ObservableObject
{
    readonly UpdateChecker updateChecker;

    public AboutViewModel(UpdateChecker updateChecker)
    {
        this.updateChecker = updateChecker;
        Loc.Instance.Changed += () =>
        {
            OnPropertyChanged(nameof(VersionLine));
            OnPropertyChanged(nameof(UpdateStatus));
        };
    }

    /// <summary>E.g. "Version 2.0.9 · .NET 10.0.0".</summary>
    public string VersionLine => Loc.T("About.Version", AppInfo.VersionText, ".NET " + Environment.Version.ToString(3));

    // The text key and value of the update status, so it can be shown again after a language change.
    (string Key, string? Value) status = ("About.NotChecked", null);

    public string UpdateStatus => Loc.T(status.Key, status.Value);

    void SetStatus(string key, string? value = null)
    {
        status = (key, value);
        OnPropertyChanged(nameof(UpdateStatus));
    }

    /// <summary>The release page of a newer version, if there is one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateAvailable))]
    string? updateUrl;

    public bool IsUpdateAvailable => UpdateUrl != null;

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        SetStatus("About.Checking");
        UpdateUrl = null;
        ReleaseInfo? latest = await updateChecker.GetLatestReleaseAsync();
        if (latest == null)
        {
            SetStatus("About.CheckFailed");
        }
        else if (latest.Version > AppInfo.Version)
        {
            SetStatus("About.UpdateAvailable", latest.Version.ToString(3));
            UpdateUrl = latest.Url;
        }
        else
        {
            SetStatus("About.UpToDate");
        }
    }

    [RelayCommand]
    void OpenLink(string url)
    {
        Shell.Open(url);
    }
}

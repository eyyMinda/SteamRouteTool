using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamRouteTool.Localization;
using SteamRouteTool.Services;

namespace SteamRouteTool.ViewModels;

/// <summary>
/// Checking for and installing new versions: the Updates section in Settings and the "Update to …" button in the sidebar.
/// Checks run at startup and once a day while "Check for updates automatically" is on; installing always takes a click.
/// </summary>
public sealed partial class UpdatesViewModel : ObservableObject
{
    static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    readonly UpdateChecker checker;
    readonly SettingsStore store;
    readonly AppSettings settings;
    readonly Action<string> installAndRestart;
    readonly DispatcherTimer timer = new DispatcherTimer { Interval = CheckInterval };

    /// <param name="installAndRestart">Puts the verified exe at the given path in place and restarts the app.</param>
    public UpdatesViewModel(UpdateChecker checker, SettingsStore store, AppSettings settings, Action<string> installAndRestart)
    {
        this.checker = checker;
        this.store = store;
        this.settings = settings;
        this.installAndRestart = installAndRestart;
        autoCheck = settings.CheckForUpdates;
        timer.Tick += (sender, e) => _ = CheckAsync();
        Loc.Instance.Changed += () =>
        {
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(CurrentVersionText));
            OnPropertyChanged(nameof(UpdateButtonText));
        };
    }

    /// <summary>Raised when a check finds a newer version than the last one found.</summary>
    public event Action<ReleaseInfo>? UpdateFound;

    public string CurrentVersionText => Loc.T("Nav.Version", AppInfo.VersionText);

    /// <summary>The newer release, if there is one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateAvailable), nameof(UpdateButtonText), nameof(CanInstall))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    ReleaseInfo? latest;

    public bool IsUpdateAvailable => Latest != null;

    /// <summary>E.g. "Update to 2.1.1", shown in the sidebar.</summary>
    public string UpdateButtonText => Latest == null ? "" : Loc.T("Updates.UpdateTo", Latest.Version.ToString(3));

    /// <summary>Whether this copy can replace itself (not when started through dotnet in development); otherwise the release page is offered.</summary>
    public bool CanInstall => IsUpdateAvailable && Updater.CanInstall;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(CheckCommand))]
    bool isBusy;

    [ObservableProperty]
    bool autoCheck;

    partial void OnAutoCheckChanged(bool value)
    {
        settings.CheckForUpdates = value;
        store.Save(settings);
        if (value)
        {
            timer.Start();
            _ = CheckAsync();
        }
        else { timer.Stop(); }
    }

    // The status as a text key and its value, so it can be shown again after a language change.
    (string Key, string? Value) status = ("Updates.NotChecked", null);

    public string Status => Loc.T(status.Key, status.Value);

    void SetStatus(string key, string? value = null)
    {
        status = (key, value);
        OnPropertyChanged(nameof(Status));
    }

    /// <summary>Checks now if automatic checks are on, then once a day. Leftovers of the last update are removed first.</summary>
    public void Start()
    {
        Updater.CleanUp();
        if (!AutoCheck) { return; }
        timer.Start();
        _ = CheckAsync();
    }

    [RelayCommand(CanExecute = nameof(CanCheck))]
    async Task CheckAsync()
    {
        if (IsBusy) { return; }
        IsBusy = true;
        SetStatus("Updates.Checking");
        ReleaseInfo? release = await checker.GetLatestReleaseAsync();
        IsBusy = false;
        if (release == null)
        {
            SetStatus("Updates.CheckFailed");
        }
        else if (release.Version > AppInfo.Version)
        {
            bool isNew = Latest?.Version != release.Version;
            Latest = release;
            SetStatus("Updates.Available", release.Version.ToString(3));
            if (isNew) { UpdateFound?.Invoke(release); }
        }
        else
        {
            Latest = null;
            SetStatus("Updates.UpToDate");
        }
    }

    bool CanCheck() => !IsBusy;

    /// <summary>Downloads and verifies the new version, then restarts into it.</summary>
    [RelayCommand(CanExecute = nameof(CanRunInstall))]
    async Task InstallAsync()
    {
        if (Latest is not ReleaseInfo release) { return; }
        IsBusy = true;
        SetStatus("Updates.Downloading", release.Version.ToString(3));
        try
        {
            string path = await Updater.DownloadAsync(release);
            SetStatus("Updates.Restarting");
            installAndRestart(path);
        }
        catch (UpdateException ex)
        {
            SetStatus(ex.Message, ex.Detail);
            IsBusy = false;
        }
    }

    bool CanRunInstall() => CanInstall && !IsBusy;

    [RelayCommand]
    void OpenReleasePage()
    {
        Shell.Open(Latest?.Url ?? AppInfo.ReleasesUrl);
    }
}

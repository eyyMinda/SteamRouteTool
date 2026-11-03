using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamRouteTool.Localization;
using SteamRouteTool.Services;

namespace SteamRouteTool.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    readonly SettingsStore store;
    readonly AppSettings settings;

    public SettingsViewModel(SettingsStore store, AppSettings settings)
    {
        this.store = store;
        this.settings = settings;
        theme = settings.Theme;
        pingOnStartup = settings.PingOnStartup;
        unblockOnExit = settings.UnblockOnExit;
        minimizeToTray = settings.MinimizeToTray;
        language = Loc.Languages.FirstOrDefault(info => info.Code == settings.Language) ?? Loc.Languages[0];
    }

    public IReadOnlyList<LanguageInfo> Languages => Loc.Languages;

    [ObservableProperty]
    LanguageInfo language;

    partial void OnLanguageChanged(LanguageInfo value)
    {
        Loc.Instance.SetLanguage(value.Code);
        settings.Language = value.Code;
        store.Save(settings);
    }

    [ObservableProperty]
    bool unblockOnExit;

    partial void OnUnblockOnExitChanged(bool value)
    {
        settings.UnblockOnExit = value;
        store.Save(settings);
    }

    [ObservableProperty]
    bool minimizeToTray;

    partial void OnMinimizeToTrayChanged(bool value)
    {
        settings.MinimizeToTray = value;
        store.Save(settings);
    }

    // ----- Start with Windows -----

    /// <summary>Whether the sign-in task exists. Read from Task Scheduler by <see cref="LoadStartWithWindowsAsync"/>, not from the settings file.</summary>
    [ObservableProperty]
    bool startWithWindows;

    /// <summary>True while the task is being read or changed; the checkbox is disabled meanwhile.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeStartup))]
    bool isStartupBusy = true;

    public bool CanChangeStartup => !IsStartupBusy;

    bool loadingStartup;

    /// <summary>Reads whether the task exists, and points it at this exe if the app has been moved since it was created.</summary>
    public async Task LoadStartWithWindowsAsync()
    {
        try
        {
            string? registered = await Task.Run(StartupTask.GetRegisteredCommand);
            loadingStartup = true;
            StartWithWindows = registered != null;
            loadingStartup = false;
            if (registered != null && !string.Equals(registered, StartupTask.GetLaunchCommand().Command, StringComparison.OrdinalIgnoreCase))
            {
                await Task.Run(StartupTask.Enable);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception || ex is IOException || ex is UnauthorizedAccessException)
        {
            // Leave the checkbox as read; changing it reports any error.
        }
        finally
        {
            loadingStartup = false;
            IsStartupBusy = false;
        }
    }

    async partial void OnStartWithWindowsChanged(bool value)
    {
        if (loadingStartup) { return; }
        IsStartupBusy = true;
        try
        {
            await Task.Run(value ? StartupTask.Enable : StartupTask.Disable);
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception || ex is IOException || ex is UnauthorizedAccessException)
        {
            loadingStartup = true;
            StartWithWindows = !value;
            loadingStartup = false;
            MessageBox.Show(Loc.T(value ? "Settings.StartupError.On" : "Settings.StartupError.Off", ex.Message),
                "Steam Route Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsStartupBusy = false;
        }
    }

    // ----- Appearance and other settings -----

    public IReadOnlyList<LocalizedOption> ThemeOptions { get; } =
        [new LocalizedOption("System", "Theme.System"), new LocalizedOption("Light", "Theme.Light"), new LocalizedOption("Dark", "Theme.Dark")];
    public string SettingsFolder => SettingsStore.DefaultDirectory;

    [ObservableProperty]
    string theme;

    [ObservableProperty]
    bool pingOnStartup;

    partial void OnThemeChanged(string value)
    {
        AppTheme.Apply(value);
        settings.Theme = value;
        store.Save(settings);
    }

    partial void OnPingOnStartupChanged(bool value)
    {
        settings.PingOnStartup = value;
        store.Save(settings);
    }

    [RelayCommand]
    void OpenSettingsFolder()
    {
        Directory.CreateDirectory(SettingsFolder);
        Shell.Open(SettingsFolder);
    }

    [RelayCommand]
    void OpenFirewall()
    {
        Shell.Open("wf.msc");
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    }

    [ObservableProperty]
    bool unblockOnExit;

    partial void OnUnblockOnExitChanged(bool value)
    {
        settings.UnblockOnExit = value;
        store.Save(settings);
    }

    public IReadOnlyList<string> ThemeOptions { get; } = ["System", "Light", "Dark"];
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

using System.Windows;
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
    }

    public IReadOnlyList<string> ThemeOptions { get; } = ["System", "Light", "Dark"];
    public string SettingsFolder => SettingsStore.DefaultDirectory;

    [ObservableProperty]
    string theme;

    [ObservableProperty]
    bool pingOnStartup;

    partial void OnThemeChanged(string value)
    {
        ApplyTheme(value);
        settings.Theme = value;
        store.Save(settings);
    }

    partial void OnPingOnStartupChanged(bool value)
    {
        settings.PingOnStartup = value;
        store.Save(settings);
    }

    public static void ApplyTheme(string theme)
    {
        Application.Current.ThemeMode = theme switch
        {
            "Light" => ThemeMode.Light,
            "Dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
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

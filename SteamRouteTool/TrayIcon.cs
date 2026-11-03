using System.Windows;
using SteamRouteTool.Services;
using SteamRouteTool.ViewModels;

namespace SteamRouteTool;

/// <summary>
/// The notification area icon, shown while "Minimize to tray" is on. Minimizing the window then hides it from the taskbar;
/// clicking the icon brings it back, and its menu has Open, Presets, Unblock all and Exit.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    readonly Window window;
    readonly RoutesViewModel routes;
    readonly SettingsStore store;
    readonly AppSettings settings;
    readonly NotifyIcon icon = new NotifyIcon();
    WindowState restoreState = WindowState.Normal;

    public TrayIcon(Window window, RoutesViewModel routes, SettingsViewModel settingsViewModel, SettingsStore store, AppSettings settings, Action exit)
    {
        this.window = window;
        this.routes = routes;
        this.store = store;
        this.settings = settings;

        icon.Click += Restore;
        icon.ContextMenuRequested += () => icon.ShowMenu(
        [
            new NotifyIcon.MenuItem("Open Steam Route Tool", Restore, IsDefault: true),
            new NotifyIcon.MenuItem("Presets", Submenu: GetPresetItems()),
            new NotifyIcon.MenuItem("Unblock all", () => routes.UnblockAllCommand.Execute(null), IsEnabled: routes.UnblockAllCommand.CanExecute(null)),
            null,
            new NotifyIcon.MenuItem("Exit", exit)
        ]);
        icon.BalloonClick += Restore;

        UpdateText();
        routes.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(RoutesViewModel.BlockedSummary)) { UpdateText(); }
        };
        settingsViewModel.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.MinimizeToTray)) { UpdateVisibility(); }
        };
        window.IsVisibleChanged += (sender, e) => UpdateVisibility();
        window.StateChanged += (sender, e) =>
        {
            if (window.WindowState == WindowState.Minimized && settings.MinimizeToTray) { HideToTray(); }
            else if (window.WindowState != WindowState.Minimized) { restoreState = window.WindowState; }
        };
        // Not UpdateVisibility(): the window isn't shown yet, and starting in the tray goes through HideToTray().
        icon.Visible = settings.MinimizeToTray;
    }

    /// <summary>Hides the window to the tray. The first time, a notification says the app is still running.</summary>
    public void HideToTray()
    {
        window.Hide();
        UpdateVisibility();
        if (!settings.TrayHintShown)
        {
            settings.TrayHintShown = true;
            store.Save(settings);
            icon.ShowBalloon("Steam Route Tool is still running",
                "It keeps your routes as they are. Click the icon to open it, or right-click it to exit.");
        }
    }

    /// <summary>Shows the window again, as it was before it was minimized.</summary>
    public void Restore()
    {
        window.Show();
        if (window.WindowState == WindowState.Minimized) { window.WindowState = restoreState; }
        window.Activate();
    }

    public void Dispose()
    {
        icon.Dispose();
    }

    // Visible while the option is on, and always while the window is hidden (so it can't get lost).
    void UpdateVisibility()
    {
        icon.Visible = settings.MinimizeToTray || !window.IsVisible;
    }

    // The selected game's presets, the one matching what's blocked ticked. Choosing one applies it.
    List<NotifyIcon.MenuItem?> GetPresetItems()
    {
        if (routes.Presets.Count == 0) { return [new NotifyIcon.MenuItem("No presets yet", IsEnabled: false)]; }
        return routes.Presets
            .Select(preset => (NotifyIcon.MenuItem?)new NotifyIcon.MenuItem(preset.Name, () => _ = routes.ApplyPresetAsync(preset),
                IsEnabled: routes.CanChangeRules, IsChecked: preset == routes.SelectedPreset))
            .ToList();
    }

    void UpdateText()
    {
        string blocked = routes.BlockedSummary;
        icon.Text = blocked.Length == 0 ? "Steam Route Tool" : "Steam Route Tool · " + blocked;
    }
}

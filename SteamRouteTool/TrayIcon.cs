using System.Windows;
using SteamRouteTool.Localization;
using SteamRouteTool.Models;
using SteamRouteTool.Services;
using SteamRouteTool.ViewModels;

namespace SteamRouteTool;

/// <summary>
/// The notification area icon, shown while "Minimize to tray" is on. Minimizing the window then hides it from the taskbar;
/// clicking the icon brings it back, and its menu switches games and presets, runs Auto select, unblocks all routes and exits.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    readonly Window window;
    readonly RoutesViewModel routes;
    readonly SettingsStore store;
    readonly AppSettings settings;
    readonly NotifyIcon icon = new NotifyIcon();
    WindowState restoreState = WindowState.Normal;

    public TrayIcon(Window window, RoutesViewModel routes, SettingsViewModel settingsViewModel, SettingsStore store, AppSettings settings,
        Action openSettings, Action exit)
    {
        this.window = window;
        this.routes = routes;
        this.store = store;
        this.settings = settings;

        icon.Click += Restore;
        // Grouped: open; what to play on (game, preset); actions; a status line (info only); settings and exit.
        icon.ContextMenuRequested += () =>
        {
            List<NotifyIcon.MenuItem?> items =
            [
                new NotifyIcon.MenuItem(Loc.T("Tray.Open"), Restore, IsDefault: true),
                null,
                new NotifyIcon.MenuItem(routes.SelectedGame.Name, Submenu: GetGameItems()),
                new NotifyIcon.MenuItem(Loc.T("Tray.Preset", routes.SelectedPreset?.Name ?? Loc.T("Tray.PresetNone")), Submenu: GetPresetItems()),
                null,
                new NotifyIcon.MenuItem(Loc.T("Tray.AutoSelect", routes.AutoSelectLabel), () => routes.ApplyAutoSelectCommand.Execute(null),
                    IsEnabled: routes.ApplyAutoSelectCommand.CanExecute(null)),
                new NotifyIcon.MenuItem(Loc.T("Tray.UnblockAll"), () => routes.UnblockAllCommand.Execute(null), IsEnabled: routes.UnblockAllCommand.CanExecute(null)),
            ];
            if (StatusLine() is string status) { items.AddRange([null, new NotifyIcon.MenuItem(status, IsEnabled: false)]); }
            items.AddRange([null, new NotifyIcon.MenuItem(Loc.T("Nav.Settings"), () => { Restore(); openSettings(); }), new NotifyIcon.MenuItem(Loc.T("Tray.Exit"), exit)]);
            icon.ShowMenu(items);
        };
        icon.BalloonClick += Restore;

        UpdateText();
        Loc.Instance.Changed += UpdateText;
        routes.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName is nameof(RoutesViewModel.BlockedSummary) or nameof(RoutesViewModel.SelectedPreset) or nameof(RoutesViewModel.SelectedGame)
                or nameof(RoutesViewModel.BestPingText) or nameof(RoutesViewModel.WorstPingText))
            {
                UpdateText();
            }
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
            icon.ShowBalloon(Loc.T("Tray.HintTitle"), Loc.T("Tray.HintText"));
        }
    }

    /// <summary>A notification from the tray icon, e.g. about a new version; clicking it opens the window.</summary>
    public void ShowNotification(string title, string text)
    {
        icon.ShowBalloon(title, text);
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

    // The supported games, the selected one ticked. Choosing one switches to it and applies its saved profile.
    List<NotifyIcon.MenuItem?> GetGameItems()
    {
        return Game.Supported
            .Select(game => (NotifyIcon.MenuItem?)new NotifyIcon.MenuItem(game.Name, () => routes.SelectedGame = game,
                IsChecked: game == routes.SelectedGame))
            .ToList();
    }

    // The selected game's presets, the one matching what's blocked ticked. Choosing one applies it.
    List<NotifyIcon.MenuItem?> GetPresetItems()
    {
        if (routes.Presets.Count == 0) { return [new NotifyIcon.MenuItem(Loc.T("Tray.NoPresets"), IsEnabled: false)]; }
        return routes.Presets
            .Select(preset => (NotifyIcon.MenuItem?)new NotifyIcon.MenuItem(preset.Name, () => _ = routes.ApplyPresetAsync(preset),
                IsEnabled: routes.CanChangeRules, IsChecked: preset == routes.SelectedPreset))
            .ToList();
    }

    // The tooltip: the app, the game and preset, and the status line, one per line (Windows keeps up to 127 characters).
    void UpdateText()
    {
        if (StatusLine() is not string status)
        {
            icon.Text = "Steam Route Tool";
            return;
        }
        string preset = Loc.T("Tray.Preset", routes.SelectedPreset?.Name ?? Loc.T("Tray.PresetNone"));
        icon.Text = "Steam Route Tool\n" + routes.SelectedGame.Name + " · " + preset + "\n" + status;
    }

    // E.g. "40 blocked · best 12 ms · worst 27 ms", or null before the routes have loaded.
    string? StatusLine()
    {
        if (routes.BlockedSummary.Length == 0) { return null; }
        return routes.BestPingText == "–"
            ? Loc.T("Tray.StatusNoPing", routes.BlockedRouteCount)
            : Loc.T("Tray.Status", routes.BlockedRouteCount, routes.BestPingText, routes.WorstPingText);
    }
}

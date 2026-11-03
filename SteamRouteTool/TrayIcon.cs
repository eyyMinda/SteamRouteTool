using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using SteamRouteTool.Services;
using SteamRouteTool.ViewModels;
using Forms = System.Windows.Forms;

namespace SteamRouteTool;

/// <summary>
/// The notification area icon, shown while "Minimize to tray" is on. Minimizing the window then hides it from the taskbar;
/// clicking the icon brings it back, and its menu has Open, Unblock all and Exit.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    readonly Window window;
    readonly RoutesViewModel routes;
    readonly SettingsStore store;
    readonly AppSettings settings;
    readonly Forms.NotifyIcon icon;
    readonly ContextMenu menu;
    WindowState restoreState = WindowState.Normal;

    public TrayIcon(Window window, RoutesViewModel routes, SettingsViewModel settingsViewModel, SettingsStore store, AppSettings settings, Action exit)
    {
        this.window = window;
        this.routes = routes;
        this.store = store;
        this.settings = settings;

        // A WPF menu, so it follows the app's light or dark theme.
        menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        menu.Items.Add(MenuItem("Open Steam Route Tool", Restore, bold: true));
        menu.Items.Add(new MenuItem { Header = "Unblock all", Command = routes.UnblockAllCommand });
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Exit", exit));
        // The menu only closes on a click elsewhere if it's in the foreground window.
        menu.Opened += (sender, e) =>
        {
            if (PresentationSource.FromVisual(menu) is HwndSource source) { SetForegroundWindow(source.Handle); }
        };

        using Stream iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/wifi.ico"))!.Stream;
        icon = new Forms.NotifyIcon { Icon = new System.Drawing.Icon(iconStream, Forms.SystemInformation.SmallIconSize) };
        icon.MouseClick += (sender, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) { Restore(); }
            else if (e.Button == Forms.MouseButtons.Right) { menu.IsOpen = true; }
        };
        icon.BalloonTipClicked += (sender, e) => Restore();

        UpdateText();
        routes.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(RoutesViewModel.BlockedSummary)) { UpdateText(); }
        };
        settingsViewModel.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.MinimizeToTray)) { UpdateVisibility(); }
        };
        window.StateChanged += (sender, e) =>
        {
            if (window.WindowState == WindowState.Minimized && settings.MinimizeToTray) { HideToTray(); }
            else if (window.WindowState != WindowState.Minimized) { restoreState = window.WindowState; }
        };
        UpdateVisibility();
    }

    /// <summary>Hides the window to the tray. The first time, a notification says the app is still running.</summary>
    public void HideToTray()
    {
        window.Hide();
        icon.Visible = true;
        if (!settings.TrayHintShown)
        {
            settings.TrayHintShown = true;
            store.Save(settings);
            icon.ShowBalloonTip(5000, "Steam Route Tool is still running",
                "It keeps your routes as they are. Click the icon to open it, or right-click it to exit.", Forms.ToolTipIcon.None);
        }
    }

    /// <summary>Shows the window again, as it was before it was minimized.</summary>
    public void Restore()
    {
        window.Show();
        if (window.WindowState == WindowState.Minimized) { window.WindowState = restoreState; }
        window.Activate();
        UpdateVisibility();
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }

    // Visible while the option is on, and always while the window is hidden (so it can't get lost).
    void UpdateVisibility()
    {
        icon.Visible = settings.MinimizeToTray || !window.IsVisible;
    }

    void UpdateText()
    {
        string blocked = routes.BlockedSummary;
        // The tooltip is limited to 127 characters.
        string text = blocked.Length == 0 ? "Steam Route Tool" : "Steam Route Tool · " + blocked;
        icon.Text = text.Length > 127 ? text[..127] : text;
    }

    static MenuItem MenuItem(string header, Action action, bool bold = false)
    {
        MenuItem item = new MenuItem { Header = header, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
        item.Click += (sender, e) => action();
        return item;
    }

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);
}

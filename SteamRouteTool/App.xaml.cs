using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using SteamRouteTool.Localization;
using SteamRouteTool.Services;
using SteamRouteTool.ViewModels;

namespace SteamRouteTool;

public partial class App : Application
{
    // Clickable controls show the hand cursor. Changing the default value (instead of replacing the theme's styles)
    // keeps the Fluent look and survives switching between light and dark mode. Must run before any of them are created.
    static App()
    {
        foreach (Type clickable in new[] { typeof(ButtonBase), typeof(ComboBox), typeof(ListBoxItem), typeof(DataGridColumnHeader) })
        {
            FrameworkElement.CursorProperty.OverrideMetadata(clickable, new FrameworkPropertyMetadata(Cursors.Hand));
        }
    }

    // Named per signed-in user session; a second instance asks the first one to show its window, then exits.
    const string InstanceName = @"Local\SteamRouteTool";
    const string ShowEventName = @"Local\SteamRouteTool.Show";

    // Held for the whole run; a field so it isn't collected (which would release it).
    Mutex? instanceMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!TryBecomeTheOnlyInstance())
        {
            Shutdown();
            return;
        }
        DispatcherUnhandledException += OnUnhandledException;

        HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(15) };
        // GitHub's API rejects requests without a User-Agent.
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SteamRouteTool/" + AppInfo.VersionText);

        SettingsStore settingsStore = new SettingsStore(Path.Combine(SettingsStore.DefaultDirectory, "settings.json"));
        AppSettings settings = settingsStore.Load();
        Loc.Instance.SetLanguage(settings.Language);
        AppTheme.Apply(settings.Theme);
        AppTheme.FollowSystemChanges();

        RoutesViewModel routes = new RoutesViewModel(new SdrConfigClient(httpClient), new FirewallService(), new PingService(),
            new PingCache(PingCache.DefaultPath), settingsStore, settings);
        UpdatesViewModel updates = new UpdatesViewModel(new UpdateChecker(httpClient), settingsStore, settings, InstallAndRestart);
        SettingsViewModel settingsViewModel = new SettingsViewModel(settingsStore, settings, updates);
        MainViewModel main = new MainViewModel(routes, settingsViewModel, new AboutViewModel(), updates);

        MainWindow window = new MainWindow() { DataContext = main };
        // Closing covers the close button and Alt+F4; SessionEnding covers signing out and shutting down.
        // Being ended from Task Manager or crashing skips this; the next launch then re-applies the saved profile.
        bool cleanedUp = false;
        void CleanUp()
        {
            if (cleanedUp) { return; }
            cleanedUp = true;
            routes.SavePingCache();
            if (settings.UnblockOnExit) { routes.RemoveRulesOnExit(); }
        }
        window.Closing += (sender, args) => CleanUp();
        window.Activated += (sender, args) => _ = routes.CheckFirewallAsync();
        SessionEnding += (sender, args) => CleanUp();
        cleanUp = CleanUp;
        MainWindow = window;

        // Exit from the tray menu also works while the window has never been shown.
        TrayIcon tray = new TrayIcon(window, routes, settingsViewModel, settingsStore, settings, () =>
        {
            CleanUp();
            Shutdown();
        });
        Exit += (sender, args) => tray.Dispose();
        ListenForShowRequests(tray);
        // In the tray, a new version is announced with a notification; with the window open, the sidebar shows it.
        updates.UpdateFound += release =>
        {
            if (!window.IsVisible) { tray.ShowNotification(Loc.T("Tray.UpdateTitle", release.Version.ToString(3)), Loc.T("Tray.UpdateText")); }
        };

        // Started at sign-in: stay out of the way, in the tray or minimized on the taskbar.
        if (e.Args.Contains(StartupTask.MinimizedArgument))
        {
            if (settings.MinimizeToTray) { tray.HideToTray(); }
            else
            {
                window.WindowState = WindowState.Minimized;
                window.Show();
            }
        }
        else { window.Show(); }

        updates.Start();
        _ = settingsViewModel.LoadStartWithWindowsAsync();
        await routes.InitializeAsync();
    }

    // Set once the window exists: saves state and, with "unblock on exit", removes the rules.
    Action? cleanUp;

    // The verified new exe is put in place first, so a failure leaves this copy running as it was; then this copy closes
    // like on exit and starts the new one (which only becomes the running instance once this one has let go).
    void InstallAndRestart(string newExePath)
    {
        Updater.Swap(newExePath);
        cleanUp?.Invoke();
        instanceMutex?.ReleaseMutex();
        instanceMutex?.Dispose();
        instanceMutex = null;
        Process.Start(new ProcessStartInfo(Updater.ExePath!) { UseShellExecute = true });
        Shutdown();
    }

    bool TryBecomeTheOnlyInstance()
    {
        bool createdNew;
        try
        {
            instanceMutex = new Mutex(true, InstanceName, out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            // Held by an instance running with other rights (e.g. elevated): that one is the running instance.
            createdNew = false;
        }
        if (createdNew) { return true; }

        try
        {
            // This process was just started by the user, so it may hand the foreground to the running instance.
            AllowSetForegroundWindow(AnyProcess);
            using EventWaitHandle show = EventWaitHandle.OpenExisting(ShowEventName);
            show.Set();
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException || ex is UnauthorizedAccessException) { }
        return false;
    }

    // Brings the window back when the app is started again while it's already running (e.g. hidden in the tray).
    void ListenForShowRequests(TrayIcon tray)
    {
        EventWaitHandle show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        ThreadPool.RegisterWaitForSingleObject(show, (state, timedOut) => Dispatcher.BeginInvoke(tray.Restore), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    const int AnyProcess = -1; // ASFW_ANY

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);

    // Last resort: keep the app running and tell the user, instead of closing without a word.
    void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        MessageBox.Show(Loc.T("App.Error", e.Exception.Message, AppInfo.IssuesUrl),
            "Steam Route Tool", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

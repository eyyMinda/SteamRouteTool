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
        SettingsViewModel settingsViewModel = new SettingsViewModel(settingsStore, settings);
        MainViewModel main = new MainViewModel(routes, settingsViewModel, new AboutViewModel(new UpdateChecker(httpClient)));

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
        SessionEnding += (sender, args) => CleanUp();
        MainWindow = window;

        // Exit from the tray menu also works while the window has never been shown.
        TrayIcon tray = new TrayIcon(window, routes, settingsViewModel, settingsStore, settings, () =>
        {
            CleanUp();
            Shutdown();
        });
        Exit += (sender, args) => tray.Dispose();
        ListenForShowRequests(tray);

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

        _ = main.About.CheckForUpdatesAsync();
        _ = settingsViewModel.LoadStartWithWindowsAsync();
        await routes.InitializeAsync();
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

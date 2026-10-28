using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(15) };
        // GitHub's API rejects requests without a User-Agent.
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SteamRouteTool/" + AppInfo.VersionText);

        SettingsStore settingsStore = new SettingsStore(Path.Combine(SettingsStore.DefaultDirectory, "settings.json"));
        AppSettings settings = settingsStore.Load();
        SettingsViewModel.ApplyTheme(settings.Theme);

        RoutesViewModel routes = new RoutesViewModel(new SdrConfigClient(httpClient), new FirewallService(), new PingService(),
            new PingCache(PingCache.DefaultPath), settingsStore, settings);
        MainViewModel main = new MainViewModel(routes, new SettingsViewModel(settingsStore, settings), new AboutViewModel(new UpdateChecker(httpClient)));

        MainWindow window = new MainWindow() { DataContext = main };
        window.Closing += (sender, args) => routes.SavePingCache();
        MainWindow = window;
        window.Show();

        _ = main.About.CheckForUpdatesAsync();
        await routes.InitializeAsync();
    }

    // Last resort: keep the app running and tell the user, instead of closing without a word.
    void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        MessageBox.Show("Something went wrong:" + Environment.NewLine + e.Exception.Message + Environment.NewLine + Environment.NewLine +
            "If this keeps happening, please report it at " + AppInfo.IssuesUrl,
            "Steam Route Tool", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

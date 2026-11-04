using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SteamRouteTool.ViewModels;

namespace SteamRouteTool;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    // Shortcuts, from any page: Ctrl+F search, F5 ping all, Ctrl+1/2/3 switch game. They open the Routes page.
    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel main) { return; }
        RoutesViewModel routes = main.Routes;
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;

        if (ctrl && e.Key == Key.F)
        {
            main.SelectedPage = main.Pages[0];
            // After the page has been shown, so the search box can take focus.
            Dispatcher.BeginInvoke(routes.RequestSearchFocus, DispatcherPriority.Input);
            e.Handled = true;
        }
        else if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
        {
            main.SelectedPage = main.Pages[0];
            if (routes.PingAllCommand.CanExecute(null)) { routes.PingAllCommand.Execute(null); }
            e.Handled = true;
        }
        else if (ctrl && e.Key is >= Key.D1 and <= Key.D9 or >= Key.NumPad1 and <= Key.NumPad9)
        {
            int index = e.Key >= Key.NumPad1 ? e.Key - Key.NumPad1 : e.Key - Key.D1;
            if (index >= routes.Games.Count) { return; }
            main.SelectedPage = main.Pages[0];
            routes.SelectedGame = routes.Games[index];
            e.Handled = true;
        }
    }
}

using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SteamRouteTool;

/// <summary>
/// A notification area icon on the Windows API (Shell_NotifyIcon). WPF has none, and the Windows Forms one would add
/// about 10 MB to the self-contained exe. Use from the UI thread.
/// </summary>
public sealed class NotifyIcon : IDisposable
{
    const int CallbackMessage = 0x8000 + 1; // WM_APP + 1
    const int IconId = 1;

    const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    const int NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    const int NOTIFYICON_VERSION_4 = 4;
    const int MF_STRING = 0x0, MF_GRAYED = 0x1, MF_CHECKED = 0x8, MF_POPUP = 0x10, MF_SEPARATOR = 0x800;
    const int TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100, TPM_NONOTIFY = 0x80;
    const int WM_NULL = 0x0;
    const int AllowDark = 1;
    // With version 4, the low word of lParam is the event.
    const int NIN_SELECT = 0x400, NIN_KEYSELECT = 0x401, NIN_BALLOONUSERCLICK = 0x405, WM_CONTEXTMENU = 0x7B;

    readonly HwndSource messageWindow;
    readonly IntPtr iconHandle;
    // Sent to top-level windows when Explorer restarts; the icon has to be added again.
    readonly int taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
    string text = "";
    bool visible;

    public NotifyIcon()
    {
        AllowDarkMenus();
        // A hidden top-level window (not message-only, so it receives TaskbarCreated) for the icon's mouse messages.
        messageWindow = new HwndSource(new HwndSourceParameters("SteamRouteTool tray") { Width = 0, Height = 0, WindowStyle = 0 });
        messageWindow.AddHook(WndProc);
        iconHandle = LoadAppIcon();
    }

    /// <summary>Left click, or Enter on the focused icon.</summary>
    public event Action? Click;
    /// <summary>Right click, or the menu key on the focused icon.</summary>
    public event Action? ContextMenuRequested;
    public event Action? BalloonClick;

    /// <summary>The window that receives the icon's messages; the foreground window while the icon's menu is open.</summary>
    public IntPtr Handle => messageWindow.Handle;

    public bool Visible
    {
        get => visible;
        set
        {
            if (value == visible) { return; }
            visible = value;
            if (value) { Add(); }
            else
            {
                NOTIFYICONDATA data = NewData(0);
                Shell_NotifyIcon(NIM_DELETE, ref data);
            }
        }
    }

    /// <summary>The tooltip, up to 127 characters.</summary>
    public string Text
    {
        get => text;
        set
        {
            text = value.Length > 127 ? value[..127] : value;
            if (!visible) { return; }
            NOTIFYICONDATA data = NewData(NIF_TIP | NIF_SHOWTIP);
            Shell_NotifyIcon(NIM_MODIFY, ref data);
        }
    }

    /// <summary>Shows a notification from the icon (a toast on Windows 10 and 11). The icon must be visible.</summary>
    public void ShowBalloon(string title, string message)
    {
        if (!visible) { return; }
        NOTIFYICONDATA data = NewData(NIF_INFO);
        data.szInfoTitle = title.Length > 63 ? title[..63] : title;
        data.szInfo = message.Length > 255 ? message[..255] : message;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    /// <summary>
    /// A menu entry. <see cref="IsDefault"/> shows it in bold (the action of a click on the icon); an entry with
    /// <see cref="Submenu"/> opens that menu instead of running an action.
    /// </summary>
    public sealed record MenuItem(string Text, Action? Action = null, bool IsDefault = false, bool IsEnabled = true, bool IsChecked = false,
        IReadOnlyList<MenuItem?>? Submenu = null);

    /// <summary>Shows a menu at the mouse pointer; null entries are separators. Returns once the menu has closed.</summary>
    public void ShowMenu(IReadOnlyList<MenuItem?> items)
    {
        IntPtr menu = CreatePopupMenu();
        try
        {
            // An entry's command ID is its index in this list plus 1, as 0 means nothing was chosen.
            List<Action> actions = new List<Action>();
            AppendItems(menu, items, actions);

            // The documented way to show a notification area menu: the owner must be the foreground window, or the
            // menu doesn't close on a click elsewhere; WM_NULL afterwards makes the next click on the icon work.
            GetCursorPos(out POINT point);
            SetForegroundWindow(Handle);
            int chosen = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY, point.X, point.Y, Handle, IntPtr.Zero);
            PostMessage(Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            // After the menu is gone, not inside this window message (Exit disposes this window).
            if (chosen > 0) { messageWindow.Dispatcher.BeginInvoke(actions[chosen - 1]); }
        }
        finally
        {
            // Also destroys the submenus.
            DestroyMenu(menu);
        }
    }

    // Menus treat "&" as the start of a keyboard shortcut; texts such as preset names are shown as typed.
    static string Escape(string text) => text.Replace("&", "&&");

    static void AppendItems(IntPtr menu, IReadOnlyList<MenuItem?> items, List<Action> actions)
    {
        foreach (MenuItem? item in items)
        {
            if (item == null)
            {
                AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
                continue;
            }
            int flags = (item.IsEnabled ? 0 : MF_GRAYED) | (item.IsChecked ? MF_CHECKED : 0);
            if (item.Submenu != null)
            {
                IntPtr submenu = CreatePopupMenu();
                AppendItems(submenu, item.Submenu, actions);
                AppendMenu(menu, MF_POPUP | flags, submenu, Escape(item.Text));
                continue;
            }
            actions.Add(item.Action ?? (() => { }));
            AppendMenu(menu, MF_STRING | flags, actions.Count, Escape(item.Text));
            if (item.IsDefault) { SetMenuDefaultItem(menu, actions.Count, 0); }
        }
    }

    public void Dispose()
    {
        Visible = false;
        messageWindow.Dispose();
        if (iconHandle != IntPtr.Zero) { DestroyIcon(iconHandle); }
    }

    void Add()
    {
        NOTIFYICONDATA data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIcon(NIM_ADD, ref data);
        data.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, ref data);
    }

    NOTIFYICONDATA NewData(int flags)
    {
        return new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = messageWindow.Handle,
            uID = IconId,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = iconHandle,
            szTip = text,
            szInfo = "",
            szInfoTitle = ""
        };
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            switch ((int)(lParam.ToInt64() & 0xFFFF))
            {
                case NIN_SELECT:
                case NIN_KEYSELECT:
                    Click?.Invoke();
                    break;
                case WM_CONTEXTMENU:
                    ContextMenuRequested?.Invoke();
                    break;
                case NIN_BALLOONUSERCLICK:
                    BalloonClick?.Invoke();
                    break;
            }
            handled = true;
        }
        else if (msg == taskbarCreatedMessage && visible)
        {
            Add();
        }
        return IntPtr.Zero;
    }

    // Windows draws the menu in light colours unless the app opts in to dark mode for its Win32 menus. The API
    // (uxtheme ordinal 135, SetPreferredAppMode) is undocumented but stable since Windows 10 1903; without it the menu stays light.
    static void AllowDarkMenus()
    {
        if (Environment.OSVersion.Version.Build < 18362) { return; }
        try
        {
            SetPreferredAppMode(AllowDark);
            FlushMenuThemes();
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException || ex is DllNotFoundException) { }
    }

    // The app's own icon (ApplicationIcon), at the small icon size. In development the app runs through dotnet.exe,
    // so the icon is read from the app host exe next to the dll.
    static IntPtr LoadAppIcon()
    {
        string path = Environment.ProcessPath!;
        if (Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            path = Path.Combine(AppContext.BaseDirectory, "SteamRouteTool.exe");
        }
        IntPtr[] small = new IntPtr[1];
        return ExtractIconEx(path, 0, null, small, 1) > 0 ? small[0] : IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, int count);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool AppendMenu(IntPtr menu, int flags, IntPtr id, string? text);

    [DllImport("user32.dll")]
    static extern bool SetMenuDefaultItem(IntPtr menu, int item, int byPosition);

    [DllImport("user32.dll")]
    static extern int TrackPopupMenuEx(IntPtr menu, int flags, int x, int y, IntPtr window, IntPtr parameters);

    [DllImport("user32.dll")]
    static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    static extern void FlushMenuThemes();
}

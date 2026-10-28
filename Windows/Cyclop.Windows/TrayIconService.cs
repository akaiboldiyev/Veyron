using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;

namespace Cyclop.Windows;

/// <summary>Notification-area entry point for Veyron.</summary>
internal sealed class TrayIconService : IDisposable
{
    private const int WmApp = 0x8000;
    private const int TrayMessage = WmApp + 73;
    private const int WmContextMenu = 0x007B;
    private const int WmRButtonUp = 0x0205;
    private const int WmLButtonUp = 0x0202;
    private const int NinSelect = 0x0400;
    private const int NinKeySelect = 0x0401;
    private const int NimAdd = 0;
    private const int NimDelete = 2;
    private const int NimSetVersion = 4;
    private const uint NotifyIconVersion4 = 4;
    private const int NifMessage = 1;
    private const int NifIcon = 2;
    private const int NifTip = 4;
    private static readonly IntPtr ApplicationIcon = new(32512);
    public static TrayIconService Current { get; } = new();

    private HwndSource? source;
    private NOTIFYICONDATA data;
    private uint taskbarCreated;
    private bool visible;
    private ContextMenu? menu;
    private TrayIconService() { }

    public void Initialize(bool show)
    {
        if (source is not null) { SetVisible(show); return; }
        var parameters = new HwndSourceParameters("Veyron notification icon") { Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000) };
        source = new HwndSource(parameters);
        source.AddHook(WindowProc);
        taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = source.Handle,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = TrayMessage,
            hIcon = LoadIcon(IntPtr.Zero, ApplicationIcon),
            szTip = "Veyron v1",
            szInfo = "",
            szInfoTitle = ""
        };
        SetVisible(show);
    }

    public void SetVisible(bool show)
    {
        if (source is null || visible == show) return;
        visible = show;
        if(show)AddIcon();else Shell_NotifyIcon(NimDelete, ref data);
    }

    private void AddIcon()
    {
        if(!Shell_NotifyIcon(NimAdd,ref data))return;
        data.uTimeoutOrVersion=NotifyIconVersion4;
        Shell_NotifyIcon(NimSetVersion,ref data);
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if(unchecked((uint)message)==taskbarCreated&&visible){AddIcon();return IntPtr.Zero;}
        if (message != TrayMessage) return IntPtr.Zero;
        var notification = unchecked((int)(lParam.ToInt64() & 0xffff));
        if (notification is WmContextMenu or WmRButtonUp) { ShowMenu(); handled = true; }
        else if (notification is WmLButtonUp or NinSelect or NinKeySelect) { MainWindow.OpenFromTray(); handled = true; }
        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        if (menu is null)
        {
            menu = new ContextMenu();
            menu.Items.Add(new MenuItem { Header = $"Veyron v{typeof(TrayIconService).Assembly.GetName().Version?.ToString(3) ?? "dev"}", IsEnabled = false });
            menu.Items.Add(new Separator());
            var open = new MenuItem { Header = "Открыть панель" }; open.Click += (_, _) => MainWindow.OpenFromTray(); menu.Items.Add(open);
            var startup = new MenuItem { Header = "Запускать при входе в Windows", IsCheckable = true };
            startup.Click += async (_, _) =>
            {
                await DataStore.Current.SetLaunchAtLoginAsync(!DataStore.Current.LaunchAtLogin);
                startup.IsChecked = DataStore.Current.LaunchAtLogin;
            };
            menu.Items.Add(startup);
            var privacy = new MenuItem { Header = "Скрывать содержимое" };
            var all = new MenuItem { Header = "Все разделы", IsCheckable = true };
            all.Click += (_, _) => { DataStore.Current.SetHideSensitiveContents(!DataStore.Current.HideSensitiveContents); MainWindow.ApplyPrivacyNow(); };
            privacy.Items.Add(all);
            foreach (var (id, title) in new[] { ("clipboard", "Буфер обмена"), ("snippets", "Заготовки"), ("calendar", "Календарь"), ("notes", "Заметки") })
            {
                var section = new MenuItem { Header = title, IsCheckable = true, Tag = id };
                section.Click += (_, _) => { DataStore.Current.SetPrivacySection(id, !DataStore.Current.PrivacySections.Contains(id)); MainWindow.ApplyPrivacyNow(); };
                privacy.Items.Add(section);
            }
            privacy.SubmenuOpened += (_, _) => RefreshPrivacy(privacy);
            menu.Items.Add(privacy);
            menu.Items.Add(new Separator());
            var quit = new MenuItem { Header = "Выход из Veyron" }; quit.Click += (_, _) => Application.Current.Shutdown(); menu.Items.Add(quit);
            menu.Opened += async (_, _) =>
            {
                await DataStore.Current.RefreshLaunchAtLoginStatusAsync();
                startup.IsChecked = DataStore.Current.LaunchAtLogin;
                RefreshPrivacy(privacy);
            };
        }
        SetForegroundWindow(source!.Handle);
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private static void RefreshPrivacy(MenuItem root)
    {
        if (root.Items.Count == 0) return;
        ((MenuItem)root.Items[0]).IsChecked = DataStore.Current.HideSensitiveContents;
        for (var i = 1; i < root.Items.Count; i++)
            if (root.Items[i] is MenuItem section && section.Tag is string id)
                section.IsChecked = DataStore.Current.PrivacySections.Contains(id);
    }

    public void Dispose()
    {
        SetVisible(false);
        menu = null;
        source?.RemoveHook(WindowProc);
        source?.Dispose();
        source = null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
}

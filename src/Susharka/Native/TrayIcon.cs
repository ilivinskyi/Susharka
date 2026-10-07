using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace Susharka.Native;

/// <summary>Notification-area icon with a WPF context menu, without pulling in WinForms.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const int CallbackMessage = 0x8000 + 42; // WM_APP + 42
    private const int WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_LBUTTONDBLCLK = 0x0203;

    private readonly HwndSource _source;
    private readonly int _taskbarCreated;
    private IntPtr _icon;
    private Win32.NOTIFYICONDATA _data;
    private bool _added;

    public event Action? Click;
    public Func<ContextMenu>? MenuFactory { get; set; }

    public TrayIcon(string tooltip)
    {
        // A real (hidden) top-level window, so it receives the TaskbarCreated broadcast after Explorer restarts.
        _source = new HwndSource(new HwndSourceParameters("SusharkaTray")
        {
            Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000), // WS_POPUP
        });
        _source.AddHook(WndProc);
        _taskbarCreated = Win32.RegisterWindowMessage("TaskbarCreated");

        if (Environment.ProcessPath is { } exe) Win32.ExtractIconEx(exe, 0, out _, out _icon, 1);

        _data = new Win32.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<Win32.NOTIFYICONDATA>(),
            hWnd = _source.Handle,
            uID = 1,
            uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
            szTip = tooltip,
            szInfo = "",
            szInfoTitle = "",
        };
        Add();
    }

    private void Add()
    {
        _added = Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref _data);
    }

    public void ShowBalloon(string title, string text)
    {
        if (!_added) return;
        var d = _data;
        d.uFlags = Win32.NIF_INFO;
        d.szInfoTitle = title;
        d.szInfo = text;
        d.dwInfoFlags = Win32.NIIF_INFO;
        Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref d);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            switch (lParam.ToInt32() & 0xFFFF)
            {
                case WM_LBUTTONUP:
                    Click?.Invoke();
                    break;
                case WM_RBUTTONUP:
                    ShowMenu();
                    break;
            }
            handled = true;
        }
        else if (msg == _taskbarCreated)
        {
            Add();
        }
        return IntPtr.Zero;
    }

    private Window? _menuHost;
    private ContextMenu? _openMenu;

    /// <summary>
    /// A context menu only stays open while our process owns the foreground, and Windows won't give the
    /// foreground to a hidden window. So the menu is hosted on a 1×1 invisible window that we show and
    /// activate at the pointer; when it loses activation (a click elsewhere) the menu closes.
    /// </summary>
    private void ShowMenu()
    {
        if (MenuFactory?.Invoke() is not { } menu) return;
        _openMenu?.SetCurrentValue(ContextMenu.IsOpenProperty, false);

        var host = _menuHost ??= CreateMenuHost();
        Win32.GetCursorPos(out var p);
        host.Show();
        Monitors.Place(host, p.X, p.Y, 1, 1);
        host.Activate();
        Win32.SetForegroundWindow(new WindowInteropHelper(host).Handle);

        _openMenu = menu;
        menu.PlacementTarget = host;
        menu.Placement = PlacementMode.MousePoint;
        menu.Closed += (_, _) =>
        {
            if (_openMenu != menu) return;
            _openMenu = null;
            host.Hide();
        };
        menu.IsOpen = true;
    }

    private Window CreateMenuHost()
    {
        var host = new Window
        {
            Title = "Susharka menu",
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0)),
            ShowInTaskbar = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            Width = 1,
            Height = 1,
        };
        host.SourceInitialized += (_, _) =>
            Win32.AddExStyle(new WindowInteropHelper(host).Handle, Win32.WS_EX_TOOLWINDOW);
        host.Deactivated += (_, _) => _openMenu?.SetCurrentValue(ContextMenu.IsOpenProperty, false);
        return host;
    }

    public void Dispose()
    {
        if (_added) Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref _data);
        _added = false;
        if (_icon != IntPtr.Zero) Win32.DestroyIcon(_icon);
        _menuHost?.Close();
        _source.Dispose();
    }
}

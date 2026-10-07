using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;

namespace Susharka.Native;

/// <summary>A physical display: bounds in device pixels plus its DPI scale.</summary>
internal sealed record Monitor(IntPtr Handle, Win32.RECT Bounds, Win32.RECT WorkArea, double Scale, string Device)
{
    public double WidthDip => Bounds.Width / Scale;
    public double HeightDip => Bounds.Height / Scale;

    /// <summary>Screen pixel → DIP relative to this monitor's top-left.</summary>
    public Point ToLocalDip(int x, int y) => new((x - Bounds.Left) / Scale, (y - Bounds.Top) / Scale);

    public Rect ToLocalDip(Win32.RECT r) =>
        new((r.Left - Bounds.Left) / Scale, (r.Top - Bounds.Top) / Scale, r.Width / Scale, r.Height / Scale);
}

internal static class Monitors
{
    public static List<Monitor> All()
    {
        var list = new List<Monitor>();
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref Win32.RECT _, IntPtr _) =>
        {
            list.Add(FromHandle(h));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static Monitor FromHandle(IntPtr h)
    {
        var info = new Win32.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFOEX>() };
        Win32.GetMonitorInfo(h, ref info);
        double scale = 1.0;
        if (Win32.GetDpiForMonitor(h, 0, out var dx, out _) == 0 && dx > 0) scale = dx / 96.0;
        return new Monitor(h, info.rcMonitor, info.rcWork, scale, info.szDevice ?? "");
    }

    public static Monitor FromPoint(int x, int y) =>
        FromHandle(Win32.MonitorFromPoint(new Win32.POINT { X = x, Y = y }, Win32.MONITOR_DEFAULTTONEAREST));

    public static Monitor UnderCursor()
    {
        Win32.GetCursorPos(out var p);
        return FromPoint(p.X, p.Y);
    }

    /// <summary>
    /// Places a WPF window exactly over a pixel rectangle. Moving between monitors with different DPI
    /// makes WPF apply the system-suggested size, so the placement is re-applied after DPI changes.
    /// </summary>
    public static void Place(Window window, int x, int y, int w, int h)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        window.Tag = new Win32.RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, x, y, w, h, Win32.SWP_NOACTIVATE);
        if (window.GetValue(PlacementHooked) is not true)
        {
            window.SetValue(PlacementHooked, true);
            window.DpiChanged += (_, _) => window.Dispatcher.BeginInvoke(() =>
            {
                if (window.Tag is Win32.RECT r)
                    Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, r.Left, r.Top, r.Width, r.Height, Win32.SWP_NOACTIVATE);
            });
        }
    }

    private static readonly DependencyProperty PlacementHooked =
        DependencyProperty.RegisterAttached("PlacementHooked", typeof(bool), typeof(Monitors));

    public static void Place(Window window, Win32.RECT r) => Place(window, r.Left, r.Top, r.Width, r.Height);
}

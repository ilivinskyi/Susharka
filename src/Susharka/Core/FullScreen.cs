using System;
using System.Text;
using Susharka.Native;

namespace Susharka.Core;

/// <summary>Detects a full-screen app (video, game, slideshow) on a monitor, so the line stays out of the way.</summary>
internal static class FullScreen
{
    public static bool IsActive(Monitor monitor)
    {
        if (Win32.SHQueryUserNotificationState(out var state) == 0 &&
            state is Win32.QUNS_RUNNING_D3D_FULL_SCREEN or Win32.QUNS_PRESENTATION_MODE)
            return true;

        var fg = Win32.GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == Win32.GetShellWindow() || fg == Win32.GetDesktopWindow()) return false;
        if (!Win32.IsWindowVisible(fg)) return false;

        var cls = new StringBuilder(64);
        Win32.GetClassName(fg, cls, cls.Capacity);
        var name = cls.ToString();
        if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

        if (!Win32.GetWindowRect(fg, out var r)) return false;
        var m = monitor.Bounds;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }
}

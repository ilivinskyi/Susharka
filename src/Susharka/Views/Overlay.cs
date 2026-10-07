using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Susharka.Native;

namespace Susharka.Views;

/// <summary>
/// Transparent, click-through, topmost window covering one monitor. Hosts short-lived effects that
/// must escape the line's strip: a capture flying up to its peg, photos falling off.
/// </summary>
internal sealed class FxWindow : Window
{
    private static readonly Dictionary<string, FxWindow> ByDevice = new();
    private int _users;

    public Canvas Layer { get; } = new() { IsHitTestVisible = false };
    public Monitor Monitor { get; private set; }

    private FxWindow(Monitor monitor)
    {
        Monitor = monitor;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        Focusable = false;
        Title = "Susharka effects";
        Content = Layer;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Win32.AddExStyle(hwnd, Win32.WS_EX_TRANSPARENT | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_LAYERED);
        };
    }

    /// <summary>Borrows the effects window for a monitor; call <see cref="Release"/> when the effect ends.</summary>
    public static FxWindow Acquire(Monitor monitor)
    {
        if (!ByDevice.TryGetValue(monitor.Device, out var w))
        {
            w = new FxWindow(monitor);
            ByDevice[monitor.Device] = w;
        }
        w.Monitor = monitor;
        if (w._users++ == 0)
        {
            Monitors.Place(w, monitor.Bounds);
            w.Show();
            Monitors.Place(w, monitor.Bounds);
        }
        return w;
    }

    public void Release(UIElement? element = null)
    {
        if (element != null) Layer.Children.Remove(element);
        if (--_users > 0) return;
        _users = 0;
        Layer.Children.Clear();
        Hide();
    }

    public static void HideAll()
    {
        foreach (var w in ByDevice.Values)
        {
            w._users = 0;
            w.Layer.Children.Clear();
            w.Hide();
        }
    }
}

/// <summary>A small click-through thumbnail that follows the pointer during a drag.</summary>
internal sealed class DragGhost : Window
{
    private readonly int _offsetX, _offsetY;

    public DragGhost(ImageSource image, double width, double height, double angle)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Content = new Border
        {
            Margin = new Thickness(16),
            Child = new Image { Source = image, Width = width, Height = height },
            Opacity = 0.88,
            RenderTransform = new RotateTransform(angle, width / 2, height / 2),
        };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Win32.AddExStyle(hwnd, Win32.WS_EX_TRANSPARENT | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_LAYERED);
        };
        _offsetX = (int)(width / 2 + 16);
        _offsetY = (int)(height / 2 + 16);
    }

    public void Follow()
    {
        Win32.GetCursorPos(out var p);
        var scale = Monitors.FromPoint(p.X, p.Y).Scale;
        var hwnd = new WindowInteropHelper(this).Handle;
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, p.X - (int)(_offsetX * scale), p.Y - (int)(_offsetY * scale), 0, 0,
            Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }
}

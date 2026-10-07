using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Susharka.Native;

namespace Susharka.Capture;

/// <summary>
/// The crop tool: freezes every monitor, dims it, and lets the user drag a rectangle.
/// Enter or Space takes the whole monitor under the pointer; Esc or right-click cancels.
/// </summary>
internal sealed class CaptureSession
{
    public static bool IsActive { get; private set; }

    private readonly List<CaptureOverlay> _overlays = new();
    private readonly BitmapSource _desktop;
    private readonly Win32.RECT _desktopBounds;
    private readonly Action<BitmapSource, Win32.RECT> _done;
    private readonly Action? _cancelled;
    private bool _finished;

    private CaptureSession(Action<BitmapSource, Win32.RECT> done, Action? cancelled)
    {
        _done = done;
        _cancelled = cancelled;
        _desktop = ScreenCapture.CaptureVirtualScreen(out _desktopBounds);
    }

    /// <summary>Starts the crop tool. <paramref name="done"/> receives the image and its screen rect in pixels.</summary>
    public static void Start(Action<BitmapSource, Win32.RECT> done, Action? cancelled = null)
    {
        if (IsActive) return;
        IsActive = true;
        CaptureSession session;
        try
        {
            session = new CaptureSession(done, cancelled);
        }
        catch (Exception)
        {
            IsActive = false;
            cancelled?.Invoke();
            return;
        }
        session.Open();
    }

    private void Open()
    {
        var cursor = Monitors.UnderCursor();
        foreach (var m in Monitors.All())
        {
            var image = ScreenCapture.Crop(_desktop, _desktopBounds, m.Bounds);
            var overlay = new CaptureOverlay(this, m, image);
            _overlays.Add(overlay);
            overlay.Present();
        }
        var active = _overlays.FirstOrDefault(o => o.Monitor.Device == cursor.Device) ?? _overlays.FirstOrDefault();
        active?.TakeFocus();
    }

    internal void Finish(Win32.RECT region)
    {
        if (_finished) return;
        _finished = true;
        var image = ScreenCapture.Crop(_desktop, _desktopBounds, region);
        CloseAll();
        _done(image, region);
    }

    internal void Cancel()
    {
        if (_finished) return;
        _finished = true;
        CloseAll();
        _cancelled?.Invoke();
    }

    private void CloseAll()
    {
        foreach (var o in _overlays) o.Close();
        _overlays.Clear();
        IsActive = false;
    }
}

internal sealed class CaptureOverlay : Window
{
    private readonly CaptureSession _session;
    public Monitor Monitor { get; }

    private readonly Canvas _canvas = new();
    private readonly Path _dim;
    private readonly Rectangle _selection;
    private readonly Line _crossH, _crossV;
    private readonly Border _sizeLabel;
    private readonly TextBlock _sizeText;
    private readonly Border _hint;
    private Point? _start;
    private Rect _rect;

    public CaptureOverlay(CaptureSession session, Monitor monitor, BitmapSource image)
    {
        _session = session;
        Monitor = monitor;
        Title = "Susharka capture";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        UseLayoutRounding = true;

        var w = monitor.WidthDip;
        var h = monitor.HeightDip;
        Width = w;
        Height = h;

        var frozen = new Image { Source = image, Stretch = Stretch.Fill, Width = w, Height = h };
        RenderOptions.SetBitmapScalingMode(frozen, BitmapScalingMode.NearestNeighbor);
        _canvas.Children.Add(frozen);

        _dim = new Path { Fill = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), IsHitTestVisible = false };
        _canvas.Children.Add(_dim);

        _selection = new Rectangle
        {
            Stroke = Brushes.White,
            StrokeThickness = 1,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Opacity = 0.6 },
        };
        _canvas.Children.Add(_selection);

        var crossBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
        _crossH = new Line { Stroke = crossBrush, StrokeThickness = 1, IsHitTestVisible = false, X1 = 0, X2 = w };
        _crossV = new Line { Stroke = crossBrush, StrokeThickness = 1, IsHitTestVisible = false, Y1 = 0, Y2 = h };
        _canvas.Children.Add(_crossH);
        _canvas.Children.Add(_crossV);

        _sizeText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 11,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
        };
        _sizeLabel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xB3, 0x1E, 0x1F, 0x24)),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 2, 6, 3),
            Child = _sizeText,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        _canvas.Children.Add(_sizeLabel);

        _hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x1E, 0x1F, 0x24)),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16, 7, 16, 8),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "Drag to capture   ·   Enter: whole screen   ·   Esc: cancel",
                Foreground = Brushes.White,
                FontSize = 13,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            },
        };
        _hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_hint, (w - _hint.DesiredSize.Width) / 2);
        Canvas.SetTop(_hint, 28);
        _canvas.Children.Add(_hint);

        Content = _canvas;
        UpdateDim();

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        MouseRightButtonUp += (_, _) => _session.Cancel();
        KeyDown += OnKey;
        Deactivated += (_, _) => { /* other monitors' overlays may take focus; that's fine */ };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Win32.AddExStyle(hwnd, Win32.WS_EX_TOOLWINDOW);
            Monitors.Place(this, monitor.Bounds);
        };
    }

    public void Present()
    {
        ShowActivated = false;
        Show();
        Monitors.Place(this, Monitor.Bounds);
        Win32.GetCursorPos(out var p);
        if (Monitor.Bounds.Contains(p.X, p.Y)) MoveCrosshair(Monitor.ToLocalDip(p.X, p.Y));
        else HideCrosshair();
    }

    public void TakeFocus()
    {
        Activate();
        var hwnd = new WindowInteropHelper(this).Handle;
        Win32.SetForegroundWindow(hwnd);
        Focus();
        Keyboard.Focus(this);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _session.Cancel();
                break;
            case Key.Enter:
            case Key.Space:
                Win32.GetCursorPos(out var p);
                _session.Finish(Monitors.FromPoint(p.X, p.Y).Bounds);
                break;
        }
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(_canvas);
        _rect = new Rect(_start.Value, _start.Value);
        CaptureMouse();
        _hint.Visibility = Visibility.Collapsed;
        HideCrosshair();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(_canvas);
        if (_start is not { } s)
        {
            MoveCrosshair(p);
            return;
        }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            // Shift keeps it square.
            var side = Math.Max(Math.Abs(p.X - s.X), Math.Abs(p.Y - s.Y));
            p = new Point(s.X + Math.Sign(p.X - s.X) * side, s.Y + Math.Sign(p.Y - s.Y) * side);
        }
        _rect = new Rect(s, p);
        _selection.Visibility = Visibility.Visible;
        Canvas.SetLeft(_selection, _rect.X);
        Canvas.SetTop(_selection, _rect.Y);
        _selection.Width = _rect.Width;
        _selection.Height = _rect.Height;
        UpdateDim();

        var px = ToPixels(_rect);
        _sizeText.Text = $"{px.Width} × {px.Height}";
        _sizeLabel.Visibility = Visibility.Visible;
        _sizeLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var lx = Math.Min(p.X + 12, Width - _sizeLabel.DesiredSize.Width - 4);
        var ly = Math.Min(p.Y + 14, Height - _sizeLabel.DesiredSize.Height - 4);
        Canvas.SetLeft(_sizeLabel, lx);
        Canvas.SetTop(_sizeLabel, ly);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_start == null) return;
        ReleaseMouseCapture();
        _start = null;
        var px = ToPixels(_rect);
        if (px.Width < 4 || px.Height < 4)
        {
            // A click without a drag: start over.
            _selection.Visibility = Visibility.Collapsed;
            _sizeLabel.Visibility = Visibility.Collapsed;
            _rect = Rect.Empty;
            UpdateDim();
            MoveCrosshair(e.GetPosition(_canvas));
            return;
        }
        _session.Finish(px);
    }

    private Win32.RECT ToPixels(Rect r)
    {
        var s = Monitor.Scale;
        int l = Monitor.Bounds.Left + (int)Math.Round(r.Left * s);
        int t = Monitor.Bounds.Top + (int)Math.Round(r.Top * s);
        int rr = Monitor.Bounds.Left + (int)Math.Round(r.Right * s);
        int b = Monitor.Bounds.Top + (int)Math.Round(r.Bottom * s);
        return new Win32.RECT
        {
            Left = Math.Max(l, Monitor.Bounds.Left),
            Top = Math.Max(t, Monitor.Bounds.Top),
            Right = Math.Min(rr, Monitor.Bounds.Right),
            Bottom = Math.Min(b, Monitor.Bounds.Bottom),
        };
    }

    private void UpdateDim()
    {
        var full = new RectangleGeometry(new Rect(0, 0, Width, Height));
        if (_rect.IsEmpty || _rect.Width < 1 || _rect.Height < 1)
        {
            _dim.Data = full;
            return;
        }
        _dim.Data = new GeometryGroup { FillRule = FillRule.EvenOdd, Children = { full, new RectangleGeometry(_rect) } };
    }

    private void MoveCrosshair(Point p)
    {
        _crossH.Visibility = _crossV.Visibility = Visibility.Visible;
        _crossH.Y1 = _crossH.Y2 = Math.Round(p.Y) + 0.5;
        _crossV.X1 = _crossV.X2 = Math.Round(p.X) + 0.5;
    }

    private void HideCrosshair() => _crossH.Visibility = _crossV.Visibility = Visibility.Collapsed;
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Susharka.Anim;
using Susharka.Core;
using Susharka.Native;

namespace Susharka.Views;

/// <summary>What a photo on the line can ask the app to do.</summary>
internal interface ICardActions
{
    void Copy(Pegged item);
    void Open(Pegged item);
    void Markup(Pegged item);
    void LetGo(Pegged item);
    void Delete(Pegged item);
    void SaveAs(Pegged item);
    void ShowInExplorer(Pegged item);
    /// <summary>A drag ended; the file may have been moved or recycled.</summary>
    void DragEnded(Pegged item);
}

/// <summary>
/// The transparent strip along the top of a monitor holding the rope and the photos. Transparent pixels
/// are click-through (layered window), so only the photos themselves take the mouse.
/// </summary>
internal sealed class LinePanel : Window
{
    private readonly Canvas _stage = new();
    private readonly TranslateTransform _stageShift = new();
    private readonly Grid _rope = new() { IsHitTestVisible = false };
    private readonly Border _hint;
    private readonly Dictionary<Guid, PeggedCard> _cards = new();
    private readonly List<PeggedCard> _order = new();
    private readonly Spring _reveal;
    private Tween? _retractTween;
    private double _width = 1280;
    private int _busy, _menus;
    private IntPtr _hwnd;

    public ICardActions? Actions { get; set; }
    public Monitor? Monitor { get; private set; }
    public bool IsRevealed { get; private set; }
    /// <summary>A press, drag or menu is in progress, so the line must stay open.</summary>
    /// <remarks>
    /// A press or drag only counts while a mouse button is actually down, so a missed mouse-up can never
    /// hold the line open forever.
    /// </remarks>
    public bool IsBusy => _menus > 0 ||
        (_busy > 0 && (Win32.IsDown(Win32.VK_LBUTTON) || Win32.IsDown(Win32.VK_RBUTTON)));
    public string HintText { set => ((TextBlock)_hint.Child).Text = value; }

    private static double HiddenOffset => -(LineLayout.PanelHeight + 12);

    public LinePanel()
    {
        Title = "Susharka";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        UseLayoutRounding = false;

        _stage.RenderTransform = _stageShift;
        _stage.Children.Add(_rope);

        _hint = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 5, 12, 6),
            Background = new SolidColorBrush(Color.FromArgb(0xE0, 0xF7, 0xF8, 0xFA)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0, 0, 0)),
            BorderThickness = new Thickness(0.5),
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { Opacity = 0.15, BlurRadius = 10, ShadowDepth = 3, Direction = 270 },
            Child = new TextBlock
            {
                FontSize = 12,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x3A, 0x3D, 0x44)),
            },
        };
        _stage.Children.Add(_hint);

        Content = _stage;
        _reveal = Spring.Response(HiddenOffset, v => _stageShift.Y = v, 0.42, 0.82);

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Win32.AddExStyle(_hwnd, Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE);
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        };
        SizeChanged += (_, _) =>
        {
            if (ActualWidth > 0 && Math.Abs(ActualWidth - _width) > 0.5)
            {
                _width = ActualWidth;
                Relayout(animated: false);
            }
        };
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_MOUSEACTIVATE)
        {
            // Clicking a photo must not steal focus from the app the user is working in.
            handled = true;
            return new IntPtr(Win32.MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    // ───────────────────────────── placement & reveal ─────────────────────────────

    public void PlaceOn(Monitor monitor)
    {
        var same = Monitor != null && Monitor.Device == monitor.Device && Monitor.Bounds.Equals(monitor.Bounds) &&
                   Math.Abs(Monitor.Scale - monitor.Scale) < 0.001;
        Monitor = monitor;
        _width = monitor.WidthDip;
        Width = _width;
        Height = LineLayout.PanelHeight;
        Monitors.Place(this, monitor.Bounds.Left, monitor.Bounds.Top, monitor.Bounds.Width,
            (int)Math.Ceiling(LineLayout.PanelHeight * monitor.Scale));
        if (!same) Relayout(animated: false);
    }

    public void Reveal()
    {
        if (Monitor == null) PlaceOn(Monitors.UnderCursor());
        _retractTween?.Cancel();
        _retractTween = null;
        if (!IsVisible)
        {
            _reveal.Snap(HiddenOffset);
            Show();
            if (Monitor != null) PlaceOn(Monitor);
        }
        IsRevealed = true;
        _reveal.SetResponse(0.42, 0.82);
        _reveal.AnimateTo(0);
    }

    public void Retract(bool instantly = false)
    {
        IsRevealed = false;
        _retractTween?.Cancel();
        if (instantly || !IsVisible)
        {
            _reveal.Snap(HiddenOffset);
            Hide();
            return;
        }
        var from = _stageShift.Y;
        _reveal.Snap(from);
        _retractTween = Tween.Run(0.22, Ease.InQuad, t => _reveal.Snap(Ease.Lerp(from, HiddenOffset, t)), () =>
        {
            _retractTween = null;
            if (!IsRevealed) Hide();
        });
    }

    /// <summary>
    /// Whether a point (monitor-local DIP) keeps the line open: anywhere along the rope's strip, or
    /// close around a photo. Deliberately not the full width × photo depth, so working in browser tabs
    /// or toolbars beside the photos lets the line go.
    /// </summary>
    public bool IsInZone(Point p)
    {
        var ropeStrip = LineLayout.RopeTop + LineLayout.Sag(_width) + 28 + _stageShift.Y;
        if (p.Y <= Math.Max(4, ropeStrip)) return true;
        foreach (var c in _order)
        {
            var r = FrameRectOnMonitor(c);
            r.Inflate(28, 0);
            if (p.X >= r.Left && p.X <= r.Right && p.Y <= r.Bottom + 34) return true;
        }
        return false;
    }

    /// <summary>Whether a point (monitor-local DIP) is on a photo, its peg or its × button.</summary>
    public bool IsOverCard(Point p)
    {
        foreach (var c in _order)
        {
            var r = FrameRectOnMonitor(c);
            r.Inflate(12, 12);
            r.Y -= LineLayout.PinAbove;
            r.Height += LineLayout.PinAbove;
            if (r.Contains(p)) return true;
        }
        return false;
    }

    // ───────────────────────────── rope ─────────────────────────────

    private void BuildRope()
    {
        _rope.Children.Clear();
        var w = _width;
        var top = LineLayout.RopeTop;
        var sag = LineLayout.Sag(w);
        var geo = new PathGeometry(new[]
        {
            new PathFigure(new Point(-LineLayout.Overhang, top),
                new[] { new QuadraticBezierSegment(new Point(w / 2, top + 2 * sag), new Point(w + LineLayout.Overhang, top), true) },
                false),
        });
        geo.Freeze();

        _rope.Children.Add(new Path
        {
            Data = geo, Stroke = new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0)), StrokeThickness = 2.2,
            RenderTransform = new TranslateTransform(0, 0.9), Effect = new BlurEffect { Radius = 1.6 },
        });
        _rope.Children.Add(new Path
        {
            Data = geo, Stroke = new SolidColorBrush(Color.FromArgb(0xE6, 0xC9, 0xCD, 0xD4)), StrokeThickness = 1.6,
        });
        _rope.Children.Add(new Path
        {
            Data = geo, Stroke = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)), StrokeThickness = 0.6,
            RenderTransform = new TranslateTransform(0, -0.25),
        });

        _rope.Width = w;
        _rope.Height = top + 2 * sag + 6;
        _rope.OpacityMask = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops =
            {
                new GradientStop(Colors.Transparent, 0),
                new GradientStop(Colors.Black, 0.08),
                new GradientStop(Colors.Black, 0.92),
                new GradientStop(Colors.Transparent, 1),
            },
        };
    }

    private double RopeY(double x) => LineLayout.RopeY(x, _width);

    // ───────────────────────────── cards ─────────────────────────────

    public IEnumerable<PeggedCard> Cards => _order;

    public PeggedCard? CardFor(Pegged item) => _cards.GetValueOrDefault(item.Id);

    /// <summary>Hangs a card for an item. Arrival is a drop-in swing unless a flight will deliver it.</summary>
    public PeggedCard AddCard(Pegged item, int index, bool animated, bool awaitFlight)
    {
        var count = _order.Count + 1;
        var x = LineLayout.SlotX(Math.Min(index, count - 1), count, _width);
        var card = new PeggedCard(item, RopeY, x);
        card.CloseClicked += c => Actions?.LetGo(c.Item);
        CardInteraction.Attach(card, this);
        _cards[item.Id] = card;
        _order.Insert(Math.Min(index, _order.Count), card);
        _stage.Children.Add(card);

        if (awaitFlight)
        {
            card.Opacity = 0;
        }
        else if (animated)
        {
            card.DropY.Snap(-LineLayout.PanelHeight * 0.6);
            card.DropY.AnimateTo(0);
            card.Angle.Kick(item.Tilt >= 0 ? 70 : -70);
        }
        Relayout(animated, except: card);
        UpdateHint();
        return card;
    }

    /// <summary>Takes a card off the line, letting it fall when it was let go or pushed off the end.</summary>
    public void RemoveCard(Pegged item, RemovalReason reason, bool animated)
    {
        if (!_cards.Remove(item.Id, out var card)) return;
        var idx = _order.IndexOf(card);
        _order.Remove(card);

        if (animated && IsVisible && IsRevealed && Monitor != null)
        {
            if (reason == RemovalReason.Gone) FadeAway(card);
            else Fall(card, Monitor);
        }
        else
        {
            _stage.Children.Remove(card);
        }
        Relayout(animated);
        UpdateHint();
    }

    public void UpdateCard(Pegged item)
    {
        if (!_cards.TryGetValue(item.Id, out var card)) return;
        card.Reload();
        Relayout(animated: true);
    }

    public void Clear()
    {
        foreach (var c in _order) _stage.Children.Remove(c);
        _order.Clear();
        _cards.Clear();
        UpdateHint();
    }

    private void Relayout(bool animated, PeggedCard? except = null)
    {
        BuildRope();
        var n = _order.Count;
        for (int i = 0; i < n; i++)
        {
            var c = _order[i];
            var x = LineLayout.SlotX(i, n, _width);
            if (!animated || c == except)
            {
                c.X.Snap(x);
                c.Refresh();
                continue;
            }
            var dx = x - c.X.Value;
            if (Math.Abs(dx) > 0.5)
            {
                c.X.AnimateTo(x);
                // A photo that slides along the line swings back from the pull.
                c.Nudge(Math.Sign(dx) * Math.Min(Math.Abs(dx) / LineLayout.Spacing, 1) * 38);
            }
        }
        UpdateHint();
    }

    private void UpdateHint()
    {
        var show = _order.Count == 0;
        _hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_hint, (_width - _hint.DesiredSize.Width) / 2);
        Canvas.SetTop(_hint, LineLayout.RopeTop + LineLayout.Sag(_width) * 2 + 18);
        var from = _hint.Opacity;
        var to = show ? 1.0 : 0.0;
        if (Math.Abs(from - to) > 0.01) Tween.Run(0.3, Ease.InOutCubic, t => _hint.Opacity = Ease.Lerp(from, to, t));
    }

    // ───────────────────────────── effects ─────────────────────────────

    /// <summary>Converts a card's frame rect to monitor-local DIP (the panel sits at the monitor's top-left).</summary>
    public Rect FrameRectOnMonitor(PeggedCard card) =>
        new(card.CurrentX - card.FrameW / 2, card.AnchorY + LineLayout.FrameTop + _stageShift.Y, card.FrameW, card.FrameH);

    /// <summary>
    /// A fresh capture shrinks from where it was taken into its frame on the line, along a gentle arc,
    /// turning to the card's tilt as the glass frame assembles around it.
    /// </summary>
    public void Fly(PeggedCard card, Rect source, BitmapSource image, Action? landed = null)
    {
        if (Monitor == null)
        {
            card.Opacity = 1;
            landed?.Invoke();
            return;
        }
        var fx = FxWindow.Acquire(Monitor);
        var photo = new Image { Source = image, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(photo, BitmapScalingMode.HighQuality);
        var photoClip = new Border { Child = photo, ClipToBounds = true };
        var frame = new Border
        {
            Child = photoClip,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0, 0, 0)),
            BorderThickness = new Thickness(0.5),
            Effect = new DropShadowEffect { Opacity = 0.22, BlurRadius = 20, ShadowDepth = 6, Direction = 270 },
        };
        var glass = new LinearGradientBrush(Color.FromArgb(0xEE, 0xFB, 0xFC, 0xFE), Color.FromArgb(0xD6, 0xEC, 0xEF, 0xF4), 90);
        var rotate = new RotateTransform();
        frame.RenderTransform = rotate;
        fx.Layer.Children.Add(frame);

        Tween.Run(0.65, Ease.InOutCubic, t =>
        {
            var target = FrameRectOnMonitor(card);
            var w = Ease.Lerp(source.Width, target.Width, t);
            var h = Ease.Lerp(source.Height, target.Height, t);
            var cx = Ease.Lerp(source.X + source.Width / 2, target.X + target.Width / 2, t);
            var cy = Ease.Lerp(source.Y + source.Height / 2, target.Y + target.Height / 2, t) - 30 * Math.Sin(Math.PI * t);
            var chrome = Ease.SmoothStep(0.35, 1, t);
            var radius = LineLayout.FrameRadius * t;
            var inset = LineLayout.FrameInset * chrome;

            frame.Width = w;
            frame.Height = h;
            Canvas.SetLeft(frame, cx - w / 2);
            Canvas.SetTop(frame, cy - h / 2);
            frame.CornerRadius = new CornerRadius(radius);
            frame.Padding = new Thickness(inset);
            frame.Background = chrome > 0.01 ? glass : null;
            frame.Opacity = 1;
            var pw = Math.Max(1, w - 2 * inset);
            var ph = Math.Max(1, h - 2 * inset);
            photoClip.Clip = new RectangleGeometry(new Rect(0, 0, pw, ph), Math.Max(0, radius - inset), Math.Max(0, radius - inset));
            rotate.CenterX = w / 2;
            rotate.CenterY = 0;
            rotate.Angle = card.Item.Tilt * t;
        }, () =>
        {
            card.Opacity = 1;
            card.Angle.Kick(card.Item.Tilt >= 0 ? 45 : -45);
            Tween.Run(0.16, Ease.OutQuad, t => frame.Opacity = 1 - t, () => fx.Release(frame));
            landed?.Invoke();
        });
    }

    /// <summary>The photo slips off its peg and drops out of sight, turning and fading.</summary>
    private void Fall(PeggedCard card, Monitor monitor)
    {
        const double pad = 14;
        var area = new Rect(-pad, -pad, card.FrameW + 2 * pad, card.FrameH + 2 * pad + 6);
        BitmapSource snap;
        try { snap = ImageIO.Snapshot(card.Frame, area, monitor.Scale); }
        catch (Exception) { _stage.Children.Remove(card); return; }
        _stage.Children.Remove(card);

        var fx = FxWindow.Acquire(monitor);
        var img = new Image { Source = snap, Width = area.Width, Height = area.Height };
        var originX = card.CurrentX - card.FrameW / 2 - pad;
        var originY = card.AnchorY + LineLayout.FrameTop - pad + _stageShift.Y;
        var rot = new RotateTransform(card.CurrentAngle, card.FrameW / 2 + pad, pad - LineLayout.FrameTop);
        var move = new TranslateTransform();
        img.RenderTransform = new TransformGroup { Children = { rot, move } };
        Canvas.SetLeft(img, originX);
        Canvas.SetTop(img, originY);
        fx.Layer.Children.Add(img);

        var startAngle = card.CurrentAngle;
        var spin = card.Item.Tilt >= 0 ? 14 : -14;
        Tween.Run(0.55, Ease.InCubic, t =>
        {
            move.Y = 520 * t;
            rot.Angle = startAngle + spin * t;
            img.Opacity = 1 - t * t;
        }, () => fx.Release(img));
    }

    private void FadeAway(PeggedCard card)
    {
        Tween.Run(0.25, Ease.OutQuad, t => card.Opacity = 1 - t, () => _stage.Children.Remove(card));
    }

    // ───────────────────────────── interaction plumbing ─────────────────────────────

    public void BeginBusy() => _busy++;
    public void EndBusy() => _busy = Math.Max(0, _busy - 1);
    public void MenuOpened() => _menus++;
    public void MenuClosed() => _menus = Math.Max(0, _menus - 1);

    public void BringToFront()
    {
        if (_hwnd != IntPtr.Zero) Win32.SetForegroundWindow(_hwnd);
    }

    public double Scale => Monitor?.Scale ?? 1;

    public int IndexOf(PeggedCard card) => _order.IndexOf(card);
    public PeggedCard? CardAt(int i) => i >= 0 && i < _order.Count ? _order[i] : null;
    public bool HasCards => _order.Any();
}

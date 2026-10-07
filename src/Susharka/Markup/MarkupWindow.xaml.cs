using System;
using System.Collections.Generic;
using IOException = System.IO.IOException;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Susharka.Core;

namespace Susharka.Markup;

/// <summary>
/// A small annotation editor opened by press-and-hold: pen, highlighter, arrow, rectangle, ellipse,
/// text and crop, with undo. Done writes the result back over the screenshot.
/// </summary>
public partial class MarkupWindow : Window
{
    private enum Tool { Pen, Highlighter, Arrow, Rect, Ellipse, Text, Crop }

    private static readonly Color[] Palette =
    {
        Color.FromRgb(0xE5, 0x39, 0x35), // red
        Color.FromRgb(0xFB, 0x8C, 0x00), // orange
        Color.FromRgb(0xFD, 0xD8, 0x35), // yellow
        Color.FromRgb(0x43, 0xA0, 0x47), // green
        Color.FromRgb(0x1E, 0x88, 0xE5), // blue
        Color.FromRgb(0x1C, 0x1D, 0x21), // black
        Color.FromRgb(0xFF, 0xFF, 0xFF), // white
    };

    private readonly string _path;
    private readonly int _pixelW, _pixelH;
    private Rect _crop;
    private Tool _tool = Tool.Pen;
    private Color _color = Palette[0];
    private double _size = 1; // multiplier: thin 0.5, medium 1, thick 2

    private readonly Stack<UndoStep> _undo = new();
    private readonly Stack<UndoStep> _redo = new();
    private bool _suppressInkUndo;

    private Point? _dragStart;
    private Shape? _liveShape;
    private Rectangle? _cropRect;
    private bool _dirty;

    public event Action? Saved;

    public MarkupWindow(string path)
    {
        InitializeComponent();
        _path = path;
        Title = "Mark up — " + System.IO.Path.GetFileName(path);

        var image = ImageIO.Load(path) ?? throw new IOException("Can't read " + path);
        _pixelW = image.PixelWidth;
        _pixelH = image.PixelHeight;
        Photo.Source = image;
        foreach (var fe in new FrameworkElement[] { Photo, Ink, Shapes, CropLayer, Surface })
        {
            fe.Width = _pixelW;
            fe.Height = _pixelH;
        }
        RenderOptions.SetBitmapScalingMode(Photo, BitmapScalingMode.HighQuality);
        _crop = new Rect(0, 0, _pixelW, _pixelH);
        ApplyCrop();

        FitWindowToImage();
        BuildSwatches();
        WireTools();

        Ink.StrokeCollected += (_, e) =>
        {
            if (_suppressInkUndo) return;
            var stroke = e.Stroke;
            Push(new UndoStep(() => Ink.Strokes.Remove(stroke), () => Ink.Strokes.Add(stroke)));
        };

        Surface.PreviewMouseLeftButtonDown += OnSurfaceDown;
        Surface.PreviewMouseMove += OnSurfaceMove;
        Surface.PreviewMouseLeftButtonUp += OnSurfaceUp;
        SizeChanged += (_, _) => UpdateInkAttributes();
        Loaded += (_, _) => UpdateInkAttributes();
        PreviewKeyDown += OnKey;
        Closing += (_, e) =>
        {
            if (!_dirty || _closingConfirmed) return;
            var r = MessageBox.Show(this, "Save your markup before closing?", "Mark up",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) e.Cancel = true;
            else if (r == MessageBoxResult.Yes && !Save()) e.Cancel = true;
        };
        UpdateUndoButtons();
    }

    private bool _closingConfirmed;

    private void FitWindowToImage()
    {
        var work = SystemParameters.WorkArea;
        const double chromeW = 36 + 16, chromeH = 36 + 47 + 39;
        var maxW = work.Width * 0.85 - chromeW;
        var maxH = work.Height * 0.85 - chromeH;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        // Show the screenshot at its physical size when it fits, so the editor feels 1:1.
        var w = _pixelW / dpi;
        var h = _pixelH / dpi;
        var s = Math.Min(1, Math.Min(maxW / w, maxH / h));
        Width = Math.Max(MinWidth, w * s + chromeW);
        Height = Math.Max(MinHeight, h * s + chromeH);
    }

    private void BuildSwatches()
    {
        foreach (var c in Palette)
        {
            var rb = new RadioButton
            {
                Style = (Style)FindResource("Swatch"),
                Background = new SolidColorBrush(c),
                GroupName = "color",
                IsChecked = c == _color,
                ToolTip = "Colour",
            };
            rb.Checked += (_, _) =>
            {
                _color = c;
                UpdateInkAttributes();
            };
            Swatches.Children.Add(rb);
        }
    }

    private void WireTools()
    {
        void On(ToggleButton b, Tool t) => b.Checked += (_, _) => SelectTool(t);
        On(PenTool, Tool.Pen);
        On(HighlighterTool, Tool.Highlighter);
        On(ArrowTool, Tool.Arrow);
        On(RectTool, Tool.Rect);
        On(EllipseTool, Tool.Ellipse);
        On(TextTool, Tool.Text);
        On(CropTool, Tool.Crop);
        SizeS.Checked += (_, _) => { _size = 0.5; UpdateInkAttributes(); };
        SizeM.Checked += (_, _) => { _size = 1; UpdateInkAttributes(); };
        SizeL.Checked += (_, _) => { _size = 2; UpdateInkAttributes(); };
        SelectTool(Tool.Pen);
    }

    private void SelectTool(Tool tool)
    {
        CommitText();
        _tool = tool;
        var inking = tool is Tool.Pen or Tool.Highlighter;
        Ink.EditingMode = inking ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None;
        Ink.IsHitTestVisible = inking;
        Surface.Cursor = tool switch
        {
            Tool.Text => Cursors.IBeam,
            Tool.Pen or Tool.Highlighter => null,
            _ => Cursors.Cross,
        };
        UpdateInkAttributes();
    }

    /// <summary>Converts on-screen sizes to image pixels, so a "medium" pen looks the same at any zoom.</summary>
    private double ViewScale => View.ActualWidth > 0 && _crop.Width > 0 ? View.ActualWidth / _crop.Width : 1;
    private double ScreenToImage(double dip) => dip / Math.Max(0.01, ViewScale);

    private void UpdateInkAttributes()
    {
        if (Ink == null) return;
        var highlighter = _tool == Tool.Highlighter;
        Ink.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = highlighter ? Color.FromArgb(0xFF, _color.R, _color.G, _color.B) : _color,
            Width = ScreenToImage((highlighter ? 14 : 3) * _size),
            Height = ScreenToImage((highlighter ? 14 : 3) * _size),
            IsHighlighter = highlighter,
            StylusTip = highlighter ? StylusTip.Rectangle : StylusTip.Ellipse,
            FitToCurve = true,
            IgnorePressure = false,
        };
    }

    // ───────────────────────────── shapes, text, crop ─────────────────────────────

    private void OnSurfaceDown(object sender, MouseButtonEventArgs e)
    {
        if (_tool is Tool.Pen or Tool.Highlighter) return;
        var p = e.GetPosition(Surface);
        if (_tool == Tool.Text)
        {
            if (e.OriginalSource is TextBox) return;
            CommitText();
            PlaceText(p);
            e.Handled = true;
            return;
        }
        CommitText();
        _dragStart = p;
        Surface.CaptureMouse();
        e.Handled = true;

        var stroke = new SolidColorBrush(_color);
        var thickness = ScreenToImage(3 * _size);
        _liveShape = _tool switch
        {
            Tool.Arrow => new Path { Stroke = stroke, Fill = stroke, StrokeThickness = thickness, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round },
            Tool.Rect => new Rectangle { Stroke = stroke, StrokeThickness = thickness, RadiusX = ScreenToImage(3), RadiusY = ScreenToImage(3) },
            Tool.Ellipse => new Ellipse { Stroke = stroke, StrokeThickness = thickness },
            _ => null,
        };
        if (_tool == Tool.Crop)
        {
            _cropRect = new Rectangle
            {
                Stroke = Brushes.White,
                StrokeThickness = ScreenToImage(1.5),
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Fill = new SolidColorBrush(Color.FromArgb(0x22, 0x2F, 0x6F, 0xEB)),
            };
            CropLayer.Children.Add(_cropRect);
        }
        if (_liveShape != null) Shapes.Children.Add(_liveShape);
    }

    private void OnSurfaceMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } s) return;
        var p = e.GetPosition(Surface);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _tool is Tool.Rect or Tool.Ellipse or Tool.Crop)
        {
            var side = Math.Max(Math.Abs(p.X - s.X), Math.Abs(p.Y - s.Y));
            p = new Point(s.X + Math.Sign(p.X - s.X) * side, s.Y + Math.Sign(p.Y - s.Y) * side);
        }
        var r = new Rect(s, p);
        switch (_liveShape)
        {
            case Path arrow:
                arrow.Data = ArrowGeometry(s, p, arrow.StrokeThickness);
                break;
            case Shape shape:
                Canvas.SetLeft(shape, r.X);
                Canvas.SetTop(shape, r.Y);
                shape.Width = r.Width;
                shape.Height = r.Height;
                break;
        }
        if (_cropRect != null)
        {
            r.Intersect(new Rect(0, 0, _pixelW, _pixelH));
            if (r.IsEmpty) return;
            Canvas.SetLeft(_cropRect, r.X);
            Canvas.SetTop(_cropRect, r.Y);
            _cropRect.Width = r.Width;
            _cropRect.Height = r.Height;
        }
    }

    private void OnSurfaceUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is not { } s) return;
        Surface.ReleaseMouseCapture();
        _dragStart = null;
        var p = e.GetPosition(Surface);
        var tiny = Math.Abs(p.X - s.X) < ScreenToImage(3) && Math.Abs(p.Y - s.Y) < ScreenToImage(3);

        if (_cropRect != null)
        {
            var r = new Rect(Canvas.GetLeft(_cropRect), Canvas.GetTop(_cropRect), _cropRect.Width, _cropRect.Height);
            CropLayer.Children.Remove(_cropRect);
            _cropRect = null;
            if (tiny || double.IsNaN(r.X) || r.Width < 4 || r.Height < 4) return;
            r = new Rect(Math.Round(r.X), Math.Round(r.Y), Math.Round(r.Width), Math.Round(r.Height));
            var before = _crop;
            _crop = r;
            ApplyCrop();
            Push(new UndoStep(() => { _crop = before; ApplyCrop(); }, () => { _crop = r; ApplyCrop(); }));
            PenTool.IsChecked = true;
            return;
        }

        if (_liveShape is { } shape)
        {
            _liveShape = null;
            if (tiny)
            {
                Shapes.Children.Remove(shape);
                return;
            }
            Push(new UndoStep(() => Shapes.Children.Remove(shape), () => Shapes.Children.Add(shape)));
        }
    }

    private static Geometry ArrowGeometry(Point from, Point to, double thickness)
    {
        var v = to - from;
        var len = v.Length;
        var g = new StreamGeometry();
        if (len < 1) return g;
        v /= len;
        var head = Math.Min(len * 0.45, thickness * 4.5 + 6);
        var normal = new Vector(-v.Y, v.X);
        var baseP = to - v * head;
        var left = baseP + normal * head * 0.55;
        var right = baseP - normal * head * 0.55;
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(from, false, false);
            ctx.LineTo(to - v * head * 0.6, true, true);
            ctx.BeginFigure(to, true, true);
            ctx.LineTo(left, true, true);
            ctx.LineTo(right, true, true);
        }
        g.Freeze();
        return g;
    }

    private TextBox? _editing;

    private void PlaceText(Point p)
    {
        var box = new TextBox
        {
            FontSize = ScreenToImage(18 * (_size == 2 ? 1.6 : _size == 0.5 ? 0.75 : 1)),
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
            Foreground = new SolidColorBrush(_color),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(ScreenToImage(1)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x2F, 0x6F, 0xEB)),
            CaretBrush = new SolidColorBrush(_color),
            MinWidth = ScreenToImage(40),
            AcceptsReturn = true,
            Padding = new Thickness(0),
        };
        Canvas.SetLeft(box, p.X);
        Canvas.SetTop(box, p.Y - box.FontSize * 0.7);
        Shapes.IsHitTestVisible = true;
        Shapes.Children.Add(box);
        _editing = box;
        box.LostKeyboardFocus += (_, _) => CommitText();
        Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            Keyboard.Focus(box);
        });
    }

    /// <summary>Turns the text being typed into a fixed label (or drops it if empty).</summary>
    private void CommitText()
    {
        var box = _editing;
        if (box == null) return;
        _editing = null;
        Shapes.IsHitTestVisible = false;
        Shapes.Children.Remove(box);
        if (string.IsNullOrWhiteSpace(box.Text)) return;

        var label = new TextBlock
        {
            Text = box.Text,
            FontSize = box.FontSize,
            FontWeight = box.FontWeight,
            FontFamily = box.FontFamily,
            Foreground = box.Foreground,
            Padding = new Thickness(box.BorderThickness.Left + 2, box.BorderThickness.Top + 1, 0, 0),
        };
        Canvas.SetLeft(label, Canvas.GetLeft(box));
        Canvas.SetTop(label, Canvas.GetTop(box));
        Shapes.Children.Add(label);
        Push(new UndoStep(() => Shapes.Children.Remove(label), () => Shapes.Children.Add(label)));
        Keyboard.Focus(this);
    }

    private void ApplyCrop()
    {
        CropHost.Width = _crop.Width;
        CropHost.Height = _crop.Height;
        Canvas.SetLeft(Surface, -_crop.X);
        Canvas.SetTop(Surface, -_crop.Y);
        Dispatcher.BeginInvoke(UpdateInkAttributes, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ───────────────────────────── undo ─────────────────────────────

    private sealed record UndoStep(Action Undo, Action Redo);

    private void Push(UndoStep step)
    {
        _undo.Push(step);
        _redo.Clear();
        _dirty = true;
        UpdateUndoButtons();
    }

    private void OnUndo(object? sender, RoutedEventArgs? e)
    {
        CommitText();
        if (_undo.Count == 0) return;
        var step = _undo.Pop();
        _suppressInkUndo = true;
        step.Undo();
        _suppressInkUndo = false;
        _redo.Push(step);
        _dirty = _undo.Count > 0;
        UpdateUndoButtons();
    }

    private void OnRedo(object? sender, RoutedEventArgs? e)
    {
        if (_redo.Count == 0) return;
        var step = _redo.Pop();
        _suppressInkUndo = true;
        step.Redo();
        _suppressInkUndo = false;
        _undo.Push(step);
        _dirty = true;
        UpdateUndoButtons();
    }

    private void UpdateUndoButtons()
    {
        UndoButton.IsEnabled = _undo.Count > 0;
        RedoButton.IsEnabled = _redo.Count > 0;
    }

    // ───────────────────────────── keys, save ─────────────────────────────

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_editing != null)
        {
            if (e.Key == Key.Escape) { _editing.Text = ""; CommitText(); e.Handled = true; }
            return;
        }
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl:
                OnRedo(null, null);
                break;
            case Key.Z when ctrl:
                OnUndo(null, null);
                break;
            case Key.S when ctrl:
            case Key.Enter:
                OnDone(null, null);
                break;
            case Key.Escape:
                if (_dragStart != null) return;
                OnCancel(null, null);
                break;
            case Key.P: PenTool.IsChecked = true; break;
            case Key.H: HighlighterTool.IsChecked = true; break;
            case Key.A: ArrowTool.IsChecked = true; break;
            case Key.R: RectTool.IsChecked = true; break;
            case Key.O: EllipseTool.IsChecked = true; break;
            case Key.T: TextTool.IsChecked = true; break;
            case Key.C when !ctrl: CropTool.IsChecked = true; break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnCancel(object? sender, RoutedEventArgs? e)
    {
        _closingConfirmed = !_dirty;
        Close();
    }

    private void OnDone(object? sender, RoutedEventArgs? e)
    {
        CommitText();
        if (!_dirty)
        {
            _closingConfirmed = true;
            Close();
            return;
        }
        if (!Save()) return;
        _closingConfirmed = true;
        Close();
    }

    private bool Save()
    {
        try
        {
            CommitText();
            UpdateLayout();
            var bitmap = ImageIO.Snapshot(CropHost, new Rect(0, 0, _crop.Width, _crop.Height), 1.0);
            ImageIO.Save(bitmap, _path);
            _dirty = false;
            Saved?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't save: " + ex.Message, "Mark up", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }
}

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Susharka.Anim;
using Susharka.Core;

namespace Susharka.Views;

/// <summary>
/// One photo on the line: a glass frame with the screenshot, a brushed-metal peg, a hover × and a
/// "Copied" badge. The element's origin is the point where the peg grips the rope; it rotates around it.
/// </summary>
internal sealed class PeggedCard : Canvas
{
    public Pegged Item { get; }
    public double FrameW { get; private set; }
    public double FrameH { get; private set; }
    public Border Frame { get; }
    public BitmapSource? Thumbnail => _photo.Source as BitmapSource;

    public event Action<PeggedCard>? CloseClicked;

    public readonly Spring X;
    public readonly Spring DropY;
    public readonly Spring Angle;

    private readonly Func<double, double> _ropeY;
    private readonly Image _photo = new() { Stretch = Stretch.Fill };
    private readonly Border _photoClip;
    private readonly Border _peg;
    private readonly Border _close;
    private readonly Border _badge;
    private readonly DropShadowEffect _shadow;
    private readonly ScaleTransform _scale = new();
    private readonly RotateTransform _rotate = new();
    private readonly TranslateTransform _move = new();
    private readonly Spring _hover;
    private double _hoverValue, _pressValue;
    private Tween? _pressTween;
    private DispatcherTimer? _badgeTimer;

    public PeggedCard(Pegged item, Func<double, double> ropeY, double startX)
    {
        Item = item;
        _ropeY = ropeY;
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;
        Cursor = Cursors.Hand;

        var group = new TransformGroup();
        group.Children.Add(_scale);
        group.Children.Add(_rotate);
        group.Children.Add(_move);
        RenderTransform = group;

        // Glass frame: soft white with a specular edge, concentric corners around the photo.
        _shadow = new DropShadowEffect
        {
            Color = Colors.Black, Direction = 270, ShadowDepth = 5, BlurRadius = 20, Opacity = 0.18,
            RenderingBias = RenderingBias.Performance,
        };
        _photoClip = new Border
        {
            CornerRadius = new CornerRadius(LineLayout.PhotoRadius),
            Child = _photo,
            ClipToBounds = true,
        };
        var photoEdge = new Border
        {
            CornerRadius = new CornerRadius(LineLayout.PhotoRadius),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0.5),
            IsHitTestVisible = false,
        };
        var photoGrid = new Grid();
        photoGrid.Children.Add(_photoClip);
        photoGrid.Children.Add(photoEdge);

        var specular = new Border
        {
            CornerRadius = new CornerRadius(LineLayout.FrameRadius - 0.5),
            BorderThickness = new Thickness(1),
            BorderBrush = new LinearGradientBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF), 90),
            Padding = new Thickness(LineLayout.FrameInset - 1),
            Child = photoGrid,
        };
        Frame = new Border
        {
            CornerRadius = new CornerRadius(LineLayout.FrameRadius),
            BorderThickness = new Thickness(0.5),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0, 0, 0)),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(0xEE, 0xFB, 0xFC, 0xFE), 0),
                    new(Color.FromArgb(0xD6, 0xEC, 0xEF, 0xF4), 1),
                }, 90),
            Child = specular,
            Effect = _shadow,
        };
        SetTop(Frame, LineLayout.FrameTop);
        Children.Add(Frame);

        _peg = BuildPeg();
        Children.Add(_peg);

        _close = BuildCloseButton();
        Children.Add(_close);

        _badge = BuildBadge();
        Children.Add(_badge);

        _hover = Spring.Response(0, v => { _hoverValue = v; ApplyHover(); }, 0.25, 0.9);

        X = Spring.Response(startX, _ => ApplyPosition(), 0.55, 0.78);
        DropY = Spring.Response(0, _ => ApplyPosition(), 0.42, 0.72);
        Angle = new Spring(item.Tilt, v => _rotate.Angle = v, 46, 2.6);

        MouseEnter += (_, _) => _hover.AnimateTo(1);
        MouseLeave += (_, _) => _hover.AnimateTo(0);

        Reload();
    }

    /// <summary>(Re)loads the image from disk and resizes the frame to its aspect ratio.</summary>
    public void Reload()
    {
        var (pw, ph) = ImageIO.PixelSize(Item.Path);
        var (w, h) = LineLayout.PhotoSize(pw, ph);
        FrameW = w + 2 * LineLayout.FrameInset;
        FrameH = h + 2 * LineLayout.FrameInset;

        _photo.Width = w;
        _photo.Height = h;
        _photoClip.Clip = new RectangleGeometry(new Rect(0, 0, w, h), LineLayout.PhotoRadius, LineLayout.PhotoRadius);
        // Decode at ~3× the displayed width so it stays crisp on high-DPI screens and when it grows on hover.
        _photo.Source = ImageIO.Load(Item.Path, (int)Math.Min(pw > 0 ? pw : 600, w * 3));
        RenderOptions.SetBitmapScalingMode(_photo, BitmapScalingMode.HighQuality);

        Frame.Width = FrameW;
        Frame.Height = FrameH;
        SetLeft(Frame, -FrameW / 2);
        SetLeft(_peg, -_peg.Width / 2);
        SetTop(_peg, -LineLayout.PinAbove);
        SetLeft(_close, -FrameW / 2 - 9);
        SetTop(_close, LineLayout.FrameTop - 9);
        _badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SetLeft(_badge, -_badge.DesiredSize.Width / 2);
        SetTop(_badge, LineLayout.FrameTop + FrameH + 10);

        _scale.CenterX = 0;
        _scale.CenterY = LineLayout.FrameTop + FrameH / 2;
    }

    /// <summary>Lowest point of the card below its anchor, including the badge area and shadow.</summary>
    public double Depth => LineLayout.FrameTop + FrameH + 16;

    public double CurrentX => X.Value;
    public double AnchorY => _ropeY(X.Value) + DropY.Value;
    public double CurrentAngle => _rotate.Angle;

    private void ApplyPosition()
    {
        if (X is null || DropY is null) return; // springs apply once while being constructed
        _move.X = X.Value;
        _move.Y = _ropeY(X.Value) + DropY.Value;
    }

    public void Refresh() => ApplyPosition();

    private void ApplyHover()
    {
        var s = (1 + 0.035 * _hoverValue) * (1 - 0.05 * _pressValue);
        _scale.ScaleX = _scale.ScaleY = s;
        _shadow.Opacity = 0.18 + 0.08 * _hoverValue;
        _shadow.BlurRadius = 20 + 8 * _hoverValue;
        _shadow.ShadowDepth = 5 + 3 * _hoverValue;
        var closeVisible = Math.Clamp(_hoverValue, 0, 1);
        _close.Opacity = closeVisible;
        if (_close.RenderTransform is ScaleTransform cs) cs.ScaleX = cs.ScaleY = 0.6 + 0.4 * closeVisible;
        _close.IsHitTestVisible = _hoverValue > 0.5;
    }

    /// <summary>Press feedback: shrinks slowly while held, hinting at the long press.</summary>
    public void SetPressed(bool pressed)
    {
        _pressTween?.Cancel();
        var from = _pressValue;
        var to = pressed ? 1.0 : 0.0;
        _pressTween = Tween.Run(pressed ? 0.45 : 0.18, Ease.OutQuad, t =>
        {
            _pressValue = Ease.Lerp(from, to, t);
            ApplyHover();
        });
    }

    public void SetDragging(bool dragging) => Opacity = dragging ? 0.45 : 1;

    /// <summary>A gentle sway, as when a neighbour moves or a breeze passes.</summary>
    public void Nudge(double angularVelocity) => Angle.Kick(angularVelocity);

    public void ShowCopied()
    {
        _badgeTimer?.Stop();
        Tween.Run(0.2, Ease.OutQuad, t =>
        {
            _badge.Opacity = Math.Max(_badge.Opacity, t);
            ((TranslateTransform)_badge.RenderTransform).Y = -6 * (1 - t);
        });
        _badgeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.3) };
        _badgeTimer.Tick += (_, _) =>
        {
            _badgeTimer!.Stop();
            var start = _badge.Opacity;
            Tween.Run(0.3, Ease.InQuad, t => _badge.Opacity = start * (1 - t));
        };
        _badgeTimer.Start();
    }

    private static Border BuildPeg()
    {
        // Brushed aluminium pill with a dark slot where the rope passes through.
        var metal = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0xC9, 0xCD, 0xD3), 0),
                new GradientStop(Color.FromRgb(0xF7, 0xF8, 0xFA), 0.35),
                new GradientStop(Color.FromRgb(0xAE, 0xB4, 0xBC), 0.72),
                new GradientStop(Color.FromRgb(0xDD, 0xE0, 0xE5), 1),
            },
        };
        var slot = new Border
        {
            Width = 5, Height = 1.4,
            CornerRadius = new CornerRadius(0.7),
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x2E, 0x33, 0x3B)),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8.8, 0, 0),
        };
        return new Border
        {
            Width = 9, Height = 26,
            CornerRadius = new CornerRadius(4.5),
            Background = metal,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)),
            BorderThickness = new Thickness(0.5),
            Child = slot,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.30, BlurRadius = 4, ShadowDepth = 1.5, Direction = 270 },
        };
    }

    private Border BuildCloseButton()
    {
        var cross = new Path
        {
            Data = Geometry.Parse("M0,0 L7,7 M7,0 L0,7"),
            Stroke = new SolidColorBrush(Color.FromRgb(0x3A, 0x3D, 0x44)),
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var circle = new Border
        {
            Width = 19, Height = 19,
            CornerRadius = new CornerRadius(9.5),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0xF7, 0xF8, 0xFA)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)),
            BorderThickness = new Thickness(0.5),
            Child = cross,
            Effect = new DropShadowEffect { Opacity = 0.22, BlurRadius = 6, ShadowDepth = 1.5, Direction = 270 },
        };
        // 26×26 hit target around the visible circle.
        var hit = new Border
        {
            Width = 26, Height = 26,
            Background = Brushes.Transparent,
            Child = circle,
            Opacity = 0,
            IsHitTestVisible = false,
            Cursor = Cursors.Arrow,
            ToolTip = "Let it go",
            RenderTransform = new ScaleTransform(0.6, 0.6, 13, 13),
        };
        hit.MouseLeftButtonDown += (_, e) => e.Handled = true;
        hit.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            CloseClicked?.Invoke(this);
        };
        return hit;
    }

    private static Border BuildBadge()
    {
        var check = new Path
        {
            Data = Geometry.Parse("M0,4 L3,7 L9,0"),
            Stroke = new SolidColorBrush(Color.FromRgb(0x2B, 0x2E, 0x34)),
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
        };
        var text = new TextBlock
        {
            Text = "Copied",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2B, 0x2E, 0x34)),
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { check, text } };
        return new Border
        {
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(10, 4, 10, 5),
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0xF7, 0xF8, 0xFA)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0, 0, 0)),
            BorderThickness = new Thickness(0.5),
            Child = row,
            Opacity = 0,
            IsHitTestVisible = false,
            RenderTransform = new TranslateTransform(),
            Effect = new DropShadowEffect { Opacity = 0.18, BlurRadius = 10, ShadowDepth = 3, Direction = 270 },
        };
    }
}

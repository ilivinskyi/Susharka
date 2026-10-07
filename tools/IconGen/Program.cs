using System;
using System.Collections.Generic;
using File = System.IO.File;
using MemoryStream = System.IO.MemoryStream;
using BinaryWriter = System.IO.BinaryWriter;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

// Draws the Susharka app icon and writes a multi-size .ico.
//   IconGen <design> <out.ico>        design: glass (the app icon) | line | peg
//   IconGen png <size> <out.png>       the icon as a single PNG
//   IconGen msix-assets <dir>         tile/store logos for the MSIX package
//   IconGen preview <out.png>         all designs side by side at real sizes, light and dark
internal static class Program
{
    private static readonly int[] IcoSizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length >= 3 && args[0] == "png")
        {
            Save(Render("glass", int.Parse(args[1])), args[2]);
            return;
        }
        if (args.Length >= 2 && args[0] == "msix-assets")
        {
            WriteMsixAssets(args[1]);
            return;
        }
        if (args.Length >= 2 && args[0] == "preview")
        {
            Save(Preview(), args[1]);
            Console.WriteLine($"Wrote {args[1]}");
            return;
        }
        var design = args.Length > 0 ? args[0] : "glass";
        var output = args.Length > 1 ? args[1] : "icon.ico";
        var pngs = new List<byte[]>();
        foreach (var s in IcoSizes) pngs.Add(Png(Render(design, s)));
        WriteIco(output, IcoSizes, pngs);
        Console.WriteLine($"Wrote {output} ({design})");
    }

    // ───────────────────────────── designs (drawn on a 256 grid) ─────────────────────────────

    private static Canvas Design(string name, int size) => name switch
    {
        "glass" => Glass(size),
        "peg" => Peg(size),
        _ => Line(size),
    };

    /// <summary>Two pegged photos on a line across a warm sunset tile.</summary>
    private static Canvas Line(int size)
    {
        var small = size <= 32;
        var c = NewCanvas();
        c.Children.Add(Tile(Gradient(45, "#FFB36B", "#F2607D", "#8B5CF6")));
        c.Children.Add(Sheen());
        c.Children.Add(Rope(small ? 64 : 62, small ? 100 : 92, small ? 12 : 6));

        if (small)
        {
            c.Children.Add(Photo(64, 88, 128, 116, -5, 9, 12, Gradient(90, "#7DD3FC", "#3B82F6"), Scenery.Hills));
            c.Children.Add(Clothespin(118, 70, -5, 2.2));
            return c;
        }
        c.Children.Add(Photo(40, 84, 100, 116, -9, 8, 14, Gradient(90, "#7DD3FC", "#2563EB"), Scenery.Hills));
        c.Children.Add(Photo(128, 80, 92, 106, 7, 8, 14, Gradient(90, "#FDE68A", "#FB923C"), Scenery.Sun));
        c.Children.Add(Clothespin(80, 70, -9, 1.4));
        c.Children.Add(Clothespin(166, 68, 7, 1.4));
        return c;
    }

    /// <summary>A frosted glass photo hanging from a metal clip on a short arc, Fluent style, no tile.</summary>
    private static Canvas Glass(int size)
    {
        var c = NewCanvas();
        if (size <= 24)
        {
            // Tray sizes: just the photo and its clip, as large as possible.
            c.Children.Add(Photo(16, 44, 224, 196, -5, 14, 30, Gradient(90, "#67E8F9", "#3B82F6", "#6366F1"), Scenery.Hills, glass: true));
            c.Children.Add(MetalClip(114, 14, -5, 2.6));
            return c;
        }
        var small = size <= 48;
        // Back card peeking out
        c.Children.Add(Photo(118, 54, 112, 96, 10, 7, 16, Gradient(90, "#C4B5FD", "#7C3AED"), Scenery.None, glass: true, opacity: 0.92));
        c.Children.Add(Rope(small ? 34 : 30, small ? 60 : 58, small ? 10 : 7, x0: 8, x1: 248, color: "#A3ACBD"));
        c.Children.Add(Photo(26, 66, 178, 156, -6, 10, 22, Gradient(90, "#67E8F9", "#3B82F6", "#6366F1"), Scenery.Hills, glass: true));
        c.Children.Add(MetalClip(108, 46, -6, small ? 1.6 : 1.3));
        return c;
    }

    /// <summary>A big wooden clothespin gripping the corner of a photo on a deep teal tile.</summary>
    private static Canvas Peg(int size)
    {
        var small = size <= 32;
        var c = NewCanvas();
        c.Children.Add(Tile(Gradient(60, "#14B8A6", "#0E7490", "#1E3A8A")));
        c.Children.Add(Sheen());
        c.Children.Add(Photo(small ? 46 : 52, small ? 74 : 80, small ? 164 : 150, small ? 140 : 128, 8, small ? 10 : 9, 16,
            Gradient(90, "#FDE68A", "#F97316", "#DB2777"), small ? Scenery.Sun : Scenery.SunHills));
        c.Children.Add(Clothespin(small ? 114 : 112, small ? 26 : 30, -18, small ? 3.2 : 2.6));
        return c;
    }

    // ───────────────────────────── parts ─────────────────────────────

    private enum Scenery { None, Hills, Sun, SunHills }

    private static Canvas NewCanvas() => new() { Width = 256, Height = 256, ClipToBounds = false };

    private static UIElement Tile(Brush fill) => At(new Border
    {
        Width = 232, Height = 232, CornerRadius = new CornerRadius(54), Background = fill,
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), BorderThickness = new Thickness(1),
        Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 3, Direction = 270, Opacity = 0.25 },
    }, 12, 14);

    private static UIElement Sheen() => At(new Border
    {
        Width = 232, Height = 232, CornerRadius = new CornerRadius(54),
        Background = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0x55, 255, 255, 255), 0),
            new(Color.FromArgb(0x00, 255, 255, 255), 0.5),
        }, new Point(0, 0), new Point(0, 1)),
    }, 12, 14);

    private static UIElement Rope(double yEnds, double yMid, double thickness, double x0 = 12, double x1 = 244, string color = "#FFFFFF")
    {
        var g = new PathGeometry(new[]
        {
            new PathFigure(new Point(x0, yEnds),
                new[] { new QuadraticBezierSegment(new Point(128, 2 * yMid - yEnds), new Point(x1, yEnds), true) }, false),
        });
        var grid = new Grid();
        grid.Children.Add(new Path
        {
            Data = g, Stroke = new SolidColorBrush(Color.FromArgb(0x45, 0, 0, 0)), StrokeThickness = thickness + 1.5,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            RenderTransform = new TranslateTransform(0, thickness * 0.35), Effect = new BlurEffect { Radius = thickness * 0.6 },
        });
        grid.Children.Add(new Path
        {
            Data = g, Stroke = Hex(color), StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        });
        grid.Children.Add(new Path
        {
            Data = g, Stroke = new SolidColorBrush(Color.FromArgb(0x8C, 255, 255, 255)), StrokeThickness = thickness * 0.3,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            RenderTransform = new TranslateTransform(0, -thickness * 0.22),
        });
        return grid;
    }

    private static UIElement Photo(double x, double y, double w, double h, double angle, double border, double radius,
        Brush picture, Scenery scenery, bool glass = false, double opacity = 1)
    {
        var inner = new Grid { ClipToBounds = true };
        inner.Children.Add(new Rectangle { Fill = picture });
        var iw = w - 2 * border;
        var ih = h - 2 * border;
        if (scenery is Scenery.Sun or Scenery.SunHills)
            inner.Children.Add(At(new Ellipse { Width = iw * 0.26, Height = iw * 0.26, Fill = Hex("#FFF7D6") }, iw * 0.16, ih * 0.14));
        if (scenery is Scenery.Hills or Scenery.SunHills)
        {
            var hills = Geometry.Parse(
                $"M0,{ih * 0.72} Q{iw * 0.22},{ih * 0.42} {iw * 0.46},{ih * 0.64} Q{iw * 0.7},{ih * 0.38} {iw},{ih * 0.6} L{iw},{ih} L0,{ih} Z");
            inner.Children.Add(new Path { Data = hills, Fill = new SolidColorBrush(Color.FromArgb(0xD9, 0x1E, 0x1B, 0x4B)) });
            var front = Geometry.Parse($"M0,{ih * 0.86} Q{iw * 0.4},{ih * 0.66} {iw},{ih * 0.84} L{iw},{ih} L0,{ih} Z");
            inner.Children.Add(new Path { Data = front, Fill = new SolidColorBrush(Color.FromArgb(0xF0, 0x15, 0x13, 0x36)) });
        }
        var innerRadius = Math.Max(2, radius - border * 0.75);
        inner.Clip = new RectangleGeometry(new Rect(0, 0, iw, ih), innerRadius, innerRadius);

        var frame = new Border
        {
            Width = w, Height = h,
            CornerRadius = new CornerRadius(radius),
            Padding = new Thickness(border),
            Background = glass
                ? new LinearGradientBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF), Color.FromArgb(0xD9, 0xE6, 0xEA, 0xF2), 90)
                : Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Child = inner,
            Opacity = opacity,
            RenderTransform = new RotateTransform(angle, w / 2, 0),
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 6, Direction = 270, Opacity = 0.32 },
        };
        return At(frame, x, y);
    }

    /// <summary>Wooden clothespin: two jaws with a steel spring.</summary>
    private static UIElement Clothespin(double x, double y, double angle, double scale)
    {
        var wood = new LinearGradientBrush(new GradientStopCollection
        {
            new(Hex("#F6D7A7").Color, 0), new(Hex("#E3B26B").Color, 0.55), new(Hex("#C98C42").Color, 1),
        }, new Point(0, 0), new Point(1, 0));
        var c = new Canvas { Width = 14, Height = 40 };
        c.Children.Add(At(new Border { Width = 6.4, Height = 40, CornerRadius = new CornerRadius(3, 3, 2.5, 2.5), Background = wood,
            BorderBrush = Hex("#9A6428"), BorderThickness = new Thickness(0.5) }, 0, 0));
        c.Children.Add(At(new Border { Width = 6.4, Height = 40, CornerRadius = new CornerRadius(3, 3, 2.5, 2.5), Background = wood,
            BorderBrush = Hex("#9A6428"), BorderThickness = new Thickness(0.5) }, 7.6, 0));
        c.Children.Add(At(new Border { Width = 15, Height = 4, CornerRadius = new CornerRadius(2),
            Background = new LinearGradientBrush(Hex("#E5E7EB").Color, Hex("#6B7280").Color, 90) }, -0.5, 15));
        c.RenderTransform = new TransformGroup { Children = { new RotateTransform(angle, 7, 20), new ScaleTransform(scale, scale, 7, 0) } };
        c.Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1.5, Direction = 270, Opacity = 0.35 };
        return At(c, x, y);
    }

    private static UIElement MetalClip(double x, double y, double angle, double scale)
    {
        var metal = new LinearGradientBrush(new GradientStopCollection
        {
            new(Hex("#D1D5DB").Color, 0), new(Hex("#FFFFFF").Color, 0.4), new(Hex("#9CA3AF").Color, 1),
        }, new Point(0, 0), new Point(1, 0));
        var b = new Border
        {
            Width = 14, Height = 34, CornerRadius = new CornerRadius(7), Background = metal,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), BorderThickness = new Thickness(0.8),
            Child = new Border { Width = 8, Height = 2.2, CornerRadius = new CornerRadius(1.1), Background = Hex("#4B5563"),
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 0, 0) },
            RenderTransform = new TransformGroup { Children = { new RotateTransform(angle, 7, 17), new ScaleTransform(scale, scale, 7, 0) } },
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1.5, Direction = 270, Opacity = 0.35 },
        };
        return At(b, x, y);
    }

    // ───────────────────────────── MSIX assets ─────────────────────────────

    /// <summary>Writes every logo the MSIX manifest references, at the scales Windows asks for.</summary>
    private static void WriteMsixAssets(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        void Tile(string name, int w, int h, double fill, params int[] scales)
        {
            foreach (var sc in scales)
            {
                int pw = w * sc / 100, ph = h * sc / 100;
                Save(Centered("glass", pw, ph, fill), System.IO.Path.Combine(dir, $"{name}.scale-{sc}.png"));
            }
        }
        Tile("Square44x44Logo", 44, 44, 1.0, 100, 125, 150, 200, 400);
        Tile("Square71x71Logo", 71, 71, 0.72, 100, 200);
        Tile("Square150x150Logo", 150, 150, 0.62, 100, 200, 400);
        Tile("Wide310x150Logo", 310, 150, 0.62, 100, 200, 400);
        Tile("Square310x310Logo", 310, 310, 0.6, 100, 200);
        Tile("StoreLogo", 50, 50, 1.0, 100, 200, 400);
        Tile("SplashScreen", 620, 300, 0.5, 100, 200);
        // Taskbar, Start and Alt+Tab use the target sizes; "unplated" variants sit directly on the taskbar.
        foreach (var t in new[] { 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256 })
        {
            var png = Render("glass", t);
            Save(png, System.IO.Path.Combine(dir, $"Square44x44Logo.targetsize-{t}.png"));
            Save(png, System.IO.Path.Combine(dir, $"Square44x44Logo.targetsize-{t}_altform-unplated.png"));
            Save(png, System.IO.Path.Combine(dir, $"Square44x44Logo.targetsize-{t}_altform-lightunplated.png"));
        }
        Console.WriteLine($"Wrote MSIX assets to {dir}");
    }

    /// <summary>The icon centred on a transparent canvas, taking <paramref name="fill"/> of the shorter side.</summary>
    private static BitmapSource Centered(string design, int w, int h, double fill)
    {
        var size = Math.Max(16, (int)Math.Round(Math.Min(w, h) * fill));
        var icon = Render(design, size);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawImage(icon, new Rect((w - size) / 2.0, (h - size) / 2.0, size, size));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        return rtb;
    }

    // ───────────────────────────── rendering ─────────────────────────────

    private static BitmapSource Render(string design, int size)
    {
        var art = Design(design, size);
        var root = new Viewbox { Width = size, Height = size, Child = art, Stretch = Stretch.Uniform };
        root.Measure(new Size(size, size));
        root.Arrange(new Rect(0, 0, size, size));
        root.UpdateLayout();
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);
        rtb.Freeze();
        return rtb;
    }

    private static BitmapSource Preview()
    {
        var designs = new[] { ("glass", "Susharka") };
        var sizes = new[] { 256, 64, 48, 32, 24, 16 };
        const int colW = 300, rowH = 360;
        var width = colW * designs.Length;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Hex("#F3F3F3"), null, new Rect(0, 0, width, rowH));
            dc.DrawRectangle(Hex("#1F1F1F"), null, new Rect(0, rowH, width, rowH));
            for (int i = 0; i < designs.Length; i++)
            {
                var (name, label) = designs[i];
                for (int band = 0; band < 2; band++)
                {
                    var top = band * rowH;
                    var ink = band == 0 ? Brushes.Black : Brushes.White;
                    dc.DrawText(Text(label, ink, 18), new Point(i * colW + 20, top + 14));
                    dc.DrawImage(Render(name, 256), new Rect(i * colW + 22, top + 46, 256, 256));
                    double x = i * colW + 22;
                    foreach (var s in sizes[1..])
                    {
                        dc.DrawImage(Render(name, s), new Rect(x, top + 318 - s / 2.0 + 8 - 16, s, s));
                        x += s + 14;
                    }
                }
            }
        }
        var rtb = new RenderTargetBitmap(width, rowH * 2, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        return rtb;
    }

    private static FormattedText Text(string s, Brush b, double size) =>
        new(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), size, b, 1.0);

    private static T At<T>(T e, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        return e;
    }

    private static SolidColorBrush Hex(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    private static LinearGradientBrush Gradient(double angle, params string[] colors)
    {
        var stops = new GradientStopCollection();
        for (int i = 0; i < colors.Length; i++) stops.Add(new GradientStop(Hex(colors[i]).Color, i / (double)(colors.Length - 1)));
        var rad = angle * Math.PI / 180;
        var dx = Math.Cos(rad) / 2;
        var dy = Math.Sin(rad) / 2;
        return new LinearGradientBrush(stops, new Point(0.5 - dx, 0.5 - dy), new Point(0.5 + dx, 0.5 + dy));
    }

    private static byte[] Png(BitmapSource b)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(b));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    private static void Save(BitmapSource b, string path) => File.WriteAllBytes(path, Png(b));

    private static void WriteIco(string path, int[] sizes, List<byte[]> pngs)
    {
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            var s = sizes[i];
            w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(pngs[i].Length); w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) w.Write(p);
    }
}

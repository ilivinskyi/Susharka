using System;

namespace Susharka.Views;

/// <summary>Geometry of the line. Pure maths in DIPs, shared by the panel, the flight and tests.</summary>
public static class LineLayout
{
    /// <summary>Height of the transparent strip the line lives in.</summary>
    public const double PanelHeight = 300;
    /// <summary>Y of the rope's ends, measured from the top of the screen.</summary>
    public const double RopeTop = 16;
    /// <summary>Horizontal distance between pegs.</summary>
    public const double Spacing = 174;
    public const double MaxPhotoWidth = 142;
    public const double MaxPhotoHeight = 150;
    public const double FrameInset = 4;
    public const double FrameRadius = 16;
    public const double PhotoRadius = FrameRadius - FrameInset;
    /// <summary>The peg pokes this far above the rope.</summary>
    public const double PinAbove = 9.5;
    /// <summary>Top edge of the frame below the rope.</summary>
    public const double FrameTop = 5;
    /// <summary>How far past each screen edge the rope runs before fading out.</summary>
    public const double Overhang = 20;

    /// <summary>The rope sags more on wider screens, up to 30 DIP.</summary>
    public static double Sag(double width) => Math.Min(30, width * 0.018);

    /// <summary>
    /// Y of the rope at x. The rope is a quadratic Bézier from (-20, top) to (w+20, top) with its control
    /// point at (w/2, top + 2·sag). Its control x is the midpoint, so x(t) is linear in t and
    /// y = top + 4·sag·t·(1−t).
    /// </summary>
    public static double RopeY(double x, double width)
    {
        var t = Math.Clamp((x + Overhang) / (width + 2 * Overhang), 0, 1);
        return RopeTop + 4 * Sag(width) * t * (1 - t);
    }

    /// <summary>Distance between pegs: 174 DIP, squeezed when the screen is too narrow for all photos.</summary>
    public static double SpacingFor(int count, double width)
        => count <= 1 ? Spacing : Math.Min(Spacing, (width - 2 * 90) / (count - 1));

    /// <summary>Centre x of slot i out of n, with the group centred on the screen.</summary>
    public static double SlotX(int index, int count, double width)
    {
        var s = SpacingFor(count, width);
        return width / 2 - (count - 1) * s / 2 + index * s;
    }

    /// <summary>Photo size inside the frame: fits a 142×150 box, keeping the aspect ratio.</summary>
    public static (double W, double H) PhotoSize(int pixelW, int pixelH)
    {
        if (pixelW <= 0 || pixelH <= 0) return (MaxPhotoWidth, MaxPhotoWidth * 0.625);
        var s = Math.Min(MaxPhotoWidth / pixelW, MaxPhotoHeight / pixelH);
        // Small crops show at 1:1 rather than blown up and blurry; tiny ones still get a usable 72 DIP.
        s = Math.Min(s, Math.Max(1, 72.0 / Math.Max(pixelW, pixelH)));
        return (Math.Max(24, Math.Round(pixelW * s)), Math.Max(24, Math.Round(pixelH * s)));
    }

    public static (double W, double H) FrameSize(int pixelW, int pixelH)
    {
        var (w, h) = PhotoSize(pixelW, pixelH);
        return (w + 2 * FrameInset, h + 2 * FrameInset);
    }
}

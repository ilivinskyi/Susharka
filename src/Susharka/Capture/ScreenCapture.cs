using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Susharka.Native;

namespace Susharka.Capture;

internal static class ScreenCapture
{
    /// <summary>Grabs the whole virtual desktop (all monitors) in physical pixels.</summary>
    public static BitmapSource CaptureVirtualScreen(out Win32.RECT bounds)
    {
        int x = Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN);
        int y = Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN);
        int w = Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN);
        int h = Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN);
        bounds = new Win32.RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };

        var screenDc = Win32.GetDC(IntPtr.Zero);
        var memDc = Win32.CreateCompatibleDC(screenDc);
        var hbmp = Win32.CreateCompatibleBitmap(screenDc, w, h);
        var old = Win32.SelectObject(memDc, hbmp);
        try
        {
            Win32.BitBlt(memDc, 0, 0, w, h, screenDc, x, y, Win32.SRCCOPY | Win32.CAPTUREBLT);
            Win32.SelectObject(memDc, old);
            var src = Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            // Drop the meaningless alpha channel GDI leaves behind.
            var bgr = new FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgr32, null, 0);
            bgr.Freeze();
            return bgr;
        }
        finally
        {
            Win32.DeleteObject(hbmp);
            Win32.DeleteDC(memDc);
            Win32.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>Crops a region given in screen pixels out of a virtual-screen capture.</summary>
    public static BitmapSource Crop(BitmapSource virtualScreen, Win32.RECT virtualBounds, Win32.RECT region)
    {
        var rect = new Int32Rect(region.Left - virtualBounds.Left, region.Top - virtualBounds.Top, region.Width, region.Height);
        rect.X = Math.Clamp(rect.X, 0, virtualScreen.PixelWidth - 1);
        rect.Y = Math.Clamp(rect.Y, 0, virtualScreen.PixelHeight - 1);
        rect.Width = Math.Clamp(rect.Width, 1, virtualScreen.PixelWidth - rect.X);
        rect.Height = Math.Clamp(rect.Height, 1, virtualScreen.PixelHeight - rect.Y);
        // Copy out so the full-desktop bitmap can be released.
        var cropped = new CroppedBitmap(virtualScreen, rect);
        var copy = new WriteableBitmap(cropped);
        copy.Freeze();
        return copy;
    }
}

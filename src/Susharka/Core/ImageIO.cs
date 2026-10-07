using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Susharka.Native;

namespace Susharka.Core;

internal static class ImageIO
{
    /// <summary>Loads an image fully into memory (no file lock), optionally downscaled for thumbnails.</summary>
    public static BitmapSource? Load(string path, int decodeWidth = 0)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.CreateOptions = BitmapCreateOptions.IgnoreColorProfile; // stream-based, so never served from the URI cache
            if (decodeWidth > 0) img.DecodePixelWidth = decodeWidth;
            img.StreamSource = fs;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Pixel size without decoding the whole image.</summary>
    public static (int W, int H) PixelSize(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }

    public static void Save(BitmapSource bitmap, string path)
    {
        BitmapEncoder enc = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
            ".bmp" => new BmpBitmapEncoder(),
            _ => new PngBitmapEncoder(),
        };
        enc.Frames.Add(BitmapFrame.Create(bitmap));
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp)) enc.Save(fs);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Puts the image on the clipboard as bitmap + PNG + file, so it pastes into apps and Explorer alike.</summary>
    public static bool CopyToClipboard(string path)
    {
        var bmp = Load(path);
        if (bmp == null) return false;

        var data = new DataObject();
        data.SetImage(bmp);
        var png = new MemoryStream();
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        enc.Save(png);
        png.Position = 0;
        data.SetData("PNG", png, false);
        data.SetFileDropList(new StringCollection { path });

        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(40); // another app holds the clipboard
            }
        }
        return false;
    }

    public static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception) { }
    }

    public static void ShowInExplorer(string path)
    {
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
        catch (Exception) { }
    }

    public static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception) { }
    }

    public static bool SendToRecycleBin(string path)
    {
        var op = new Win32.SHFILEOPSTRUCT
        {
            wFunc = Win32.FO_DELETE,
            pFrom = path + "\0\0",
            fFlags = (ushort)(Win32.FOF_ALLOWUNDO | Win32.FOF_NOCONFIRMATION | Win32.FOF_SILENT | Win32.FOF_NOERRORUI),
        };
        return Win32.SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
    }

    /// <summary>Renders an area of a visual (in its own coordinates) to a frozen bitmap at the given DPI scale.</summary>
    public static BitmapSource Snapshot(Visual visual, Rect area, double scale)
    {
        var rtb = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(area.Width * scale)), Math.Max(1, (int)Math.Ceiling(area.Height * scale)),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        // Draw through a VisualBrush so the element's offset inside its parent is ignored.
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawRectangle(new VisualBrush(visual)
            {
                Stretch = Stretch.Fill,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = area,
            }, null, new Rect(0, 0, area.Width, area.Height));
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}

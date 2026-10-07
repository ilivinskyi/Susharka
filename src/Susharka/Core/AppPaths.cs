using System;
using System.IO;
using System.Runtime.InteropServices;
using Susharka.Native;

namespace Susharka.Core;

internal static class AppPaths
{
    public static string SettingsFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Susharka", "settings.json");

    public static string DefaultScreenshotsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Susharka");

    private static readonly Guid ScreenshotsKnownFolder = new("b7bede81-df94-4682-a7d8-57a52620b86f");

    /// <summary>Where Windows itself saves Win+PrtScn and Snipping Tool captures.</summary>
    public static string WindowsScreenshotsFolder
    {
        get
        {
            try
            {
                if (Win32.SHGetKnownFolderPath(ScreenshotsKnownFolder, 0, IntPtr.Zero, out var p) == 0)
                {
                    var s = Marshal.PtrToStringUni(p);
                    Marshal.FreeCoTaskMem(p);
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch (Exception) { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        }
    }

    /// <summary>"Screenshot 2026-10-07 at 15.36.12.png", with a counter if taken within the same second.</summary>
    public static string NewScreenshotPath(string folder, DateTime now, string ext = ".png")
    {
        Directory.CreateDirectory(folder);
        var stem = $"Screenshot {now:yyyy-MM-dd} at {now:HH.mm.ss}";
        var path = Path.Combine(folder, stem + ext);
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{stem} ({i}){ext}");
        return path;
    }

    public static bool IsImage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif";
    }
}

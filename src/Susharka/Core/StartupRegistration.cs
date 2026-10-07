using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace Susharka.Core;

/// <summary>
/// "Open at login". The portable exe and the Setup.exe install use the per-user Run key; the Microsoft Store
/// (MSIX) build uses its declared StartupTask, which the user can also toggle in Settings → Apps → Startup.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "Susharka";
    /// <summary>Must match the TaskId in the MSIX manifest.</summary>
    public const string TaskId = "SusharkaStartup";

    public static bool IsPackaged { get; } = DetectPackage();

    /// <summary>Last known state; call <see cref="RefreshAsync"/> to update.</summary>
    public static bool IsEnabled { get; private set; }

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static async Task RefreshAsync()
    {
        try
        {
            if (IsPackaged)
            {
                var task = await StartupTask.GetAsync(TaskId);
                IsEnabled = task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                IsEnabled = key?.GetValue(Name) is string v && v.Equals(Command, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception)
        {
            IsEnabled = false;
        }
    }

    /// <summary>Turns starting at sign-in on or off. Returns a message when Windows won't allow the change.</summary>
    public static async Task<string?> SetAsync(bool enabled)
    {
        string? problem = null;
        try
        {
            if (IsPackaged)
            {
                var task = await StartupTask.GetAsync(TaskId);
                if (!enabled)
                {
                    task.Disable();
                }
                else if (task.State == StartupTaskState.DisabledByUser)
                {
                    problem = "Windows has Susharka turned off at startup. Turn it on in Settings → Apps → Startup.";
                }
                else if (task.State == StartupTaskState.DisabledByPolicy)
                {
                    problem = "Starting at sign-in is blocked by your organisation's policy.";
                }
                else
                {
                    await task.RequestEnableAsync();
                }
            }
            else
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (enabled) key.SetValue(Name, Command);
                else key.DeleteValue(Name, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            problem = ex.Message;
        }
        await RefreshAsync();
        return problem;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int length, System.Text.StringBuilder? name);

    private static bool DetectPackage()
    {
        try
        {
            int length = 0;
            return GetCurrentPackageFullName(ref length, null) != 15700; // APPMODEL_ERROR_NO_PACKAGE
        }
        catch (Exception)
        {
            return false; // pre-Windows 8
        }
    }
}

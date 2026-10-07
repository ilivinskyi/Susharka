using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace Susharka.Core;

/// <summary>
/// Watches the folder where Windows saves Win+PrtScn and Snipping Tool captures and reports each new
/// image once it has been fully written. Raised on a worker thread.
/// </summary>
internal sealed class ScreenshotWatcher : IDisposable
{
    private FileSystemWatcher? _watcher;
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string>? Captured;
    public string Folder { get; }

    public ScreenshotWatcher(string folder) => Folder = folder;

    public bool Start()
    {
        if (_watcher != null) return true;
        // Windows creates this folder on the first Win+PrtScn; make it now so we're already listening.
        try { Directory.CreateDirectory(Folder); }
        catch (Exception) { return false; }
        _watcher = new FileSystemWatcher(Folder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
            IncludeSubdirectories = false,
            InternalBufferSize = 64 * 1024,
        };
        _watcher.Created += (_, e) => Consider(e.FullPath);
        _watcher.Renamed += (_, e) => Consider(e.FullPath);
        _watcher.EnableRaisingEvents = true;
        return true;
    }

    public void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    private void Consider(string path)
    {
        if (!AppPaths.IsImage(path) || !_pending.TryAdd(path, 0)) return;
        Task.Run(async () =>
        {
            try
            {
                if (await WaitUntilReady(path)) Captured?.Invoke(path);
            }
            finally
            {
                await Task.Delay(2000); // swallow duplicate Created/Renamed notifications
                _pending.TryRemove(path, out _);
            }
        });
    }

    /// <summary>Waits until the writer has finished: size is stable and the file opens without sharing.</summary>
    private static async Task<bool> WaitUntilReady(string path)
    {
        long lastSize = -1;
        for (int i = 0; i < 50; i++)
        {
            await Task.Delay(120);
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return false;
                if (info.Length > 0 && info.Length == lastSize)
                {
                    using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return true;
                }
                lastSize = info.Length;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return false;
    }

    public void Dispose() => Stop();
}

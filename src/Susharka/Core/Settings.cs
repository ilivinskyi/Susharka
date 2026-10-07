using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Susharka.Core;

/// <summary>User preferences plus the list of photos currently on the line. Stored as JSON.</summary>
public sealed class Settings
{
    public string CaptureHotKey { get; set; } = "Ctrl+Shift+D4";
    public string ToggleHotKey { get; set; } = "Ctrl+Alt+T";
    public double RevealDelay { get; set; } = 0.4;
    public double RetractDelay { get; set; } = 0.5;
    public int MaxItems { get; set; } = 8;
    public bool Sounds { get; set; } = true;
    public bool WatchWindowsScreenshots { get; set; } = true;
    /// <summary>Also put each new capture on the clipboard, as Windows users expect from Win+Shift+S.</summary>
    public bool CopyOnCapture { get; set; } = true;
    public bool Welcomed { get; set; }
    public string? ScreenshotsFolder { get; set; }
    public List<string> Hung { get; set; } = new();

    [JsonIgnore] public string? FilePath { get; private set; }

    [JsonIgnore]
    public string EffectiveScreenshotsFolder =>
        string.IsNullOrWhiteSpace(ScreenshotsFolder) ? AppPaths.DefaultScreenshotsFolder : ScreenshotsFolder!;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load(string path)
    {
        Settings s;
        try
        {
            s = File.Exists(path)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json) ?? new Settings()
                : new Settings();
        }
        catch (Exception)
        {
            s = new Settings(); // corrupt file: start fresh rather than crash
        }
        s.FilePath = path;
        s.Normalize();
        return s;
    }

    public void Normalize()
    {
        RevealDelay = Math.Clamp(RevealDelay, 0.05, 5);
        RetractDelay = Math.Clamp(RetractDelay, 0.1, 5);
        MaxItems = Math.Clamp(MaxItems, 1, 30);
        Hung ??= new();
    }

    public void Save()
    {
        if (FilePath is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

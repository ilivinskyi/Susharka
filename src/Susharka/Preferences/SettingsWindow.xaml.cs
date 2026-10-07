using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using Susharka.Core;

namespace Susharka.Preferences;

/// <summary>Preferences. Every change applies and saves immediately.</summary>
public partial class SettingsWindow : Window
{
    private readonly Settings _settings;
    private readonly Func<List<string>> _apply;
    private bool _loading = true;

    internal SettingsWindow(Settings settings, HotKeyManager hotkeys, Func<List<string>> apply)
    {
        InitializeComponent();
        _settings = settings;
        _apply = apply;

        try
        {
            // Pick the largest frame of the .ico; the default is the blurry 16 px one.
            var frames = BitmapDecoder.Create(new Uri("pack://application:,,,/Assets/icon.ico"),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames;
            AppIcon.Source = frames.OrderByDescending(f => f.PixelWidth).First();
        }
        catch (Exception) { }
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        AboutText.Text = $"Version {version?.ToString(2)}";

        foreach (var box in new[] { CaptureKey, ToggleKey })
        {
            box.RecordingStarted = hotkeys.Suspend;
            box.RecordingEnded = hotkeys.Resume;
        }
        CaptureKey.Value = HotKey.Parse(settings.CaptureHotKey);
        ToggleKey.Value = HotKey.Parse(settings.ToggleHotKey);
        CaptureKey.Changed += k => { _settings.CaptureHotKey = k.Serialize(); Commit(); };
        ToggleKey.Changed += k => { _settings.ToggleHotKey = k.Serialize(); Commit(); };

        RevealDelay.Value = settings.RevealDelay;
        RevealDelay.ValueChanged += (_, e) => { _settings.RevealDelay = Math.Round(e.NewValue, 2); UpdateLabels(); Commit(); };
        MaxItems.Value = settings.MaxItems;
        MaxItems.ValueChanged += (_, e) => { _settings.MaxItems = (int)e.NewValue; UpdateLabels(); Commit(); };

        WatchWindows.IsChecked = settings.WatchWindowsScreenshots;
        WatchWindows.Click += (_, _) => { _settings.WatchWindowsScreenshots = WatchWindows.IsChecked == true; Commit(); };
        CopyOnCapture.IsChecked = settings.CopyOnCapture;
        CopyOnCapture.Click += (_, _) => { _settings.CopyOnCapture = CopyOnCapture.IsChecked == true; Commit(); };
        SoundsBox.IsChecked = settings.Sounds;
        SoundsBox.Click += (_, _) => { _settings.Sounds = SoundsBox.IsChecked == true; Commit(); };

        LoginBox.IsChecked = StartupRegistration.IsEnabled;
        LoginBox.Click += (_, _) =>
        {
            try { StartupRegistration.Set(LoginBox.IsChecked == true); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Susharka"); }
            LoginBox.IsChecked = StartupRegistration.IsEnabled;
        };

        UpdateLabels();
        _loading = false;
    }

    private void UpdateLabels()
    {
        RevealDelayText.Text = $"{_settings.RevealDelay:0.##} s";
        MaxItemsText.Text = _settings.MaxItems.ToString();
        FolderText.Text = _settings.EffectiveScreenshotsFolder;
        FolderText.ToolTip = _settings.EffectiveScreenshotsFolder;
    }

    private void Commit()
    {
        if (_loading) return;
        var problems = _apply();
        _settings.Save();
        KeyProblem.Text = string.Join(" ", problems);
        KeyProblem.Visibility = problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e) => ImageIO.OpenFolder(_settings.EffectiveScreenshotsFolder);

    private void OnChangeFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Where should screenshots be saved?",
            InitialDirectory = _settings.EffectiveScreenshotsFolder,
        };
        if (dlg.ShowDialog(this) != true) return;
        _settings.ScreenshotsFolder = dlg.FolderName;
        UpdateLabels();
        Commit();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

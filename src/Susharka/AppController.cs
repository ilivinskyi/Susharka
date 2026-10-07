using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Susharka.Capture;
using Susharka.Core;
using Susharka.Markup;
using Susharka.Native;
using Susharka.Preferences;
using Susharka.Views;
using Line = Susharka.Core.Line;
using Monitor = Susharka.Native.Monitor;

namespace Susharka;

/// <summary>Composition root: wires the line model, the panel, capture, hotkeys, tray and the folder watcher.</summary>
internal sealed class AppController : ICardActions, IDisposable
{
    public const string ShowSignalName = @"Local\Susharka.Show";
    private const string CaptureHotKeyName = "capture";
    private const string ToggleHotKeyName = "toggle";

    private Settings _settings = null!;
    private Line _line = null!;
    private LinePanel _panel = null!;
    private HotKeyManager _hotkeys = null!;
    private TrayIcon _tray = null!;
    private ScreenshotWatcher? _watcher;
    private readonly RevealLogic _reveal = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HashSet<string> _ownFiles = new(StringComparer.OrdinalIgnoreCase);
    private EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _showWait;
    private SettingsWindow? _settingsWindow;

    private bool _pinned;
    private double _peekUntil;
    private double? _otherBandSince;
    private bool _buttonsWereDown;
    private double _dismissedAt = double.NegativeInfinity;
    private (Rect Source, BitmapSource Image)? _pendingFlight;

    private double Now => _clock.Elapsed.TotalSeconds;
    private static Dispatcher Ui => Application.Current.Dispatcher;

    public void Start()
    {
        _settings = Settings.Load(AppPaths.SettingsFile);
        Sounds.Enabled = _settings.Sounds;

        _line = new Line(_settings.MaxItems);
        _panel = new LinePanel { Actions = this };
        _panel.PlaceOn(Monitors.UnderCursor());

        _line.Added += OnAdded;
        _line.Removed += (item, reason) => _panel.RemoveCard(item, reason, animated: true);
        _line.Updated += item => _panel.UpdateCard(item);
        _line.Changed += () =>
        {
            _settings.Hung = _line.Snapshot();
            _settings.Save();
        };
        _line.Restore(_settings.Hung, File.Exists);

        _hotkeys = new HotKeyManager();
        _tray = new TrayIcon("Susharka");
        _tray.Click += ToggleLine;
        _tray.MenuFactory = BuildTrayMenu;

        ApplySettings(initial: true);
        _ = StartupRegistration.RefreshAsync();

        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showSignal,
            (_, _) => Ui.BeginInvoke(OpenSettings), null, -1, false);

        if (!_settings.Welcomed)
        {
            _settings.Welcomed = true;
            _settings.Save();
            _tray.ShowBalloon("Susharka is running",
                $"Press {HotKey.Parse(_settings.CaptureHotKey).Display()} to take a screenshot. " +
                "Rest the pointer at the top of the screen to see your line.");
            Peek(Monitors.UnderCursor(), 3.5);
        }
    }

    /// <summary>Re-reads settings into every component. Returns problems worth telling the user about.</summary>
    public List<string> ApplySettings(bool initial = false)
    {
        var problems = new List<string>();
        _settings.Normalize();
        Sounds.Enabled = _settings.Sounds;
        _reveal.RevealDelay = _settings.RevealDelay;
        _reveal.RetractDelay = _settings.RetractDelay;
        _line.MaxItems = _settings.MaxItems;
        _line.TrimToCapacity();

        var capture = HotKey.Parse(_settings.CaptureHotKey);
        var toggle = HotKey.Parse(_settings.ToggleHotKey);
        if (!_hotkeys.Set(CaptureHotKeyName, capture, TakeScreenshot))
            problems.Add($"{capture.Display()} is already used by another app.");
        if (!_hotkeys.Set(ToggleHotKeyName, toggle, ToggleLine))
            problems.Add($"{toggle.Display()} is already used by another app.");
        _panel.HintText = capture.IsEmpty
            ? "Screenshots will hang here"
            : $"Screenshots will hang here  ·  {capture.Display()}";

        _watcher?.Dispose();
        _watcher = null;
        if (_settings.WatchWindowsScreenshots)
        {
            _watcher = new ScreenshotWatcher(AppPaths.WindowsScreenshotsFolder);
            _watcher.Captured += path => Ui.BeginInvoke(() => OnWindowsScreenshot(path));
            _watcher.Start();
        }

        if (problems.Count > 0) Log("Hotkeys: " + string.Join(" ", problems));
        if (initial && problems.Count > 0)
            _tray.ShowBalloon("Shortcut unavailable", string.Join(" ", problems) + " Change it in Settings.");
        return problems;
    }

    // ───────────────────────────── reveal loop ─────────────────────────────

    private void Tick()
    {
        if (!Win32.GetCursorPos(out var p)) return;
        var monitor = Monitors.FromPoint(p.X, p.Y);
        var now = Now;

        var band = Math.Max(2, (int)Math.Round(2 * monitor.Scale));
        var inBand = p.Y < monitor.Bounds.Top + band;
        var onPanelMonitor = _panel.Monitor?.Device == monitor.Device;
        var local = monitor.ToLocalDip(p.X, p.Y);
        var inZone = _panel.IsRevealed && onPanelMonitor && _panel.IsInZone(local);
        var buttonsDown = Win32.IsDown(Win32.VK_LBUTTON) || Win32.IsDown(Win32.VK_RBUTTON) || Win32.IsDown(Win32.VK_MBUTTON);
        var pressed = buttonsDown && !_buttonsWereDown;
        _buttonsWereDown = buttonsDown;
        var capturing = CaptureSession.IsActive;

        // A click anywhere but on a photo puts the line away, even when pinned.
        if (pressed && _reveal.Shown && _panel.IsRevealed && !capturing && !_panel.IsBusy &&
            !(onPanelMonitor && _panel.IsOverCard(local)))
        {
            _dismissedAt = now;
            Dismiss();
            return;
        }
        var held = _pinned || _panel.IsBusy || now < _peekUntil;
        var blocked = capturing || (inBand && !_panel.IsBusy && (buttonsDown || FullScreen.IsActive(monitor)));

        // Already open on another monitor and the pointer rests at this one's top edge: move over.
        if (_reveal.Shown && inBand && !onPanelMonitor && !blocked && !_panel.IsBusy)
        {
            _otherBandSince ??= now;
            if (now - _otherBandSince >= _reveal.RevealDelay)
            {
                _otherBandSince = null;
                _panel.Retract(instantly: true);
                _panel.PlaceOn(monitor);
                _panel.Reveal();
                _line.Prune(File.Exists);
            }
            return;
        }
        _otherBandSince = null;

        if (!_reveal.Update(new RevealInput(now, inBand, inZone, held && !capturing, blocked))) return;

        if (_reveal.Shown)
        {
            if (!onPanelMonitor && inBand) _panel.PlaceOn(monitor);
            _line.Prune(File.Exists);
            _panel.Reveal();
        }
        else
        {
            _pinned = false;
            _panel.Retract();
        }
    }

    /// <summary>Shows the line on a monitor for a moment, as after a capture.</summary>
    private void Peek(Monitor monitor, double seconds)
    {
        if (FullScreen.IsActive(monitor)) return;
        if (_panel.Monitor?.Device != monitor.Device || !_panel.IsRevealed) _panel.PlaceOn(monitor);
        _peekUntil = Now + seconds;
        _reveal.Set(true, Now);
        _panel.Reveal();
    }

    private void Dismiss()
    {
        _pinned = false;
        _peekUntil = 0;
        _reveal.Set(false, Now);
        _panel.Retract();
    }

    public void ToggleLine()
    {
        // Clicking the tray icon to hide the line: the press already dismissed it, so don't reopen it.
        if (Now - _dismissedAt < 0.6) return;
        if (_reveal.Shown)
        {
            Dismiss();
        }
        else
        {
            _pinned = true;
            _panel.PlaceOn(Monitors.UnderCursor());
            _reveal.Set(true, Now);
            _line.Prune(File.Exists);
            _panel.Reveal();
        }
    }

    // ───────────────────────────── capturing ─────────────────────────────

    public void TakeScreenshot()
    {
        if (CaptureSession.IsActive) return;
        _pinned = false;
        _peekUntil = 0;
        _reveal.Set(false, Now);
        _panel.Retract(instantly: true);
        FxWindow.HideAll();

        Ui.BeginInvoke(async () =>
        {
            await Task.Delay(90); // let the compositor drop our windows before the screen is frozen
            CaptureSession.Start(OnCaptured);
        }, DispatcherPriority.Background);
    }

    private void OnCaptured(BitmapSource image, Win32.RECT region)
    {
        Sounds.PlayShutter();
        string path;
        try
        {
            path = AppPaths.NewScreenshotPath(_settings.EffectiveScreenshotsFolder, DateTime.Now);
            _ownFiles.Add(path);
            ImageIO.Save(image, path);
        }
        catch (Exception ex)
        {
            Log(ex.ToString());
            _tray.ShowBalloon("Couldn't save the screenshot", ex.Message);
            return;
        }
        if (_settings.CopyOnCapture) ImageIO.CopyToClipboard(path);
        Hang(path, region, image);
    }

    /// <summary>Win+PrtScn / Snipping Tool saved a file: copy it into our folder and hang it.</summary>
    private void OnWindowsScreenshot(string path)
    {
        if (_ownFiles.Contains(path) || !File.Exists(path)) return;
        var folder = _settings.EffectiveScreenshotsFolder;
        var target = path;
        try
        {
            if (!string.Equals(Path.GetFullPath(Path.GetDirectoryName(path)!).TrimEnd('\\'),
                    Path.GetFullPath(folder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(folder);
                target = Path.Combine(folder, Path.GetFileName(path));
                var stem = Path.GetFileNameWithoutExtension(path);
                var ext = Path.GetExtension(path);
                for (int i = 2; File.Exists(target); i++) target = Path.Combine(folder, $"{stem} ({i}){ext}");
                _ownFiles.Add(target);
                File.Copy(path, target);
            }
        }
        catch (Exception ex)
        {
            Log(ex.ToString());
            target = path;
        }

        // A full-screen capture flies down from the whole monitor; a region (Snipping Tool) just drops in.
        Win32.RECT? source = null;
        BitmapSource? image = null;
        var (w, h) = ImageIO.PixelSize(target);
        var match = Monitors.All().FirstOrDefault(m => m.Bounds.Width == w && m.Bounds.Height == h);
        if (match != null)
        {
            source = match.Bounds;
            image = ImageIO.Load(target, Math.Min(w, 1600));
        }
        Hang(target, source, image);
    }

    private void Hang(string path, Win32.RECT? source, BitmapSource? image)
    {
        Monitor monitor;
        if (source is { } r) monitor = Monitors.FromPoint(r.Left + r.Width / 2, r.Top + r.Height / 2);
        else monitor = Monitors.UnderCursor();

        if (FullScreen.IsActive(monitor) && source == null)
        {
            _line.Hang(path, animated: false);
            return;
        }

        Peek(monitor, 2.8);
        if (source is { } rect && image != null && _panel.Monitor != null)
            _pendingFlight = (_panel.Monitor.ToLocalDip(rect), image);
        try
        {
            _line.Hang(path);
        }
        finally
        {
            _pendingFlight = null;
        }
    }

    private void OnAdded(Pegged item, bool animated)
    {
        var index = _line.Items.ToList().IndexOf(item);
        var flight = _pendingFlight;
        var card = _panel.AddCard(item, index, animated && _panel.IsRevealed, awaitFlight: flight != null);
        if (flight is { } f)
        {
            // Wait a frame so the card has its slot before the photo flies to it.
            Ui.BeginInvoke(() => _panel.Fly(card, f.Source, f.Image, Sounds.PlayPeg), DispatcherPriority.Loaded);
        }
        else if (animated)
        {
            Sounds.PlayPeg();
        }
    }

    // ───────────────────────────── card actions ─────────────────────────────

    public void Copy(Pegged item)
    {
        if (!ImageIO.CopyToClipboard(item.Path)) _line.Prune(File.Exists);
    }

    public void Open(Pegged item) => ImageIO.Open(item.Path);

    public void Markup(Pegged item)
    {
        if (!File.Exists(item.Path))
        {
            _line.Prune(File.Exists);
            return;
        }
        var editor = new MarkupWindow(item.Path);
        editor.Saved += () => _line.Touch(item);
        editor.Show();
        editor.Activate();
    }

    public void LetGo(Pegged item)
    {
        Sounds.PlayWhoosh();
        _line.Remove(item, RemovalReason.LetGo);
    }

    public void Delete(Pegged item)
    {
        if (ImageIO.SendToRecycleBin(item.Path) || !File.Exists(item.Path))
        {
            Sounds.PlayWhoosh();
            _line.Remove(item, RemovalReason.LetGo);
        }
    }

    public void SaveAs(Pegged item)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = Path.GetFileName(item.Path),
            DefaultExt = Path.GetExtension(item.Path),
            Filter = "Image|*" + Path.GetExtension(item.Path),
        };
        if (dlg.ShowDialog() != true) return;
        try { File.Copy(item.Path, dlg.FileName, overwrite: true); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Susharka", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    public void ShowInExplorer(Pegged item) => ImageIO.ShowInExplorer(item.Path);

    public void DragEnded(Pegged item)
    {
        // Explorer finishes moves asynchronously; give it a moment before checking whether the file left.
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.6) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (!File.Exists(item.Path)) _line.Remove(item, RemovalReason.Gone);
        };
        t.Start();
    }

    // ───────────────────────────── tray ─────────────────────────────

    private ContextMenu BuildTrayMenu()
    {
        var menu = new ContextMenu();
        MenuItem Add(string header, Action act, string? gesture = null, bool? check = null, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture ?? "", IsEnabled = enabled };
            if (check is { } c) mi.IsChecked = c;
            mi.Click += (_, _) => act();
            menu.Items.Add(mi);
            return mi;
        }
        void Sep() => menu.Items.Add(new Separator());

        var toggleKey = HotKey.Parse(_settings.ToggleHotKey);
        var captureKey = HotKey.Parse(_settings.CaptureHotKey);
        Add(_reveal.Shown ? "Hide line" : "Show line", ToggleLine, toggleKey.IsEmpty ? null : toggleKey.Display());
        Add("Take a screenshot", TakeScreenshot, captureKey.IsEmpty ? null : captureKey.Display());
        Sep();
        Add("Take everything down", TakeEverythingDown, enabled: _line.Items.Count > 0);
        Add("Open screenshots folder", () => ImageIO.OpenFolder(_settings.EffectiveScreenshotsFolder));
        Sep();
        Add("Hang Windows screenshots", () =>
        {
            _settings.WatchWindowsScreenshots = !_settings.WatchWindowsScreenshots;
            _settings.Save();
            ApplySettings();
        }, check: _settings.WatchWindowsScreenshots);
        Add("Sounds", () =>
        {
            _settings.Sounds = !_settings.Sounds;
            _settings.Save();
            ApplySettings();
        }, check: _settings.Sounds);
        Add("Open at login", async () =>
        {
            var problem = await StartupRegistration.SetAsync(!StartupRegistration.IsEnabled);
            if (problem != null) _tray.ShowBalloon("Open at login", problem);
        }, check: StartupRegistration.IsEnabled);
        Sep();
        Add("Settings…", OpenSettings);
        Add("Quit Susharka", () => Application.Current.Shutdown());
        return menu;
    }

    private void TakeEverythingDown()
    {
        var items = _line.Items.ToList();
        if (items.Count == 0) return;
        if (!_panel.IsRevealed) Peek(_panel.Monitor ?? Monitors.UnderCursor(), 1.6);
        Sounds.PlayWhoosh();
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120 + 70 * i) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                _line.Remove(item, RemovalReason.LetGo);
            };
            t.Start();
        }
    }

    public void OpenSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, _hotkeys, () => ApplySettings());
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    // ───────────────────────────── housekeeping ─────────────────────────────

    public static void Log(string text)
    {
        try
        {
            var path = Path.Combine(Path.GetDirectoryName(AppPaths.SettingsFile)!, "log.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
            File.AppendAllText(path, $"[{DateTime.Now:u}] {text}{Environment.NewLine}");
        }
        catch (Exception) { }
    }

    public void Dispose()
    {
        _timer.Stop();
        _showWait?.Unregister(null);
        _showSignal?.Dispose();
        _watcher?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _settings?.Save();
    }
}

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Susharka.Core;

namespace Susharka.Preferences;

/// <summary>
/// Click, then press a key combination to record a shortcut. Backspace/Delete clears it; Esc keeps the old one.
/// Global shortcuts are suspended while recording so the old combo doesn't fire.
/// </summary>
internal sealed class HotKeyBox : Border
{
    private readonly TextBlock _text = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        FontSize = 13,
    };
    private HotKey _value;
    private bool _recording;

    public event Action<HotKey>? Changed;
    public Action? RecordingStarted { get; set; }
    public Action? RecordingEnded { get; set; }

    public HotKeyBox()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        MinWidth = 170;
        Height = 32;
        CornerRadius = new CornerRadius(7);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(12, 0, 12, 0);
        Child = _text;
        ApplyLook();

        MouseLeftButtonDown += (_, e) =>
        {
            Focus();
            Keyboard.Focus(this);
            StartRecording();
            e.Handled = true;
        };
        GotKeyboardFocus += (_, _) => StartRecording();
        LostKeyboardFocus += (_, _) => StopRecording();
        PreviewKeyDown += OnKeyDown;
    }

    public HotKey Value
    {
        get => _value;
        set
        {
            _value = value;
            ApplyLook();
        }
    }

    private void StartRecording()
    {
        if (_recording) return;
        _recording = true;
        RecordingStarted?.Invoke();
        ApplyLook();
    }

    private void StopRecording()
    {
        if (!_recording) return;
        _recording = false;
        RecordingEnded?.Invoke();
        ApplyLook();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.ImeProcessed) key = e.ImeProcessedKey;

        switch (key)
        {
            case Key.Escape:
                Keyboard.ClearFocus();
                return;
            case Key.Back:
            case Key.Delete when Keyboard.Modifiers == ModifierKeys.None:
                Commit(HotKey.None);
                return;
            case Key.Tab when Keyboard.Modifiers == ModifierKeys.None:
                Keyboard.ClearFocus();
                return;
        }

        var mods = Keyboard.Modifiers;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= ModifierKeys.Windows;
        if (HotKey.IsModifierKey(key))
        {
            _text.Text = mods == ModifierKeys.None ? "Press a shortcut…" : Describe(mods) + " + …";
            return;
        }
        var hk = new HotKey(mods, key);
        if (!hk.IsValid)
        {
            _text.Text = "Add Ctrl, Alt, Shift or Win";
            return;
        }
        Commit(hk);
    }

    private static string Describe(ModifierKeys m)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (m.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        return string.Join(" + ", parts);
    }

    private void Commit(HotKey hk)
    {
        _value = hk;
        Changed?.Invoke(hk);
        Keyboard.ClearFocus();
        StopRecording();
        ApplyLook();
    }

    private void ApplyLook()
    {
        if (_recording)
        {
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0x2F, 0x6F, 0xEB));
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xEB));
            _text.Text = "Press a shortcut…";
            _text.Foreground = new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xEB));
        }
        else
        {
            Background = Brushes.White;
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0, 0, 0));
            _text.Text = _value.IsEmpty ? "Not set" : _value.Display();
            _text.Foreground = new SolidColorBrush(_value.IsEmpty ? Color.FromRgb(0x90, 0x92, 0x98) : Color.FromRgb(0x1C, 0x1D, 0x21));
        }
    }
}

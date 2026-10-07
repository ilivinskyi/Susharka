using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Interop;
using Susharka.Native;

namespace Susharka.Core;

/// <summary>System-wide shortcuts via RegisterHotKey on a message-only window.</summary>
internal sealed class HotKeyManager : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, (HotKey Key, Action Callback)> _registered = new();
    private readonly Dictionary<string, int> _ids = new();
    private int _nextId = 1;
    private bool _suspended;

    public HotKeyManager()
    {
        var p = new HwndSourceParameters("SusharkaHotKeys") { ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */ };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>Registers (or replaces) the shortcut for a named action. Returns false when another app owns it.</summary>
    public bool Set(string name, HotKey key, Action callback)
    {
        if (_ids.TryGetValue(name, out var oldId))
        {
            Win32.UnregisterHotKey(_source.Handle, oldId);
            _registered.Remove(oldId);
            _ids.Remove(name);
        }
        if (!key.IsValid) return key.IsEmpty;

        var id = _nextId++;
        _ids[name] = id;
        _registered[id] = (key, callback);
        return _suspended || Register(id, key);
    }

    /// <summary>Temporarily releases all shortcuts, e.g. while the user records a new one.</summary>
    public void Suspend()
    {
        if (_suspended) return;
        _suspended = true;
        foreach (var id in _registered.Keys) Win32.UnregisterHotKey(_source.Handle, id);
    }

    public void Resume()
    {
        if (!_suspended) return;
        _suspended = false;
        foreach (var (id, entry) in _registered) Register(id, entry.Key);
    }

    private bool Register(int id, HotKey key)
    {
        uint mods = Win32.MOD_NOREPEAT;
        if (key.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= Win32.MOD_ALT;
        if (key.Modifiers.HasFlag(ModifierKeys.Control)) mods |= Win32.MOD_CONTROL;
        if (key.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= Win32.MOD_SHIFT;
        if (key.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= Win32.MOD_WIN;
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key.Key);
        return Win32.RegisterHotKey(_source.Handle, id, mods, vk);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY && _registered.TryGetValue(wParam.ToInt32(), out var entry))
        {
            handled = true;
            entry.Callback();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _registered.Keys) Win32.UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
        _source.Dispose();
    }
}

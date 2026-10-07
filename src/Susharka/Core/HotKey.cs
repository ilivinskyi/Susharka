using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace Susharka.Core;

/// <summary>A global keyboard shortcut, e.g. "Ctrl+Shift+4". Serialised as "Ctrl+Shift+D4".</summary>
public readonly record struct HotKey(ModifierKeys Modifiers, Key Key)
{
    public static readonly HotKey None = new(ModifierKeys.None, Key.None);
    public bool IsEmpty => Key == Key.None;

    /// <summary>A global shortcut needs at least one modifier, except for function keys and PrintScreen.</summary>
    public bool IsValid =>
        Key != Key.None && !IsModifierKey(Key) &&
        (Modifiers != ModifierKeys.None || Key is >= Key.F1 and <= Key.F24 || Key == Key.PrintScreen);

    public static bool IsModifierKey(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    public string Serialize()
    {
        if (IsEmpty) return "";
        var parts = ModifierNames();
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }

    public static HotKey Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return None;
        var mods = ModifierKeys.None;
        var key = Key.None;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= ModifierKeys.Control; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "win": case "windows": mods |= ModifierKeys.Windows; break;
                default:
                    if (raw.Length == 1 && char.IsDigit(raw[0])) key = Key.D0 + (raw[0] - '0');
                    else if (Enum.TryParse<Key>(raw, true, out var k)) key = k;
                    else return None;
                    break;
            }
        }
        return new HotKey(mods, key);
    }

    /// <summary>Human-readable form, e.g. "Ctrl + Shift + 4".</summary>
    public string Display()
    {
        if (IsEmpty) return "None";
        var parts = ModifierNames();
        parts.Add(KeyName(Key));
        return string.Join(" + ", parts);
    }

    private List<string> ModifierNames()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        return parts;
    }

    public static string KeyName(Key k) => k switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (k - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (k - Key.NumPad0),
        Key.PrintScreen => "PrtScn",
        Key.OemTilde => "`",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemPipe => "\\",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.Return => "Enter",
        Key.Escape => "Esc",
        _ => k.ToString(),
    };

    public override string ToString() => Display();
}

using System.Globalization;

namespace IdleViz.Core;

/// <summary>The modifier keys of a global hotkey. The values are the ones <c>RegisterHotKey</c> takes.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>A global hotkey: one key plus at least one modifier.</summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, ushort Key)
{
    /// <summary>Ctrl + Alt + V, the Mac app's ⌃⌥V.</summary>
    public static Hotkey Default { get; } = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x56);

    /// <summary>
    /// A key alone would take that key away from every other app, so a modifier is required.
    /// A modifier key can't be the key itself.
    /// </summary>
    public bool IsValid => Modifiers != HotkeyModifiers.None && Key != 0 && !IsModifierKey(Key);

    /// <summary>"Ctrl + Alt + V", as the settings row and the flyout show it.</summary>
    public string Label => string.Join(" + ", Parts());

    /// <summary>"Ctrl+Alt+V", as a menu shows a shortcut.</summary>
    public string MenuLabel => string.Join("+", Parts());

    private IEnumerable<string> Parts()
    {
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl))
        {
            yield return "Ctrl";
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            yield return "Alt";
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            yield return "Shift";
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            yield return "Win";
        }

        yield return KeyName(Key);
    }

    /// <summary>Shift, Ctrl, Alt and the Windows keys, with or without a side.</summary>
    public static bool IsModifierKey(ushort virtualKey) =>
        virtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);

    /// <summary>The modifier a modifier key stands for, or None for any other key.</summary>
    public static HotkeyModifiers ModifierFor(ushort virtualKey) => virtualKey switch
    {
        0x10 or 0xA0 or 0xA1 => HotkeyModifiers.Shift,
        0x11 or 0xA2 or 0xA3 => HotkeyModifiers.Ctrl,
        0x12 or 0xA4 or 0xA5 => HotkeyModifiers.Alt,
        0x5B or 0x5C => HotkeyModifiers.Win,
        _ => HotkeyModifiers.None,
    };

    /// <summary>A name for a virtual key. Letters and digits are the same on every keyboard layout.</summary>
    public static string KeyName(ushort virtualKey) => virtualKey switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
        >= 0x70 and <= 0x87 => "F" + (virtualKey - 0x6F).ToString(CultureInfo.InvariantCulture),
        >= 0x60 and <= 0x69 => "Num " + (virtualKey - 0x60).ToString(CultureInfo.InvariantCulture),
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0D => "Enter",
        0x13 => "Pause",
        0x1B => "Esc",
        0x20 => "Space",
        0x21 => "Page Up",
        0x22 => "Page Down",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "←",
        0x26 => "↑",
        0x27 => "→",
        0x28 => "↓",
        0x2C => "Print Screen",
        0x2D => "Insert",
        0x2E => "Delete",
        0x6A => "Num *",
        0x6B => "Num +",
        0x6D => "Num -",
        0x6E => "Num .",
        0x6F => "Num /",
        0xBA => ";",
        0xBB => "=",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        _ => "Key " + virtualKey.ToString(CultureInfo.InvariantCulture),
    };
}

/// <summary>
/// The "Open hotkey" setting, stored as two numbers: the key's virtual-key code and the modifier flags.
/// A key of 0 means the hotkey was cleared. Nothing stored means the default.
/// </summary>
public static class HotkeySetting
{
    public const string KeyKey = "openHotkeyKey";
    public const string ModifiersKey = "openHotkeyModifiers";

    /// <summary>The stored hotkey, or null if it was cleared. Values that can't be a hotkey count as never set.</summary>
    public static Hotkey? Read(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var key = settings.GetInt(KeyKey);
        var modifiers = settings.GetInt(ModifiersKey);
        if (key is null || modifiers is null)
        {
            return Hotkey.Default;
        }

        if (key == 0)
        {
            return null;
        }

        if (key is < 0 or > 0xFE || modifiers is < 0 or > 15)
        {
            return Hotkey.Default;
        }

        var hotkey = new Hotkey((HotkeyModifiers)modifiers.Value, (ushort)key.Value);
        return hotkey.IsValid ? hotkey : Hotkey.Default;
    }

    public static void Write(SettingsStore settings, Hotkey? hotkey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.SetInt(KeyKey, hotkey?.Key ?? 0);
        settings.SetInt(ModifiersKey, (int)(hotkey?.Modifiers ?? HotkeyModifiers.None));
    }
}

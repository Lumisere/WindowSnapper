namespace WindowSnapper.Services;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Meta = 8
}

public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    public string DisplayText
    {
        get
        {
            var parts = new List<string>(5);
            if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(HotkeyModifiers.Meta)) parts.Add("Super");
            parts.Add(Key);
            return string.Join('+', parts);
        }
    }

    public string XdgTrigger
    {
        get
        {
            var parts = new List<string>(5);
            if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("CTRL");
            if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("ALT");
            if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("SHIFT");
            if (Modifiers.HasFlag(HotkeyModifiers.Meta)) parts.Add("LOGO");
            parts.Add(ToXdgKey(Key));
            return string.Join('+', parts);
        }
    }

    public string X11KeysymName => ToX11Key(Key);

    public uint WindowsVirtualKey => ToWindowsVirtualKey(Key);

    public bool MatchesAvalonia(string keyName, bool control, bool alt, bool shift, bool meta)
    {
        var modifiers = HotkeyModifiers.None;
        if (control) modifiers |= HotkeyModifiers.Control;
        if (alt) modifiers |= HotkeyModifiers.Alt;
        if (shift) modifiers |= HotkeyModifiers.Shift;
        if (meta) modifiers |= HotkeyModifiers.Meta;
        if (modifiers != Modifiers)
            return false;

        var normalized = NormalizeAvaloniaKeyName(keyName);
        return string.Equals(normalized, Key, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeAvaloniaKeyName(string keyName)
    {
        if (keyName.Length == 2 && keyName[0] == 'D' && char.IsDigit(keyName[1]))
            return keyName[1].ToString();

        return keyName switch
        {
            "Return" => "Enter",
            "Esc" => "Escape",
            "Back" => "Backspace",
            "Prior" => "PageUp",
            "Next" => "PageDown",
            "Snapshot" => "PrintScreen",
            _ => NormalizeKey(keyName)
        };
    }

    public static bool TryParse(string? value, out HotkeyGesture gesture, out string error)
    {
        gesture = default;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Enter a shortcut such as Ctrl+Shift+S.";
            return false;
        }

        var tokens = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
        {
            error = "Use at least one modifier plus a key, for example Ctrl+Shift+S.";
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        string? key = null;
        foreach (var raw in tokens)
        {
            if (TryParseModifier(raw, out var modifier))
            {
                modifiers |= modifier;
                continue;
            }

            if (key is not null)
            {
                error = "A shortcut can contain only one non-modifier key.";
                return false;
            }

            key = NormalizeKey(raw);
            if (key is null)
            {
                error = $"Unsupported hotkey key: {raw}.";
                return false;
            }
        }

        if (modifiers == HotkeyModifiers.None)
        {
            error = "Global hotkeys must include Ctrl, Alt, Shift, or Super.";
            return false;
        }

        if (key is null)
        {
            error = "The shortcut is missing a key.";
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    private static bool TryParseModifier(string value, out HotkeyModifiers modifier)
    {
        modifier = value.Trim().ToLowerInvariant() switch
        {
            "ctrl" or "control" => HotkeyModifiers.Control,
            "alt" => HotkeyModifiers.Alt,
            "shift" => HotkeyModifiers.Shift,
            "super" or "meta" or "win" or "windows" or "logo" => HotkeyModifiers.Meta,
            _ => HotkeyModifiers.None
        };
        return modifier != HotkeyModifiers.None;
    }

    private static string? NormalizeKey(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 1)
        {
            var c = trimmed[0];
            if (char.IsLetter(c))
                return char.ToUpperInvariant(c).ToString();
            if (char.IsDigit(c))
                return c.ToString();
        }

        if (trimmed.Length is >= 2 and <= 3
            && trimmed[0] is 'f' or 'F'
            && int.TryParse(trimmed[1..], out var function)
            && function is >= 1 and <= 24)
            return $"F{function}";

        return trimmed.ToLowerInvariant() switch
        {
            "space" or "spacebar" => "Space",
            "enter" or "return" => "Enter",
            "escape" or "esc" => "Escape",
            "tab" => "Tab",
            "backspace" => "Backspace",
            "delete" or "del" => "Delete",
            "insert" or "ins" => "Insert",
            "home" => "Home",
            "end" => "End",
            "pageup" or "pgup" => "PageUp",
            "pagedown" or "pgdn" => "PageDown",
            "up" => "Up",
            "down" => "Down",
            "left" => "Left",
            "right" => "Right",
            "printscreen" or "prtsc" or "print" => "PrintScreen",
            "pause" => "Pause",
            _ => null
        };
    }

    private static string ToXdgKey(string key) => key switch
    {
        "Space" => "space",
        "Enter" => "Return",
        "Escape" => "Escape",
        "Tab" => "Tab",
        "Backspace" => "BackSpace",
        "Delete" => "Delete",
        "Insert" => "Insert",
        "Home" => "Home",
        "End" => "End",
        "PageUp" => "Page_Up",
        "PageDown" => "Page_Down",
        "Up" => "Up",
        "Down" => "Down",
        "Left" => "Left",
        "Right" => "Right",
        "PrintScreen" => "Print",
        "Pause" => "Pause",
        _ when key.Length == 1 && char.IsLetter(key[0]) => key.ToLowerInvariant(),
        _ => key
    };

    private static string ToX11Key(string key) => key switch
    {
        "Space" => "space",
        "Enter" => "Return",
        "Escape" => "Escape",
        "Tab" => "Tab",
        "Backspace" => "BackSpace",
        "Delete" => "Delete",
        "Insert" => "Insert",
        "PageUp" => "Page_Up",
        "PageDown" => "Page_Down",
        "PrintScreen" => "Print",
        _ => key
    };

    private static uint ToWindowsVirtualKey(string key)
    {
        if (key.Length == 1)
        {
            var c = key[0];
            if (char.IsLetterOrDigit(c))
                return char.ToUpperInvariant(c);
        }

        if (key.StartsWith('F') && int.TryParse(key[1..], out var function) && function is >= 1 and <= 24)
            return (uint)(0x70 + function - 1);

        return key switch
        {
            "Backspace" => 0x08,
            "Tab" => 0x09,
            "Enter" => 0x0D,
            "Pause" => 0x13,
            "Escape" => 0x1B,
            "Space" => 0x20,
            "PageUp" => 0x21,
            "PageDown" => 0x22,
            "End" => 0x23,
            "Home" => 0x24,
            "Left" => 0x25,
            "Up" => 0x26,
            "Right" => 0x27,
            "Down" => 0x28,
            "PrintScreen" => 0x2C,
            "Insert" => 0x2D,
            "Delete" => 0x2E,
            _ => 0
        };
    }
}

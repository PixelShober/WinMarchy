namespace WinMarchy.Core.Hotkeys;

[Flags]
public enum Mods
{
    None = 0,
    Win = 1,
    Ctrl = 2,
    Alt = 4,
    Shift = 8,
}

/// <summary>
/// A key combination like "Win+Shift+Enter". Punctuation keys are named after their position on a US
/// keyboard ("Minus" is the key right of 0, "ß" on a German one), the way Omarchy binds them by key code,
/// so the fingers do the same thing on Linux and Windows.
/// </summary>
public sealed record KeyCombo(Mods Mods, string Key)
{
    /// <summary>Physical keys by scan code (US names).</summary>
    public static readonly IReadOnlyDictionary<string, int> ScanCodes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["Grave"] = 0x29, ["Minus"] = 0x0C, ["Equal"] = 0x0D, ["LeftBracket"] = 0x1A, ["RightBracket"] = 0x1B,
        ["Backslash"] = 0x2B, ["Semicolon"] = 0x27, ["Quote"] = 0x28, ["Comma"] = 0x33, ["Period"] = 0x34,
        ["Slash"] = 0x35, ["IntlBackslash"] = 0x56,
    };

    /// <summary>Named keys: AutoHotkey name, GlazeWM name.</summary>
    static readonly Dictionary<string, (string Ahk, string Glaze)> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = ("Enter", "enter"), ["Space"] = ("Space", "space"), ["Tab"] = ("Tab", "tab"),
        ["Escape"] = ("Escape", "escape"), ["Backspace"] = ("Backspace", "back"), ["Delete"] = ("Delete", "delete"),
        ["Insert"] = ("Insert", "insert"), ["Home"] = ("Home", "home"), ["End"] = ("End", "end"),
        ["PageUp"] = ("PgUp", "page_up"), ["PageDown"] = ("PgDn", "page_down"),
        ["Left"] = ("Left", "left"), ["Right"] = ("Right", "right"), ["Up"] = ("Up", "up"), ["Down"] = ("Down", "down"),
        ["PrintScreen"] = ("PrintScreen", "print_screen"),
        ["NumpadAdd"] = ("NumpadAdd", "add"), ["NumpadSub"] = ("NumpadSub", "subtract"),
        ["WheelUp"] = ("WheelUp", ""), ["WheelDown"] = ("WheelDown", ""),
        // Media keys: mostly sent (send:MediaPlayPause), but they may also start a shortcut on their own.
        ["MediaPlayPause"] = ("Media_Play_Pause", "media_play_pause"), ["MediaNext"] = ("Media_Next", "media_next_track"),
        ["MediaPrev"] = ("Media_Prev", "media_prev_track"), ["MediaStop"] = ("Media_Stop", "media_stop"),
        ["VolumeMute"] = ("Volume_Mute", "volume_mute"), ["VolumeUp"] = ("Volume_Up", "volume_up"), ["VolumeDown"] = ("Volume_Down", "volume_down"),
    };

    /// <summary>Keys that work without a modifier: Print, F13 to F24 and the media keys.</summary>
    static bool StandsAlone(string key) =>
        key == "PrintScreen" || key.StartsWith("Media", StringComparison.Ordinal) || key.StartsWith("Volume", StringComparison.Ordinal) ||
        (key.StartsWith('F') && int.TryParse(key[1..], out var f) && f >= 13);

    static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Return"] = "Enter", ["Esc"] = "Escape", ["Back"] = "Backspace", ["Del"] = "Delete", ["PgUp"] = "PageUp",
        ["PgDn"] = "PageDown", ["Print"] = "PrintScreen", ["`"] = "Grave", ["-"] = "Minus", ["="] = "Equal",
        ["/"] = "Slash", [","] = "Comma", ["."] = "Period", [";"] = "Semicolon", ["'"] = "Quote",
        ["["] = "LeftBracket", ["]"] = "RightBracket", ["\\"] = "Backslash", ["Plus"] = "Equal",
    };

    public bool IsWheel => Key is "WheelUp" or "WheelDown";

    public bool IsPhysical => ScanCodes.ContainsKey(Key);

    /// <summary>"Win+Ctrl+Alt+Shift+Key", always in this order.</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if (Mods.HasFlag(Mods.Win)) parts.Add("Win");
        if (Mods.HasFlag(Mods.Ctrl)) parts.Add("Ctrl");
        if (Mods.HasFlag(Mods.Alt)) parts.Add("Alt");
        if (Mods.HasFlag(Mods.Shift)) parts.Add("Shift");
        parts.Add(Key);
        return string.Join('+', parts);
    }

    public static bool TryParse(string? text, out KeyCombo combo, out string error)
    {
        combo = new KeyCombo(Mods.None, "");
        error = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            error = L.T("hotkeys.errors.empty");
            return false;
        }
        // "+" is also a key name (Plus); split on "+" but keep a trailing "++".
        var raw = text.Trim();
        var parts = raw.EndsWith("++") ? raw[..^2].Split('+').Append("Plus").ToList() : raw.Split('+').ToList();
        parts = parts.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (parts.Count == 0)
        {
            error = L.T("hotkeys.errors.empty");
            return false;
        }

        var mods = Mods.None;
        foreach (var m in parts.Take(parts.Count - 1))
        {
            var mod = m.ToLowerInvariant() switch
            {
                "win" or "super" or "lwin" or "meta" => Mods.Win,
                "ctrl" or "control" or "strg" => Mods.Ctrl,
                "alt" => Mods.Alt,
                "shift" or "umschalt" => Mods.Shift,
                _ => (Mods?)null,
            };
            if (mod == null)
            {
                error = L.T("hotkeys.errors.unknownModifier", ("modifier", m), ("combo", raw));
                return false;
            }
            if (mods.HasFlag(mod.Value))
            {
                error = L.T("hotkeys.errors.duplicateModifier", ("combo", raw));
                return false;
            }
            mods |= mod.Value;
        }

        var key = NormalizeKey(parts[^1]);
        if (key == null)
        {
            error = L.T("hotkeys.errors.unknownKey", ("key", parts[^1]), ("combo", raw));
            return false;
        }
        if (mods == Mods.None && !StandsAlone(key))
        {
            error = L.T("hotkeys.errors.needsModifier", ("combo", raw));
            return false;
        }
        combo = new KeyCombo(mods, key);
        return true;
    }

    public static KeyCombo Parse(string text) =>
        TryParse(text, out var combo, out var error) ? combo : throw new FormatException(error);

    static string? NormalizeKey(string key)
    {
        if (Aliases.TryGetValue(key, out var alias)) key = alias;
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])) return key.ToUpperInvariant();
        if (key.Length >= 2 && (key[0] is 'F' or 'f') && int.TryParse(key[1..], out var n) && n is >= 1 and <= 24) return "F" + n;
        if (ScanCodes.Keys.FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)) is { } sc) return sc;
        if (Named.Keys.FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)) is { } named) return named;
        return null;
    }

    /// <summary>Reasons a combination cannot work on Windows; null when it is fine.</summary>
    public string? ReservedReason()
    {
        if (Mods == Mods.Win && Key == "L") return L.T("hotkeys.reserved.winL");
        if (Mods == (Mods.Ctrl | Mods.Alt) && Key == "Delete") return L.T("hotkeys.reserved.ctrlAltDel");
        return null;
    }

    // ------------------------------------------------------------------ AutoHotkey

    /// <summary>AutoHotkey v2 hotkey name, e.g. "#+Enter", "#^SC00C", "#WheelDown". "$" forces the keyboard hook.</summary>
    public string ToAhk()
    {
        var prefix = "";
        if (Mods.HasFlag(Mods.Win)) prefix += "#";
        if (Mods.HasFlag(Mods.Ctrl)) prefix += "^";
        if (Mods.HasFlag(Mods.Alt)) prefix += "!";
        if (Mods.HasFlag(Mods.Shift)) prefix += "+";
        return (IsWheel ? "" : "$") + prefix + AhkKey;
    }

    /// <summary>The key part of an AutoHotkey Send string, e.g. "{Enter}", "{SC00C}", "v".</summary>
    public string ToAhkSend()
    {
        var prefix = "";
        if (Mods.HasFlag(Mods.Win)) prefix += "#";
        if (Mods.HasFlag(Mods.Ctrl)) prefix += "^";
        if (Mods.HasFlag(Mods.Alt)) prefix += "!";
        if (Mods.HasFlag(Mods.Shift)) prefix += "+";
        return prefix + "{" + AhkKey + "}";
    }

    string AhkKey =>
        ScanCodes.TryGetValue(Key, out var sc) ? $"SC{sc:X3}" :
        Named.TryGetValue(Key, out var n) ? n.Ahk :
        Key.ToLowerInvariant();

    // ------------------------------------------------------------------ GlazeWM

    /// <summary>
    /// GlazeWM binding, e.g. "lwin+shift+enter". GlazeWM names punctuation by virtual key, so physical
    /// keys go through the active keyboard layout. Null when GlazeWM cannot bind the key (mouse wheel).
    /// </summary>
    public string? ToGlaze(IKeyboardLayout layout)
    {
        string key;
        if (ScanCodes.TryGetValue(Key, out var sc))
        {
            var vk = layout.VirtualKeyFromScanCode(sc);
            if (!GlazeOemNames.TryGetValue(vk, out var name)) return null;
            key = name;
        }
        else if (Named.TryGetValue(Key, out var n))
        {
            if (n.Glaze.Length == 0) return null;
            key = n.Glaze;
        }
        else key = Key.ToLowerInvariant();

        var parts = new List<string>();
        if (Mods.HasFlag(Mods.Win)) parts.Add("lwin");
        if (Mods.HasFlag(Mods.Ctrl)) parts.Add("ctrl");
        if (Mods.HasFlag(Mods.Alt)) parts.Add("alt");
        if (Mods.HasFlag(Mods.Shift)) parts.Add("shift");
        parts.Add(key);
        return string.Join('+', parts);
    }

    static readonly Dictionary<int, string> GlazeOemNames = new()
    {
        [0xBA] = "oem_semicolon", [0xBB] = "oem_plus", [0xBC] = "oem_comma", [0xBD] = "oem_minus", [0xBE] = "oem_period",
        [0xBF] = "oem_question", [0xC0] = "oem_tilde", [0xDB] = "oem_open_brackets", [0xDC] = "oem_pipe",
        [0xDD] = "oem_close_brackets", [0xDE] = "oem_quotes", [0xDF] = "oem_8", [0xE2] = "oem_102",
    };

    /// <summary>Label for people: "Win + Shift + Enter", with the character the active layout prints for physical keys.</summary>
    public string Display(IKeyboardLayout layout)
    {
        var parts = new List<string>();
        if (Mods.HasFlag(Mods.Win)) parts.Add("Win");
        if (Mods.HasFlag(Mods.Ctrl)) parts.Add(L.Language == "de" ? "Strg" : "Ctrl");
        if (Mods.HasFlag(Mods.Alt)) parts.Add("Alt");
        if (Mods.HasFlag(Mods.Shift)) parts.Add(L.Language == "de" ? "Umschalt" : "Shift");
        parts.Add(KeyLabel(layout));
        return string.Join(" + ", parts);
    }

    public string KeyLabel(IKeyboardLayout layout)
    {
        if (ScanCodes.TryGetValue(Key, out var sc) && layout.CharFromScanCode(sc) is { } ch) return ch;
        return Key switch
        {
            "Enter" => L.Language == "de" ? "Eingabe" : "Enter",
            "Space" => L.Language == "de" ? "Leertaste" : "Space",
            "Escape" => "Esc",
            "Backspace" => L.Language == "de" ? "Rücktaste" : "Backspace",
            "Delete" => L.Language == "de" ? "Entf" : "Del",
            "Left" => "←", "Right" => "→", "Up" => "↑", "Down" => "↓",
            "PageUp" => L.Language == "de" ? "Bild ↑" : "PgUp",
            "PageDown" => L.Language == "de" ? "Bild ↓" : "PgDn",
            "Home" => L.Language == "de" ? "Pos1" : "Home",
            "PrintScreen" => L.Language == "de" ? "Druck" : "PrtSc",
            "WheelUp" => L.Language == "de" ? "Mausrad ↑" : "Wheel ↑",
            "WheelDown" => L.Language == "de" ? "Mausrad ↓" : "Wheel ↓",
            "NumpadAdd" => "Num +",
            "NumpadSub" => "Num −",
            _ => Key,
        };
    }
}

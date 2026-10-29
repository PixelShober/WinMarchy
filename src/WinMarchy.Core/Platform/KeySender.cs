using System.Runtime.InteropServices;
using WinMarchy.Core.Hotkeys;

namespace WinMarchy.Core.Platform;

/// <summary>
/// Presses a key combination with SendInput. Modifiers the user still holds from the shortcut are
/// released first (otherwise Win+Ctrl+V would arrive as Win+Ctrl+V instead of Win+V), and before a
/// Win key goes up an unassigned key (0xE8) is tapped so the Start menu stays closed – the same trick
/// AutoHotkey uses.
/// </summary>
public static class KeySender
{
    const ushort VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LMENU = 0xA4, VK_RMENU = 0xA5,
        VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_MASK = 0xE8;

    static readonly Dictionary<string, ushort> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = 0x0D, ["Space"] = 0x20, ["Tab"] = 0x09, ["Escape"] = 0x1B, ["Backspace"] = 0x08, ["Delete"] = 0x2E,
        ["Insert"] = 0x2D, ["Home"] = 0x24, ["End"] = 0x23, ["PageUp"] = 0x21, ["PageDown"] = 0x22, ["Left"] = 0x25,
        ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28, ["PrintScreen"] = 0x2C, ["NumpadAdd"] = 0x6B, ["NumpadSub"] = 0x6D,
    };

    public static void Send(KeyCombo combo)
    {
        if (!OperatingSystem.IsWindows()) return;
        var vk = VirtualKey(combo);
        if (vk == 0) return;

        var inputs = new List<INPUT>();
        bool maskQueued = false;
        void Mask()
        {
            if (maskQueued) return;
            inputs.Add(Key(VK_MASK, false));
            inputs.Add(Key(VK_MASK, true));
            maskQueued = true;
        }

        var wanted = new Dictionary<ushort, bool>
        {
            [VK_LWIN] = combo.Mods.HasFlag(Mods.Win), [VK_LCONTROL] = combo.Mods.HasFlag(Mods.Ctrl),
            [VK_LMENU] = combo.Mods.HasFlag(Mods.Alt), [VK_LSHIFT] = combo.Mods.HasFlag(Mods.Shift),
        };
        var held = new (ushort Vk, ushort Logical)[]
        {
            (VK_LWIN, VK_LWIN), (VK_RWIN, VK_LWIN), (VK_LCONTROL, VK_LCONTROL), (VK_RCONTROL, VK_LCONTROL),
            (VK_LMENU, VK_LMENU), (VK_RMENU, VK_LMENU), (VK_LSHIFT, VK_LSHIFT), (VK_RSHIFT, VK_LSHIFT),
        };

        // 1. Let go of modifiers that are down but not part of the combination.
        var alreadyDown = new HashSet<ushort>();
        foreach (var (key, logical) in held)
        {
            if ((GetAsyncKeyState(key) & 0x8000) == 0) continue;
            if (wanted[logical]) { alreadyDown.Add(logical); continue; }
            if (logical == VK_LWIN || logical == VK_LMENU) Mask();
            inputs.Add(Key(key, true));
        }
        // 2. Press the missing ones, 3. the key, 4. release what we pressed.
        var pressed = wanted.Where(w => w.Value && !alreadyDown.Contains(w.Key)).Select(w => w.Key).ToList();
        foreach (var m in pressed) inputs.Add(Key(m, false));
        inputs.Add(Key(vk, false, Extended(vk)));
        inputs.Add(Key(vk, true, Extended(vk)));
        maskQueued = false;
        foreach (var m in Enumerable.Reverse(pressed))
        {
            if (m == VK_LWIN || m == VK_LMENU) Mask();
            inputs.Add(Key(m, true));
        }
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    static ushort VirtualKey(KeyCombo c)
    {
        if (KeyCombo.ScanCodes.TryGetValue(c.Key, out var sc)) return (ushort)KeyboardLayouts.Current.VirtualKeyFromScanCode(sc);
        if (Named.TryGetValue(c.Key, out var vk)) return vk;
        if (c.Key.Length == 1) return c.Key[0];                     // A–Z and 0–9 are their own codes
        if (c.Key.StartsWith('F') && int.TryParse(c.Key[1..], out var f)) return (ushort)(0x70 + f - 1);
        return 0;
    }

    static bool Extended(ushort vk) => vk is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E;

    static INPUT Key(ushort vk, bool up, bool extended = false) => new()
    {
        type = 1,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = (up ? 0x0002u : 0) | (extended ? 0x0001u : 0) } },
    };

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // Only here so the union has the size Windows expects (MOUSEINPUT is the largest member).
    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vk);
}

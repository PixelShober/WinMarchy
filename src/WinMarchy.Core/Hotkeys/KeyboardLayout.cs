using System.Runtime.InteropServices;

namespace WinMarchy.Core.Hotkeys;

/// <summary>What a physical key means on the active keyboard layout.</summary>
public interface IKeyboardLayout
{
    /// <summary>Virtual key code of a scan code (VK_OEM_MINUS etc.).</summary>
    int VirtualKeyFromScanCode(int scanCode);

    /// <summary>The character printed on the key ("ß" for the key right of 0 on a German layout), or null.</summary>
    string? CharFromScanCode(int scanCode);
}

/// <summary>US layout: for tests and as fallback when Windows cannot tell.</summary>
public sealed class UsKeyboardLayout : IKeyboardLayout
{
    static readonly Dictionary<int, (int Vk, string Char)> Keys = new()
    {
        [0x29] = (0xC0, "`"), [0x0C] = (0xBD, "-"), [0x0D] = (0xBB, "="), [0x1A] = (0xDB, "["), [0x1B] = (0xDD, "]"),
        [0x2B] = (0xDC, "\\"), [0x27] = (0xBA, ";"), [0x28] = (0xDE, "'"), [0x33] = (0xBC, ","), [0x34] = (0xBE, "."),
        [0x35] = (0xBF, "/"), [0x56] = (0xE2, "\\"),
    };

    public int VirtualKeyFromScanCode(int scanCode) => Keys.TryGetValue(scanCode, out var k) ? k.Vk : 0;
    public string? CharFromScanCode(int scanCode) => Keys.TryGetValue(scanCode, out var k) ? k.Char : null;
}

/// <summary>German (QWERTZ) layout, for tests.</summary>
public sealed class GermanKeyboardLayout : IKeyboardLayout
{
    static readonly Dictionary<int, (int Vk, string Char)> Keys = new()
    {
        [0x29] = (0xDC, "^"), [0x0C] = (0xDB, "ß"), [0x0D] = (0xDD, "´"), [0x1A] = (0xBA, "ü"), [0x1B] = (0xBB, "+"),
        [0x2B] = (0xBF, "#"), [0x27] = (0xC0, "ö"), [0x28] = (0xDE, "ä"), [0x33] = (0xBC, ","), [0x34] = (0xBE, "."),
        [0x35] = (0xBD, "-"), [0x56] = (0xE2, "<"),
    };

    public int VirtualKeyFromScanCode(int scanCode) => Keys.TryGetValue(scanCode, out var k) ? k.Vk : 0;
    public string? CharFromScanCode(int scanCode) => Keys.TryGetValue(scanCode, out var k) ? k.Char : null;
}

/// <summary>The layout of the window in front (that is where the shortcuts are typed).</summary>
public sealed class WindowsKeyboardLayout : IKeyboardLayout
{
    const uint MAPVK_VSC_TO_VK_EX = 3;
    const uint MAPVK_VK_TO_CHAR = 2;

    static IntPtr Layout()
    {
        var thread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var hkl = GetKeyboardLayout(thread);
        return hkl != IntPtr.Zero ? hkl : GetKeyboardLayout(0);
    }

    public int VirtualKeyFromScanCode(int scanCode) => (int)MapVirtualKeyEx((uint)scanCode, MAPVK_VSC_TO_VK_EX, Layout());

    public string? CharFromScanCode(int scanCode)
    {
        var hkl = Layout();
        var vk = MapVirtualKeyEx((uint)scanCode, MAPVK_VSC_TO_VK_EX, hkl);
        if (vk == 0) return null;
        // Dead keys (´ ^ `) have the top bit set; the character is in the low word.
        var ch = MapVirtualKeyEx(vk, MAPVK_VK_TO_CHAR, hkl) & 0xFFFF;
        return ch == 0 ? null : char.ToLowerInvariant((char)ch).ToString();
    }

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyExW")] static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr hkl);
}

public static class KeyboardLayouts
{
    public static IKeyboardLayout Current { get; set; } =
        OperatingSystem.IsWindows() ? new WindowsKeyboardLayout() : new UsKeyboardLayout();
}

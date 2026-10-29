using System.Runtime.InteropServices;

namespace WinMarchy.Core.Platform;

/// <summary>
/// Hides the Windows taskbar the way the Winarchy Lite script did (ShowWindow on Shell_TrayWnd and
/// every Shell_SecondaryTrayWnd), plus auto-hide through the official AppBar API so Windows gives the
/// space back to the windows. No Explorer patches, no Windhawk; the original auto-hide setting is
/// remembered and put back.
/// </summary>
public static class Taskbar
{
    const int SW_HIDE = 0, SW_SHOWNA = 8;
    const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10;
    const int ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;

    static IEnumerable<IntPtr> Windows()
    {
        var primary = FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero) yield return primary;
        var prev = IntPtr.Zero;
        while ((prev = FindWindowEx(IntPtr.Zero, prev, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) yield return prev;
    }

    public static bool IsAutoHide()
    {
        if (!OperatingSystem.IsWindows()) return false;
        var data = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = FindWindow("Shell_TrayWnd", null) };
        return ((int)SHAppBarMessage(ABM_GETSTATE, ref data) & ABS_AUTOHIDE) != 0;
    }

    public static void SetAutoHide(bool on)
    {
        if (!OperatingSystem.IsWindows()) return;
        var data = new APPBARDATA
        {
            cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
            hWnd = FindWindow("Shell_TrayWnd", null),
            lParam = (IntPtr)(on ? ABS_AUTOHIDE : ABS_ALWAYSONTOP),
        };
        SHAppBarMessage(ABM_SETSTATE, ref data);
    }

    /// <summary>Called by the agent twice a second: Explorer shows a recreated taskbar again after restarts.</summary>
    public static void EnforceHidden()
    {
        if (!OperatingSystem.IsWindows()) return;
        foreach (var h in Windows())
            if (IsWindowVisible(h)) ShowWindow(h, SW_HIDE);
    }

    public static void Show()
    {
        if (!OperatingSystem.IsWindows()) return;
        foreach (var h in Windows()) ShowWindow(h, SW_SHOWNA);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint message, ref APPBARDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
}

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

    /// <summary>The process that owns the taskbar (Explorer), or 0.</summary>
    internal static uint ShellProcess()
    {
        var tray = FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(tray, out var pid);
        return pid;
    }

    internal static bool IsTaskbarWindow(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(64);
        if (GetClassName(hwnd, sb, sb.Capacity) == 0) return false;
        var name = sb.ToString();
        return name is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);

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

/// <summary>
/// Keeps the taskbar hidden from a thread of its own: Explorer shows it again after a theme or colour
/// change, a restart, or when the mouse touches the screen edge with auto-hide on. A window event hook
/// hides it the moment it appears, a timer twice a second catches the rest. Neither waits for the UI
/// thread, which may be busy with a theme change.
/// </summary>
public sealed class TaskbarGuard : IDisposable
{
    const uint EVENT_OBJECT_SHOW = 0x8002, WINEVENT_OUTOFCONTEXT = 0, WM_TIMER = 0x0113, WM_QUIT = 0x0012;

    readonly Thread _thread;
    volatile bool _hide;
    uint _threadId;
    IntPtr _hook;
    uint _hookedPid;
    WinEventProc? _proc;

    public TaskbarGuard()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "WinMarchy taskbar" };
        if (OperatingSystem.IsWindows()) _thread.Start();
    }

    /// <summary>Whether the taskbar should be hidden right now.</summary>
    public bool Hide
    {
        get => _hide;
        set => _hide = value;
    }

    void Run()
    {
        _threadId = GetCurrentThreadId();
        _proc = OnEvent;
        SetTimer(IntPtr.Zero, IntPtr.Zero, 500, IntPtr.Zero);
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_TIMER) Tick();
            DispatchMessage(ref msg);
        }
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
    }

    void Tick()
    {
        // Explorer restarted: the hook has to follow it to its new process.
        var pid = Taskbar.ShellProcess();
        if (pid != 0 && pid != _hookedPid)
        {
            if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
            _hook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _proc!, pid, 0, WINEVENT_OUTOFCONTEXT);
            _hookedPid = pid;
        }
        if (_hide)
        {
            try { Taskbar.EnforceHidden(); } catch { }
        }
    }

    void OnEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (!_hide || idObject != 0 || hwnd == IntPtr.Zero) return;
        try
        {
            if (Taskbar.IsTaskbarWindow(hwnd)) Taskbar.EnforceHidden();
        }
        catch { }
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }

    delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int x, y;
    }

    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern IntPtr SetTimer(IntPtr hwnd, IntPtr id, uint ms, IntPtr proc);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
}

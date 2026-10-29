using System.Runtime.InteropServices;
using WinMarchy.App.Agent;
using WinMarchy.Core.Hotkeys;

namespace WinMarchy.App.Startup;

/// <summary>
/// A second WinMarchy.exe (started by AutoHotkey's fallback, GlazeWM, the bar or a double click) hands
/// its job to the running one through the agent window and exits. The same message AutoHotkey sends.
/// </summary>
static class AgentClient
{
    /// <returns>true when a running WinMarchy took it.</returns>
    public static bool Forward(StartArgs args, int waitMs = 0)
    {
        var deadline = Environment.TickCount64 + waitMs;
        do
        {
            var hwnd = FindWindow(null, AhkScript.AgentTitle);
            if (hwnd != IntPtr.Zero)
            {
                // Autostart while WinMarchy already runs: nothing to hand over.
                if (args.Action == null && args.Background) return true;
                // The running WinMarchy may bring a window to the front (menu, main window).
                GetWindowThreadProcessId(hwnd, out var pid);
                AllowSetForegroundWindow(pid);
                return args.Action != null
                    ? Send(hwnd, AgentHost.ActionMessage, args.Action)
                    : Send(hwnd, AgentHost.OpenMessage, args.Page ?? "");
            }
            if (waitMs > 0) Thread.Sleep(250);
        } while (Environment.TickCount64 < deadline);
        return false;
    }

    static bool Send(IntPtr hwnd, long kind, string text)
    {
        var data = Marshal.StringToHGlobalUni(text);
        try
        {
            var cds = new COPYDATASTRUCT { dwData = new IntPtr(kind), cbData = (text.Length + 1) * 2, lpData = data };
            var ok = SendMessageTimeout(hwnd, WM_COPYDATA, IntPtr.Zero, ref cds, SMTO_ABORTIFHUNG, 3000, out var result);
            return ok != IntPtr.Zero && result == new IntPtr(1);
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }

    const int WM_COPYDATA = 0x004A;
    const uint SMTO_ABORTIFHUNG = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(uint pid);

    [DllImport("user32.dll")]
    static extern IntPtr SendMessageTimeout(IntPtr hwnd, int msg, IntPtr wParam, ref COPYDATASTRUCT lParam, uint flags, uint timeout, out IntPtr result);
}

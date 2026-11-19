using System.Runtime.InteropServices;

namespace WinMarchy.Core.Platform;

/// <summary>
/// Display scaling one step up or down for the monitor under the mouse (Omarchy's SUPER+/ ).
/// Windows' settings page uses the same, undocumented DisplayConfig request (type -3/-4, scale relative
/// to the recommended value). Per user, no admin rights.
/// </summary>
public static class DisplayScale
{
    static readonly int[] Steps = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };
    const int GET_DPI_SCALE = -3, SET_DPI_SCALE = -4;

    /// <returns>The new scale in percent, or null if Windows refused.</returns>
    public static int? Step(int direction)
    {
        if (!OperatingSystem.IsWindows()) return null;
        GetCursorPos(out var pt);
        var monitor = MonitorFromPoint(pt, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfo(monitor, ref info)) return null;

        foreach (var path in DisplayConfig.ActivePaths())
        {
            if (!string.Equals(DisplayConfig.GdiName(path), info.szDevice, StringComparison.OrdinalIgnoreCase)) continue;

            var get = new DPI_GET { header = new DisplayConfig.HEADER { type = GET_DPI_SCALE, size = Marshal.SizeOf<DPI_GET>(), adapterId = path.sourceAdapter, id = path.sourceId } };
            if (DisplayConfigGetDeviceInfo(ref get) != 0) return null;
            int recommended = Math.Abs(get.minScaleRel);
            int target = Math.Clamp(get.curScaleRel + Math.Sign(direction), get.minScaleRel, get.maxScaleRel);
            if (recommended + target < 0 || recommended + target >= Steps.Length) return null;
            if (target != get.curScaleRel)
            {
                var set = new DPI_SET { header = new DisplayConfig.HEADER { type = SET_DPI_SCALE, size = Marshal.SizeOf<DPI_SET>(), adapterId = path.sourceAdapter, id = path.sourceId }, scaleRel = target };
                if (DisplayConfigSetDeviceInfo(ref set) != 0) return null;
            }
            return Steps[recommended + target];
        }
        return null;
    }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DPI_GET
    {
        public DisplayConfig.HEADER header;
        public int minScaleRel;
        public int curScaleRel;
        public int maxScaleRel;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DPI_SET
    {
        public DisplayConfig.HEADER header;
        public int scaleRel;
    }

    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DPI_GET request);
    [DllImport("user32.dll")] static extern int DisplayConfigSetDeviceInfo(ref DPI_SET request);
}

/// <summary>Keeping the display awake (Omarchy's idle-lock toggle) and the battery state.</summary>
public static class Power
{
    const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x1, ES_DISPLAY_REQUIRED = 0x2;

    /// <summary>Must be called on a thread that lives as long as the setting should hold (the UI thread).</summary>
    public static void KeepAwake(bool on)
    {
        if (!OperatingSystem.IsWindows()) return;
        SetThreadExecutionState(on ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED : ES_CONTINUOUS);
    }

    /// <summary>Percent and whether it is charging; null without battery.</summary>
    public static (int Percent, bool Charging, TimeSpan? Remaining)? Battery()
    {
        if (!OperatingSystem.IsWindows() || !GetSystemPowerStatus(out var s) || s.BatteryFlag == 128 || s.BatteryLifePercent == 255) return null;
        TimeSpan? left = s.BatteryLifeTime >= 0 ? TimeSpan.FromSeconds(s.BatteryLifeTime) : null;
        return (s.BatteryLifePercent, s.ACLineStatus == 1, left);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
    [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}

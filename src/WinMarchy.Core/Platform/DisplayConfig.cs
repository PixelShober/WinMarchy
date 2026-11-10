using System.Runtime.InteropServices;

namespace WinMarchy.Core.Platform;

/// <summary>
/// Windows' DisplayConfig API: the active display paths, and for each its GDI name ("\\.\DISPLAY1"),
/// the name the screen gives itself ("DELL U2720Q") and whether it is built in (a laptop's own panel).
/// </summary>
internal static class DisplayConfig
{
    const uint QDC_ONLY_ACTIVE_PATHS = 2;
    internal const int GET_SOURCE_NAME = 1, GET_TARGET_NAME = 2;

    /// <summary>Connection types of a panel inside the device: internal, embedded DisplayPort, embedded UDI, LVDS.</summary>
    static readonly uint[] BuiltIn = { 0x80000000, 11, 13, 6 };

    /// <summary>The active paths, or an empty list where Windows does not tell.</summary>
    internal static List<DISPLAYCONFIG_PATH_INFO> ActivePaths()
    {
        if (!OperatingSystem.IsWindows()) return new List<DISPLAYCONFIG_PATH_INFO>();
        if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != 0) return new List<DISPLAYCONFIG_PATH_INFO>();
        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new byte[modeCount * 64];   // DISPLAYCONFIG_MODE_INFO is 64 bytes; its content is not needed
        if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return new List<DISPLAYCONFIG_PATH_INFO>();
        return paths.Take((int)pathCount).ToList();
    }

    /// <summary>The GDI name of a path's source ("\\.\DISPLAY1"), as Windows Forms' Screen.DeviceName has it.</summary>
    internal static string? GdiName(DISPLAYCONFIG_PATH_INFO path)
    {
        var name = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            header = new HEADER { type = GET_SOURCE_NAME, size = Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(), adapterId = path.sourceAdapter, id = path.sourceId },
        };
        return DisplayConfigGetDeviceInfo(ref name) == 0 ? name.viewGdiDeviceName : null;
    }

    /// <summary>The screens of the active paths by GDI name: their own name (null if they give none) and whether they are built in.</summary>
    internal static Dictionary<string, (string? Name, bool BuiltIn)> Screens()
    {
        var result = new Dictionary<string, (string?, bool)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in ActivePaths())
        {
            if (GdiName(path) is not { } gdi || result.ContainsKey(gdi)) continue;
            var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME
            {
                header = new HEADER { type = GET_TARGET_NAME, size = Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(), adapterId = path.targetAdapter, id = path.targetId },
            };
            bool known = DisplayConfigGetDeviceInfo(ref target) == 0;
            var technology = known ? target.outputTechnology : (uint)path.outputTechnology;
            var name = known && !string.IsNullOrWhiteSpace(target.monitorFriendlyDeviceName) ? target.monitorFriendlyDeviceName.Trim() : null;
            result[gdi] = (name, BuiltIn.Contains(technology));
        }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)] internal struct LUID { public uint Low; public int High; }

    /// <summary>DISPLAYCONFIG_PATH_INFO (72 bytes), flattened.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_PATH_INFO
    {
        public LUID sourceAdapter;
        public uint sourceId;
        public uint sourceModeIdx;
        public uint sourceStatus;
        public LUID targetAdapter;
        public uint targetId;
        public uint targetModeIdx;
        public int outputTechnology;
        public int rotation;
        public int scaling;
        public uint refreshNumerator;
        public uint refreshDenominator;
        public int scanLineOrdering;
        public int targetAvailable;
        public uint targetStatus;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HEADER
    {
        public int type;
        public int size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }

    /// <summary>DISPLAYCONFIG_TARGET_DEVICE_NAME (420 bytes).</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] DISPLAYCONFIG_PATH_INFO[] paths, ref uint modeCount, [Out] byte[] modes, IntPtr topology);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME request);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME request);
}

/// <summary>What the Tiling page and the work modes call the screens: "Laptop-Bildschirm", "DELL U2720Q".</summary>
public static class ScreenNames
{
    /// <summary>
    /// The names of the screens with these GDI names ("\\.\DISPLAY1"), in the same order (left to
    /// right). Where Windows tells nothing, "Screen 2" by position.
    /// </summary>
    public static List<string> Of(IReadOnlyList<(string Gdi, bool Portrait)> screens)
    {
        Dictionary<string, (string? Name, bool BuiltIn)> known;
        try
        {
            known = DisplayConfig.Screens();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            Log.Debug("screen names: " + ex.Message);
            known = new Dictionary<string, (string?, bool)>();
        }
        return Label(screens.Select(g => known.TryGetValue(g.Gdi, out var s) ? (s.Name, s.BuiltIn, g.Portrait) : (null, false, g.Portrait)).ToList());
    }

    /// <summary>
    /// A built-in panel is the laptop's screen, any other goes by its own name, else by its number. A
    /// screen turned upright gets "(Hochkant)". Two that are still called the same (two monitors of
    /// one model) get where they stand: "(links)" and "(rechts)", for three also "(Mitte)".
    /// </summary>
    /// <param name="screens">The screens from left to right.</param>
    internal static List<string> Label(IReadOnlyList<(string? Name, bool BuiltIn, bool Portrait)> screens)
    {
        var names = screens.Select((s, i) => s.BuiltIn ? L.T("screens.builtIn") : s.Name ?? L.T("tiling.monitorName", ("number", i + 1))).ToList();
        var notes = screens.Select(s => s.Portrait ? new List<string> { L.T("screens.portrait") } : new List<string>()).ToList();
        foreach (var group in names.Select((n, i) => (Key: n + "|" + string.Join(",", notes[i]), i)).GroupBy(x => x.Key).Where(g => g.Count() > 1).ToList())
        {
            var members = group.Select(x => x.i).ToList();
            for (int k = 0; k < members.Count; k++) notes[members[k]].Add(Position(k, members.Count)!);
        }
        return names.Select((n, i) => notes[i].Count == 0 ? n : $"{n} ({string.Join(", ", notes[i])})").ToList();
    }

    /// <summary>
    /// Where the screen at <paramref name="index"/> of <paramref name="count"/> stands, counted from the
    /// left: "links", "Mitte", "rechts", or "3. von links" from four on; null for a single screen.
    /// </summary>
    public static string? Position(int index, int count) => count switch
    {
        <= 1 => null,
        2 => L.T(index == 0 ? "screens.left" : "screens.right"),
        3 => L.T(index switch { 0 => "screens.left", 1 => "screens.middle", _ => "screens.right" }),
        _ => L.T("screens.nthFromLeft", ("number", index + 1)),
    };
}

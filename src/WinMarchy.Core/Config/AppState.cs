namespace WinMarchy.Core;

/// <summary>C:\WinMarchy\Config\state.json – what WinMarchy remembers about its own changes.</summary>
public sealed class AppState
{
    /// <summary>Programs WinMarchy installed itself (they can be removed again from "Undo").</summary>
    public List<string> InstalledTools { get; set; } = new();

    /// <summary>The release tag each program was installed from (for "update programs").</summary>
    public Dictionary<string, string> ToolTags { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The vendor's latest release per program, as last looked up (Start page, program updates).</summary>
    public Dictionary<string, Tools.LatestRelease> LatestReleases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The names of the screens by index (0 = leftmost) as they were when last connected, for the Tiling page.</summary>
    public List<string> ScreenNames { get; set; } = new();

    /// <summary>Folders of removed programs that were still in use (a DLL Explorer had loaded); deleted at the next start.</summary>
    public List<string> LeftoverDirs { get; set; } = new();

    /// <summary>Was the Windows taskbar on auto-hide before WinMarchy hid it? null = never touched.</summary>
    public bool? TaskbarAutoHideBefore { get; set; }

    /// <summary>Wallpaper position per theme for "next background".</summary>
    public Dictionary<string, int> WallpaperIndex { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The WinMarchy version that last wrote the generated files (GlazeWM's config, the hotkey script,
    /// the bar's config). A newer version writes them again at its first start.
    /// </summary>
    public string? GeneratedBy { get; set; }

    /// <summary>Where Fluent Search keeps its settings on this PC, as WinMarchy found out by starting it once.</summary>
    public string? FluentSearchSettingsDir { get; set; }

    /// <summary>First complete setup finished.</summary>
    public bool SetupDone { get; set; }

    public DateTime? LastApplied { get; set; }

    static readonly object Sync = new();

    public static AppState Load()
    {
        lock (Sync)
        {
            try
            {
                if (File.Exists(Paths.StateFile)) return Json.Deserialize<AppState>(File.ReadAllText(Paths.StateFile)) ?? new AppState();
            }
            catch (Exception ex)
            {
                Log.Warn("state.json: " + ex.Message);
            }
            return new AppState();
        }
    }

    public void Save()
    {
        lock (Sync) FileSafety.WriteText(Paths.StateFile, Json.Serialize(this, indented: true));
    }

    /// <summary>Load, change, save in one go.</summary>
    public static void Update(Action<AppState> change)
    {
        lock (Sync)
        {
            var s = Load();
            change(s);
            s.Save();
        }
    }
}

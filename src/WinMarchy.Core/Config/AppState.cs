namespace WinMarchy.Core;

/// <summary>C:\WinMarchy\Config\state.json – what WinMarchy remembers about its own changes.</summary>
public sealed class AppState
{
    /// <summary>Programs WinMarchy installed itself (they can be removed again from "Undo").</summary>
    public List<string> InstalledTools { get; set; } = new();

    /// <summary>Folders of removed programs that were still in use (a DLL Explorer had loaded); deleted at the next start.</summary>
    public List<string> LeftoverDirs { get; set; } = new();

    /// <summary>Was the Windows taskbar on auto-hide before WinMarchy hid it? null = never touched.</summary>
    public bool? TaskbarAutoHideBefore { get; set; }

    /// <summary>Wallpaper position per theme for "next background".</summary>
    public Dictionary<string, int> WallpaperIndex { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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

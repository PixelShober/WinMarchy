namespace WinMarchy.Core;

/// <summary>Which programs a theme change restyles.</summary>
public sealed class ThemeTargets
{
    public bool GlazeWm { get; set; } = true;
    public bool Zebar { get; set; } = true;
    public bool Terminal { get; set; } = true;
    public bool PowerShell { get; set; } = true;
    public bool VsCode { get; set; } = true;
    public bool Flow { get; set; } = true;
    public bool Files { get; set; } = true;
    /// <summary>Dark or light mode and accent colour of Windows itself.</summary>
    public bool Windows { get; set; } = true;
    public bool Wallpaper { get; set; } = true;
    /// <summary>The wallpaper also on the lock screen. Off by default: in companies IT often sets it.</summary>
    public bool LockScreen { get; set; }
}

/// <summary>C:\WinMarchy\Config\settings.json – everything the user chooses in the app.</summary>
public sealed class UserSettings
{
    /// <summary>"auto" (Windows display language), "de", "de-CH" or "en".</summary>
    public string Language { get; set; } = "auto";

    /// <summary>Look of the WinMarchy window: "system", "light" or "dark".</summary>
    public string ColorScheme { get; set; } = "system";

    /// <summary>Omarchy theme applied last.</summary>
    public string Theme { get; set; } = "tokyo-night";

    public ThemeTargets ThemeTargets { get; set; } = new();

    /// <summary>
    /// "autohotkey": AutoHotkey owns every shortcut (Win alone opens the launcher, Win+wheel switches
    /// workspaces). "glazewm": GlazeWM owns them – for machines where IT blocks AutoHotkey.
    /// </summary>
    public string HotkeyEngine { get; set; } = "autohotkey";

    /// <summary>Adds the "Persönlich" profile (consumer apps, bindings.lua) on top of "Business".</summary>
    public bool PersonalProfile { get; set; }

    /// <summary>Tapping the left Windows key alone opens the launcher (AutoHotkey only).</summary>
    public bool WinKeyOpensLauncher { get; set; } = true;

    public bool HideTaskbar { get; set; } = true;
    public bool ShowBar { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;

    /// <summary>Layout of every workspace without its own choice: "dwindle", "columns", "floating" or "manual".</summary>
    public string DefaultLayout { get; set; } = "dwindle";

    public int InnerGap { get; set; } = 10;
    public int OuterGap { get; set; } = 10;
    public bool SquareCorners { get; set; } = true;
    public bool HideTitleBars { get; set; }
    public bool DimInactive { get; set; }
    public bool FocusFollowsCursor { get; set; }

    /// <summary>
    /// Omarchy knows no minimizing: a window that gets minimized comes straight back into its tile.
    /// Win+M brings back anything that slipped through.
    /// </summary>
    public bool BlockMinimize { get; set; } = true;

    /// <summary>How many columns the scrolling layout shows side by side (1 to 3).</summary>
    public int ScrollingColumns { get; set; } = 2;

    /// <summary>Workspace "1" … "10" → monitor index (0 = leftmost). Missing: any monitor.</summary>
    public Dictionary<string, int> WorkspaceMonitors { get; set; } = new();

    /// <summary>Win+Ctrl+D: the app ("app:…" or "web:…") that starts on the new workspace of each monitor index.</summary>
    public Dictionary<string, string> DesktopApps { get; set; } = new();

    /// <summary>Browser behind Win+Shift+B and web apps: null = Windows' default browser, else an exe.</summary>
    public string? Browser { get; set; }

    /// <summary>Browser behind Win+Shift+Enter: null = Windows' default browser, else an exe.</summary>
    public string? Browser2 { get; set; }

    /// <summary>"windows" (Snipping Tool) or "greenshot".</summary>
    public string ScreenshotTool { get; set; } = "windows";

    /// <summary>What the bar shows, by module id (see ZebarPack.Modules). Missing: the default.</summary>
    public Dictionary<string, bool> BarModules { get; set; } = new();

    /// <summary>Tray icons that stay in sight while the tray is folded away (part of their name).</summary>
    public List<string> BarTrayPinned { get; set; } = new();

    /// <summary>Settings file of the Files app, if it is not found automatically.</summary>
    public string? FilesSettingsPath { get; set; }


    public bool AutoUpdate { get; set; } = true;

    /// <summary>Log page: every shortcut and GlazeWM command goes into the log, for finding faults.</summary>
    public bool VerboseLog { get; set; }

    /// <summary>Program paths that override detection (key: tool id).</summary>
    public Dictionary<string, string> ToolPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(Paths.SettingsFile))
                return Json.Deserialize<UserSettings>(File.ReadAllText(Paths.SettingsFile)) ?? new UserSettings();
        }
        catch (Exception ex)
        {
            Log.Warn(L.T("settings.unreadable", ("detail", ex.Message)));
        }
        return new UserSettings();
    }

    public void Save() => FileSafety.WriteText(Paths.SettingsFile, Json.Serialize(this, indented: true));

    public UserSettings Clone() => Json.Deserialize<UserSettings>(Json.Serialize(this))!;
}

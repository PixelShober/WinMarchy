using System.Text.Json.Serialization;

namespace WinMarchy.Core;

/// <summary>Which programs a theme change restyles.</summary>
public sealed class ThemeTargets
{
    public bool GlazeWm { get; set; } = true;
    /// <summary>The bar at the top. Called "zebar" in settings.json from the time the bar was Zebar.</summary>
    [JsonPropertyName("zebar")]
    public bool Bar { get; set; } = true;
    public bool Terminal { get; set; } = true;
    public bool PowerShell { get; set; } = true;
    public bool VsCode { get; set; } = true;
    public bool Flow { get; set; } = true;
    public bool Files { get; set; } = true;
    /// <summary>Dark or light mode and accent colour of Windows itself.</summary>
    public bool Windows { get; set; } = true;
    public bool Wallpaper { get; set; } = true;
    /// <summary>Starship's prompt and Fastfetch, if they are installed.</summary>
    public bool Shell { get; set; } = true;
    /// <summary>ExplorerBlurMica's tint in Explorer, if it is switched on.</summary>
    public bool ExplorerBlur { get; set; } = true;
    /// <summary>The wallpaper also on the lock screen. Off by default: in companies IT often sets it.</summary>
    public bool LockScreen { get; set; }
}

/// <summary>An entry of the user's own in the bar.</summary>
public sealed class BarShortcut
{
    public string Title { get; set; } = "";

    /// <summary>What it does, written like a shortcut's action: "app:terminal", "web:https://…", "do:theme-menu".</summary>
    public string Action { get; set; } = "";

    /// <summary>Also a button of its own in the bar, not only an entry in the menu under the logo.</summary>
    public bool InBar { get; set; }
}

/// <summary>
/// A work mode (Win+Ctrl+D, Win+Ctrl+1 to 3): fresh workspaces on every screen, with the programs
/// listed here, one per screen.
/// </summary>
public sealed class WorkMode
{
    /// <summary>How many modes there are (Win+Ctrl+1 to 3).</summary>
    public const int Count = 3;

    /// <summary>Empty: "Work mode 1" and so on.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// What starts on screen 1, 2, 3 …, written like a shortcut's action ("app:browser https://…",
    /// "web:…", "run:…"); empty: nothing. Programs for screens that are not connected go to the last one.
    /// </summary>
    public List<string> Apps { get; set; } = new();

    /// <summary>
    /// The mode a shortcut's action starts: "do:work-mode 2" → 2; "do:work-mode" and the older
    /// "do:desktop-new" → 1; anything else → null.
    /// </summary>
    public static int? NumberOf(string? action)
    {
        var parts = (action ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[0] is not ("do:work-mode" or "do:desktop-new")) return null;
        if (parts.Length == 1) return 1;
        return int.TryParse(parts[1], out var n) && n is >= 1 and <= Count ? n : null;
    }

    /// <summary>The name to show for mode <paramref name="number"/> (1 to 3).</summary>
    public string Title(int number) => Name.Trim() is { Length: > 0 } name ? name : L.T("workModes.default", ("number", number));
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

    /// <summary>Tapping the left Windows key alone opens the launcher (AutoHotkey only).</summary>
    public bool WinKeyOpensLauncher { get; set; } = true;

    public bool HideTaskbar { get; set; } = true;
    public bool ShowBar { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;

    /// <summary>Layout of a screen without its own choice: "dwindle", "columns" or "scrolling".</summary>
    public string DefaultLayout { get; set; } = "dwindle";

    /// <summary>
    /// Each screen's layout, by monitor index (0 = leftmost, as GlazeWM counts): every workspace shown
    /// on that screen follows it. Missing: <see cref="DefaultLayout"/>.
    /// </summary>
    public List<string> ScreenLayouts { get; set; } = new();

    /// <summary>The layout of screen <paramref name="index"/>.</summary>
    public GlazeWm.LayoutMode ScreenLayoutOf(int index) =>
        GlazeWm.LayoutEngine.TryParse(ScreenLayouts.ElementAtOrDefault(index), out var mode) ? mode : GlazeWm.LayoutEngine.Parse(DefaultLayout);

    /// <summary>Sets the layout of screen <paramref name="index"/>; screens before it keep theirs.</summary>
    public void SetScreenLayout(int index, GlazeWm.LayoutMode mode)
    {
        if (index < 0) return;
        while (ScreenLayouts.Count <= index) ScreenLayouts.Add(GlazeWm.LayoutEngine.Id(ScreenLayoutOf(ScreenLayouts.Count)));
        ScreenLayouts[index] = GlazeWm.LayoutEngine.Id(mode);
    }

    public int InnerGap { get; set; } = 10;
    public int OuterGap { get; set; } = 10;
    public bool SquareCorners { get; set; } = true;
    public bool HideTitleBars { get; set; }
    public bool DimInactive { get; set; }
    public bool FocusFollowsCursor { get; set; }

    /// <summary>A window that opens next to one in full screen ends the full screen, as in Hyprland.</summary>
    public bool NewWindowEndsFullscreen { get; set; } = true;

    /// <summary>Workspace "1" … "10" → monitor index (0 = leftmost). Missing: any monitor.</summary>
    public Dictionary<string, int> WorkspaceMonitors { get; set; } = new();

    /// <summary>The main screen by index (0 = leftmost), which gets workspaces 1 and 2. Null: Windows' main screen.</summary>
    public int? MainScreen { get; set; }

    /// <summary>Win+Ctrl+D and Win+Ctrl+1 to 3, in that order. Missing modes start nothing.</summary>
    public List<WorkMode> WorkModes { get; set; } = new();

    /// <summary>
    /// Settings from before the work modes: the program Win+Ctrl+D started per monitor index. Read
    /// once into the first work mode, then no longer written.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? DesktopApps { get; set; }

    /// <summary>Work mode <paramref name="number"/> (1 to 3); an empty one if it was never set.</summary>
    public WorkMode WorkModeOf(int number) => WorkModes.ElementAtOrDefault(number - 1) ?? new WorkMode();

    /// <summary>Browser behind Win+Shift+B and web apps: null = Windows' default browser, else an exe.</summary>
    public string? Browser { get; set; }

    /// <summary>Browser behind Win+Shift+Enter: null = Windows' default browser, else an exe.</summary>
    public string? Browser2 { get; set; }

    /// <summary>"windows" (Snipping Tool) or "greenshot". Greenshot takes over once it is installed, unless <see cref="ScreenshotToolChosen"/>.</summary>
    public string ScreenshotTool { get; set; } = "windows";

    /// <summary>The user picked the screenshot tool in the settings: WinMarchy no longer switches to Greenshot by itself.</summary>
    public bool ScreenshotToolChosen { get; set; }

    /// <summary>The user's own entries in the bar: in the menu under the logo, some also as buttons.</summary>
    public List<BarShortcut> BarShortcuts { get; set; } = new();

    /// <summary>What the bar shows, by module id (see Bars.Modules). Missing: the default.</summary>
    public Dictionary<string, bool> BarModules { get; set; } = new();

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
                return (Json.Deserialize<UserSettings>(File.ReadAllText(Paths.SettingsFile)) ?? new UserSettings()).Upgraded();
        }
        catch (Exception ex)
        {
            Log.Warn(L.T("settings.unreadable", ("detail", ex.Message)));
        }
        return new UserSettings();
    }

    public void Save() => FileSafety.WriteText(Paths.SettingsFile, Json.Serialize(this, indented: true));

    /// <summary>
    /// Older settings in today's form: the programs Win+Ctrl+D started per monitor become the first
    /// work mode; the floating and manual layouts are gone (Win+T floats single windows); Lunatask is
    /// no longer one of the apps.
    /// </summary>
    public UserSettings Upgraded()
    {
        if (!GlazeWm.LayoutEngine.TryParse(DefaultLayout, out _)) DefaultLayout = "dwindle";
        ScreenLayouts = ScreenLayouts.Select(l => GlazeWm.LayoutEngine.TryParse(l, out _) ? l : DefaultLayout).ToList();
        BarShortcuts.RemoveAll(b => b.Action.Trim().Equals("app:lunatask", StringComparison.OrdinalIgnoreCase));
        foreach (var mode in WorkModes)
            for (int i = 0; i < mode.Apps.Count; i++)
                if (mode.Apps[i].Trim().Equals("app:lunatask", StringComparison.OrdinalIgnoreCase)) mode.Apps[i] = "";
        if (DesktopApps is { Count: > 0 } old && WorkModes.Count == 0)
        {
            var screens = old.Keys.Select(k => int.TryParse(k, out var i) ? i : -1).Where(i => i is >= 0 and < 16).DefaultIfEmpty(-1).Max() + 1;
            if (screens > 0)
                WorkModes.Add(new WorkMode { Apps = Enumerable.Range(0, screens).Select(i => old.TryGetValue(i.ToString(), out var a) ? a : "").ToList() });
        }
        DesktopApps = null;
        // There are six workspaces since the limit; bindings of 7 to 10 no longer mean anything.
        foreach (var key in WorkspaceMonitors.Keys.Where(k => !int.TryParse(k, out var n) || n < 1 || n > GlazeWm.GlazeConfig.WorkspaceCount).ToList())
            WorkspaceMonitors.Remove(key);
        return this;
    }

    public UserSettings Clone() => Json.Deserialize<UserSettings>(Json.Serialize(this))!;
}

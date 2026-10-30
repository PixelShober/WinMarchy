using System.Text.Json.Serialization;

namespace WinMarchy.Core;

/// <summary>Which programs a theme change restyles.</summary>
public sealed class ThemeTargets
{
    public bool GlazeWm { get; set; } = true;
    /// <summary>The bar at the top, YASB or Zebar. Called "zebar" in settings.json from the time there was only Zebar.</summary>
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

    /// <summary>A window that opens next to one in full screen ends the full screen, as in Hyprland.</summary>
    public bool NewWindowEndsFullscreen { get; set; } = true;

    /// <summary>How many columns the scrolling layout shows side by side (1 to 3).</summary>
    public int ScrollingColumns { get; set; } = 2;

    /// <summary>Workspace "1" … "10" → monitor index (0 = leftmost). Missing: any monitor.</summary>
    public Dictionary<string, int> WorkspaceMonitors { get; set; } = new();

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

    /// <summary>"windows" (Snipping Tool) or "greenshot".</summary>
    public string ScreenshotTool { get; set; } = "windows";

    /// <summary>"winmarchy" (WinMarchy's build of GlazeWM) or "official" (the signed release from glzr-io).</summary>
    public string GlazeBuild { get; set; } = "winmarchy";

    /// <summary>
    /// "yasb" or "zebar". Null: never chosen; then a Zebar that WinMarchy set up earlier keeps running
    /// until the user switches (see <see cref="Integrations.Bars.Chosen"/>), and a new setup takes YASB.
    /// </summary>
    public string? Bar { get; set; }

    /// <summary>The user's own entries in the bar: in the menu under the logo, some also as buttons.</summary>
    public List<BarShortcut> BarShortcuts { get; set; } = new();

    /// <summary>What the bar shows, by module id (see Bars.Modules). Missing: the default.</summary>
    public Dictionary<string, bool> BarModules { get; set; } = new();

    /// <summary>Tray icons that stay in sight while the tray is folded away (part of their name). Zebar only; YASB pins with Alt+click.</summary>
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
                return (Json.Deserialize<UserSettings>(File.ReadAllText(Paths.SettingsFile)) ?? new UserSettings()).Upgraded();
        }
        catch (Exception ex)
        {
            Log.Warn(L.T("settings.unreadable", ("detail", ex.Message)));
        }
        return new UserSettings();
    }

    public void Save() => FileSafety.WriteText(Paths.SettingsFile, Json.Serialize(this, indented: true));

    /// <summary>Older settings in today's form: the programs Win+Ctrl+D started per monitor become the first work mode.</summary>
    public UserSettings Upgraded()
    {
        if (DesktopApps is { Count: > 0 } old && WorkModes.Count == 0)
        {
            var screens = old.Keys.Select(k => int.TryParse(k, out var i) ? i : -1).Where(i => i is >= 0 and < 16).DefaultIfEmpty(-1).Max() + 1;
            if (screens > 0)
                WorkModes.Add(new WorkMode { Apps = Enumerable.Range(0, screens).Select(i => old.TryGetValue(i.ToString(), out var a) ? a : "").ToList() });
        }
        DesktopApps = null;
        return this;
    }

    public UserSettings Clone() => Json.Deserialize<UserSettings>(Json.Serialize(this))!;
}

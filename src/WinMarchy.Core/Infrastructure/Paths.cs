namespace WinMarchy.Core;

/// <summary>
/// Every location WinMarchy itself reads from or writes to. Everything lives under C:\WinMarchy, so
/// there is one folder to look in (and one path for shortcuts, autostart and IT rules):
/// <code>
/// C:\WinMarchy\App        WinMarchy.exe          C:\WinMarchy\Logs      winmarchy-*.log
/// C:\WinMarchy\Config     settings, hotkeys      C:\WinMarchy\Backups   restore points
/// C:\WinMarchy\Themes     previews, wallpapers   C:\WinMarchy\Tools     GlazeWM, YASB, AutoHotkey …
/// C:\WinMarchy\Runtime    generated scripts      C:\WinMarchy\Downloads, Temp
/// </code>
/// If C:\ does not let a normal user create folders (hardened machines), WinMarchy falls back to
/// %LOCALAPPDATA%\WinMarchy with the same layout. Where other programs keep their settings is in
/// <see cref="Locations"/>.
/// </summary>
public static class Paths
{
    /// <summary>Preferred install root.</summary>
    public const string InstallRoot = @"C:\WinMarchy";

    static string? _root;

    /// <summary>C:\WinMarchy, or %LOCALAPPDATA%\WinMarchy when C:\WinMarchy cannot be created or written.</summary>
    public static string Root => _root ??= ResolveRoot();

    /// <summary>Tests point everything to a temporary folder.</summary>
    internal static void OverrideRoot(string root) => _root = root;

    public static string AppDir => Path.Combine(Root, "App");
    public static string InstalledExe => Path.Combine(AppDir, "WinMarchy.exe");
    public static string ConfigDir => Path.Combine(Root, "Config");
    public static string LogDir => Path.Combine(Root, "Logs");
    public static string BackupDir => Path.Combine(Root, "Backups");
    public static string ThemesDir => Path.Combine(Root, "Themes");
    public static string ToolsDir => Path.Combine(Root, "Tools");
    public static string RuntimeDir => Path.Combine(Root, "Runtime");
    public static string DownloadDir => Path.Combine(Root, "Downloads");
    public static string TempDir => Path.Combine(Root, "Temp");

    public static string SettingsFile => Path.Combine(ConfigDir, "settings.json");
    public static string HotkeysFile => Path.Combine(ConfigDir, "hotkeys.json");
    public static string LayoutsFile => Path.Combine(ConfigDir, "layouts.json");
    public static string StateFile => Path.Combine(ConfigDir, "state.json");
    public static string WindowFile => Path.Combine(ConfigDir, "window.json");
    public static string ScratchpadFile => Path.Combine(ConfigDir, "scratchpad.json");

    public static string AhkScript => Path.Combine(RuntimeDir, "winmarchy-hotkeys.ahk");
    /// <summary>Starship's settings for the prompt WinMarchy sets up (STARSHIP_CONFIG points here, the user's own file stays untouched).</summary>
    public static string StarshipConfig => Path.Combine(RuntimeDir, "starship.toml");
    /// <summary>Loads Starship in PowerShell 7; the profile hook runs it.</summary>
    public static string PromptScript => Path.Combine(RuntimeDir, "starship-prompt.ps1");
    public static string FastfetchConfig => Path.Combine(RuntimeDir, "fastfetch.jsonc");

    public static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>A unique file name in C:\WinMarchy\Temp (created on demand).</summary>
    public static string TempFile(string prefix, string extension) =>
        Path.Combine(Ensure(TempDir), $"{prefix}-{Guid.NewGuid():N}.{extension}");

    static string ResolveRoot()
    {
        if (!OperatingSystem.IsWindows()) return Path.Combine(Path.GetTempPath(), "WinMarchy");
        try
        {
            Directory.CreateDirectory(InstallRoot);
            var probe = Path.Combine(InstallRoot, $".write-test-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return InstallRoot;
        }
        catch
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMarchy");
        }
    }
}

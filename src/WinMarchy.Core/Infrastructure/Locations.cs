namespace WinMarchy.Core;

/// <summary>
/// Where the integrated programs keep their settings. Built from the user's base folders, so tests
/// can point all of them into a temporary directory.
/// </summary>
public sealed class Locations
{
    public Locations(string userProfile, string appData, string localAppData, string documents, string startup, string programFiles)
    {
        UserProfile = userProfile;
        AppData = appData;
        LocalAppData = localAppData;
        Documents = documents;
        Startup = startup;
        ProgramFiles = programFiles;
    }

    /// <summary>The real folders of the signed-in user.</summary>
    public static Locations Current { get; set; } = FromEnvironment();

    public static Locations FromEnvironment()
    {
        static string F(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        var profile = F(Environment.SpecialFolder.UserProfile);
        var docs = F(Environment.SpecialFolder.MyDocuments);
        return new Locations(
            profile,
            F(Environment.SpecialFolder.ApplicationData),
            F(Environment.SpecialFolder.LocalApplicationData),
            docs.Length > 0 ? docs : Path.Combine(profile, "Documents"),
            F(Environment.SpecialFolder.Startup),
            F(Environment.SpecialFolder.ProgramFiles));
    }

    /// <summary>A layout below one folder (tests).</summary>
    public static Locations Under(string root) => new(
        Path.Combine(root, "profile"), Path.Combine(root, "roaming"), Path.Combine(root, "local"),
        Path.Combine(root, "documents"), Path.Combine(root, "startup"), Path.Combine(root, "programs"));

    public string UserProfile { get; }
    public string AppData { get; }
    public string LocalAppData { get; }
    public string Documents { get; }
    public string Startup { get; }
    public string ProgramFiles { get; }

    // GlazeWM and Zebar (glzr.io)
    public string GlzrDir => Path.Combine(UserProfile, ".glzr");
    public string GlazeWmConfig => Path.Combine(GlzrDir, "glazewm", "config.yaml");
    public string ZebarDir => Path.Combine(GlzrDir, "zebar");
    public string ZebarSettings => Path.Combine(ZebarDir, "settings.json");
    public string ZebarPackDir => Path.Combine(ZebarDir, "winmarchy");

    // YASB, the bar. YASB_CONFIG_HOME moves its folder, as it does for YASB itself.
    public string YasbDir => Environment.GetEnvironmentVariable("YASB_CONFIG_HOME") is { Length: > 0 } home ? home : Path.Combine(UserProfile, ".config", "yasb");
    public string YasbConfig => Path.Combine(YasbDir, "config.yaml");
    public string YasbStyles => Path.Combine(YasbDir, "styles.css");

    // Flow Launcher
    public string FlowSettings => Path.Combine(AppData, "FlowLauncher", "Settings", "Settings.json");
    public string FlowThemesDir => Path.Combine(AppData, "FlowLauncher", "Themes");
    public string FlowThemeFile => Path.Combine(FlowThemesDir, "WinMarchy.xaml");

    // Windows Terminal (Store, Preview, unpackaged)
    public IEnumerable<string> TerminalSettingsCandidates => new[]
    {
        Path.Combine(LocalAppData, "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "LocalState", "settings.json"),
        Path.Combine(LocalAppData, "Packages", "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe", "LocalState", "settings.json"),
        Path.Combine(LocalAppData, "Microsoft", "Windows Terminal", "settings.json"),
    };

    // VS Code (stable and Insiders)
    public IEnumerable<string> VsCodeSettingsCandidates => new[]
    {
        Path.Combine(AppData, "Code", "User", "settings.json"),
        Path.Combine(AppData, "Code - Insiders", "User", "settings.json"),
    };

    // PowerShell 7 profile and the colour file it loads
    public string PowerShellProfile => Path.Combine(Documents, "PowerShell", "Microsoft.PowerShell_profile.ps1");
    // Windows PowerShell 5.1, which the terminal opens when PowerShell 7 is not installed
    public string WindowsPowerShellProfile => Path.Combine(Documents, "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1");

    // Startup folder entries WinMarchy owns
    public string AutostartShortcut => Path.Combine(Startup, "WinMarchy.lnk");

    // Winarchy Lite (the PowerShell predecessor): old data and autostart entries
    public string LegacyStateDir => Path.Combine(UserProfile, ".winarchy-lite");
    public string LegacyRuntimeDir => Path.Combine(AppData, "WinarchyLite");
    public string LegacyHotkeysShortcut => Path.Combine(Startup, "Winarchy Lite Omarchy Hotkeys.lnk");
}

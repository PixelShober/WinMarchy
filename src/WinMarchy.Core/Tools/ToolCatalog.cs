using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WinMarchy.Core.Tools;

public enum InstallKind
{
    /// <summary>Zip from the vendor, unpacked into C:\WinMarchy\Tools\&lt;id&gt;.</summary>
    Zip,
    /// <summary>MSI unpacked without installing (msiexec /a), so no admin rights are needed.</summary>
    MsiExtract,
    /// <summary>The vendor's per-user installer, run silently.</summary>
    Installer,
    /// <summary>WinMarchy only detects it and links to the vendor.</summary>
    None,
}

/// <summary>A program WinMarchy builds on. All of them may be used free of charge in companies.</summary>
public sealed record ToolDef(
    string Id,
    string Name,
    string License,
    string Homepage,
    string? Repo,
    string? AssetPattern,
    InstallKind Kind,
    string Exe,
    bool Required,
    string? InstallerArgs = null,
    bool Extra = false)
{
    public Regex? Asset => AssetPattern == null ? null : new Regex(AssetPattern, RegexOptions.IgnoreCase);

    public string ToolDir => Path.Combine(Paths.ToolsDir, Id);
}

public sealed record ToolStatus(ToolDef Tool, string? Path, string? Version, bool Running, bool InstalledByWinMarchy)
{
    public bool Installed => Path != null;
}

/// <summary>The two GlazeWM builds WinMarchy can install.</summary>
public static class GlazeBuilds
{
    public const string WinMarchy = "winmarchy";
    public const string Official = "official";
}

public static class ToolCatalog
{
    /// <summary>
    /// GlazeWM as WinMarchy ships it: its own build from PixelShober/glazewm, upstream GlazeWM plus the
    /// changes listed in WINMARCHY.md there. Not signed, so it runs without UIAccess.
    /// </summary>
    public static readonly ToolDef GlazeWm = new("glazewm", "GlazeWM", "GPL-3.0", "https://github.com/PixelShober/glazewm",
        "PixelShober/glazewm", @"^glazewm-v[\d.]+-winmarchy\.\d+-x64\.zip$", InstallKind.Zip, "glazewm.exe", Required: true);

    /// <summary>The signed release from glzr-io, without WinMarchy's changes. Same id and folder as <see cref="GlazeWm"/>.</summary>
    public static readonly ToolDef GlazeWmOfficial = GlazeWm with
    {
        Homepage = "https://github.com/glzr-io/glazewm",
        Repo = "glzr-io/glazewm",
        AssetPattern = @"^standalone-glazewm-v[\d.]+-x64\.msi$",
        Kind = InstallKind.MsiExtract,
    };

    /// <summary>Which GlazeWM the setup installs (Settings → GlazeWM).</summary>
    public static ToolDef GlazeSource(UserSettings settings) =>
        settings.GlazeBuild == GlazeBuilds.Official ? GlazeWmOfficial : GlazeWm;

    /// <summary>WinMarchy's build ships WINMARCHY.md next to glazewm.exe; the official one does not.</summary>
    public static bool IsWinMarchyBuild(string? glazeExe) =>
        glazeExe != null && Path.GetDirectoryName(glazeExe) is { } dir && File.Exists(Path.Combine(dir, "WINMARCHY.md"));

    /// <summary>Whether the GlazeWM found is the build the settings ask for.</summary>
    public static bool IsWantedGlaze(string? glazeExe, UserSettings settings) =>
        glazeExe != null && IsWinMarchyBuild(glazeExe) == (settings.GlazeBuild != GlazeBuilds.Official);

    public static readonly ToolDef Zebar = new("zebar", "Zebar", "GPL-3.0", "https://github.com/glzr-io/zebar",
        "glzr-io/zebar", @"^zebar-v[\d.]+-opt1-x64\.msi$", InstallKind.MsiExtract, "zebar.exe", Required: false);

    public static readonly ToolDef AutoHotkey = new("autohotkey", "AutoHotkey v2", "GPL-2.0", "https://www.autohotkey.com",
        "AutoHotkey/AutoHotkey", @"^AutoHotkey_2[\d.]+\.zip$", InstallKind.Zip, "AutoHotkey64.exe", Required: false);

    public static readonly ToolDef Flow = new("flow", "Flow Launcher", "MIT", "https://www.flowlauncher.com",
        "Flow-Launcher/Flow.Launcher", @"^Flow-Launcher-Setup\.exe$", InstallKind.Installer, "Flow.Launcher.exe", Required: false, "--silent");

    public static readonly ToolDef PowerShell = new("pwsh", "PowerShell 7", "MIT", "https://github.com/PowerShell/PowerShell",
        "PowerShell/PowerShell", @"^PowerShell-7[\d.]+-win-x64\.zip$", InstallKind.Zip, "pwsh.exe", Required: false, Extra: true);

    public static readonly ToolDef Terminal = new("terminal", "Windows Terminal", "MIT", "https://aka.ms/terminal",
        null, null, InstallKind.None, "wt.exe", Required: false);

    public static readonly ToolDef Files = new("files", "Files", "MIT", "https://files.community",
        null, null, InstallKind.None, "Files.exe", Required: false);

    public static readonly ToolDef VsCode = new("vscode", "Visual Studio Code", "MIT (Code-OSS) / Microsoft-Lizenz", "https://code.visualstudio.com",
        null, null, InstallKind.None, "Code.exe", Required: false);

    /// <summary>Terminal workspace for coding agents (Win+Ctrl+Enter). Windows support is still beta at the vendor.</summary>
    public static readonly ToolDef Herdr = new("herdr", "Herdr", "Apache-2.0", "https://herdr.dev",
        "herdrdev/herdr", @"^herdr-windows-x86_64\.zip$", InstallKind.Zip, "herdr.exe", Required: false, Extra: true);

    /// <summary>The Pi coding agent (Win+Alt+Enter). Needs a bash on Windows, usually Git Bash.</summary>
    public static readonly ToolDef Pi = new("pi", "Pi", "MIT", "https://pi.dev",
        "earendil-works/pi", @"^pi-windows-x64\.zip$", InstallKind.Zip, "pi.exe", Required: false, Extra: true);

    /// <summary>Screenshots with an editor, if the user prefers it to Snipping Tool. The vendor's portable zip.</summary>
    public static readonly ToolDef Greenshot = new("greenshot", "Greenshot", "GPL-3.0", "https://getgreenshot.org",
        "greenshot/greenshot", @"^Greenshot-PORTABLE-[\d.]+-RELEASE\.zip$", InstallKind.Zip, "Greenshot.exe", Required: false, Extra: true);

    public static IReadOnlyList<ToolDef> All { get; } = new[] { GlazeWm, Zebar, AutoHotkey, Flow, PowerShell, Herdr, Pi, Greenshot, Terminal, Files, VsCode };

    /// <summary>Optional programs the setup offers below the main ones, unticked.</summary>
    public static IReadOnlyList<ToolDef> Extras { get; } = All.Where(t => t.Extra).ToList();

    /// <summary>Git Bash, which Pi needs on Windows.</summary>
    public static string? GitBash(Locations loc) => new[]
    {
        Path.Combine(loc.ProgramFiles, "Git", "bin", "bash.exe"),
        Path.Combine(loc.LocalAppData, "Programs", "Git", "bin", "bash.exe"),
    }.FirstOrDefault(File.Exists) ?? OnPath("bash.exe");

    public static ToolDef Get(string id) => All.First(t => t.Id == id);

    /// <summary>Where each program usually is, besides C:\WinMarchy\Tools and PATH.</summary>
    static IEnumerable<string> KnownPaths(ToolDef t, Locations loc) => t.Id switch
    {
        "glazewm" => new[] { Path.Combine(loc.ProgramFiles, "glzr.io", "GlazeWM", "glazewm.exe") },
        "zebar" => new[]
        {
            Path.Combine(loc.ProgramFiles, "glzr.io", "Zebar", "zebar.exe"), Path.Combine(loc.ProgramFiles, "Zebar", "zebar.exe"),
            Path.Combine(loc.LocalAppData, "Programs", "Zebar", "zebar.exe"),
        },
        "autohotkey" => new[]
        {
            Path.Combine(loc.ProgramFiles, "AutoHotkey", "v2", "AutoHotkey64.exe"),
            Path.Combine(loc.LocalAppData, "Programs", "AutoHotkey", "v2", "AutoHotkey64.exe"),
        },
        "flow" => new[] { Path.Combine(loc.LocalAppData, "FlowLauncher", "Flow.Launcher.exe") },
        // Where Herdr's and Pi's own install scripts put them.
        "herdr" => new[] { Path.Combine(loc.LocalAppData, "Programs", "Herdr", "herdr.exe"), Path.Combine(loc.LocalAppData, "Programs", "Herdr", "winarchy-lite", "herdr.exe") },
        "pi" => new[] { Path.Combine(loc.LocalAppData, "Programs", "pi", "pi.exe"), Path.Combine(loc.UserProfile, ".pi", "bin", "pi.exe") },
        "pwsh" => new[] { Path.Combine(loc.ProgramFiles, "PowerShell", "7", "pwsh.exe") },
        "greenshot" => new[] { Path.Combine(loc.ProgramFiles, "Greenshot", "Greenshot.exe"), Path.Combine(loc.LocalAppData, "Programs", "Greenshot", "Greenshot.exe") },
        "terminal" => new[] { Path.Combine(loc.LocalAppData, "Microsoft", "WindowsApps", "wt.exe") },
        "vscode" => new[]
        {
            Path.Combine(loc.LocalAppData, "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(loc.ProgramFiles, "Microsoft VS Code", "Code.exe"),
        },
        _ => Array.Empty<string>(),
    };

    public static ToolStatus Detect(ToolDef t, Locations loc, UserSettings settings, AppState state)
    {
        string? path = null;
        if (settings.ToolPaths.TryGetValue(t.Id, out var own) && File.Exists(own)) path = own;
        path ??= FindIn(t.ToolDir, t.Exe);
        path ??= KnownPaths(t, loc).FirstOrDefault(File.Exists);
        path ??= OnPath(t.Exe);
        if (path == null && t.Id == "files") path = FilesPackage(loc);

        string? version = null;
        if (path != null && File.Exists(path))
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                version = info.ProductVersion?.Split('+')[0] ?? info.FileVersion;
            }
            catch { }
        }
        var running = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(t.Exe)).Length > 0;
        var ours = path != null && path.StartsWith(Paths.ToolsDir, StringComparison.OrdinalIgnoreCase) || state.InstalledTools.Contains(t.Id);
        return new ToolStatus(t, path, version, running, ours && path != null);
    }

    /// <summary>The GlazeWM command line (cli\glazewm.exe next to the window manager).</summary>
    public static string? GlazeCli(string? glazeExe)
    {
        if (glazeExe == null) return null;
        var cli = Path.Combine(Path.GetDirectoryName(glazeExe)!, "cli", "glazewm.exe");
        return File.Exists(cli) ? cli : glazeExe;
    }

    /// <summary>The window manager itself: the extracted MSI also contains cli\glazewm.exe, which is not it.</summary>
    internal static string? FindIn(string dir, string exe)
    {
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, exe, SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "cli" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Length)
            .FirstOrDefault();
    }

    static string? OnPath(string exe)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }

    /// <summary>Files from the Store or its sideload package has no exe on disk we may start; its package folder proves it is there.</summary>
    static string? FilesPackage(Locations loc)
    {
        var packages = Path.Combine(loc.LocalAppData, "Packages");
        if (!Directory.Exists(packages)) return null;
        return Directory.EnumerateDirectories(packages)
            .FirstOrDefault(d => Path.GetFileName(d) is { } n &&
                                 (n.StartsWith("Files_", StringComparison.OrdinalIgnoreCase) || n.StartsWith("FilesPreview_", StringComparison.OrdinalIgnoreCase) ||
                                  n.Contains("49306atecsolution", StringComparison.OrdinalIgnoreCase)));
    }
}

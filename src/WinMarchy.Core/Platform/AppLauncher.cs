using System.Diagnostics;
using Microsoft.Win32;
using WinMarchy.Core.Tools;

namespace WinMarchy.Core.Platform;

/// <summary>
/// Finds and starts the programs behind "app:…" and "web:…". Each app has fallbacks down to the web
/// version, so a shortcut does something sensible on any company PC.
/// </summary>
public sealed class AppLauncher
{
    readonly Locations _loc;
    readonly Func<UserSettings> _settings;

    public AppLauncher(Locations loc, Func<UserSettings> settings)
    {
        _loc = loc;
        _settings = settings;
    }

    string WindowsApps => Path.Combine(_loc.LocalAppData, "Microsoft", "WindowsApps");

    /// <returns>false when nothing could be started.</returns>
    public bool Launch(string id)
    {
        switch (id)
        {
            case "terminal": return Terminal(null);
            case "herdr":
                var herdr = Tool(ToolCatalog.Herdr);
                return herdr != null ? Terminal(herdr) : false;
            case "browser": return Browser(privateWindow: false);
            case "browser-private": return Browser(privateWindow: true);
            case "files":
                return First(Alias("files-stable.exe"), Alias("files.exe"), Alias("files-preview.exe"), Alias("files-dev.exe")) is { } files
                    ? Start(files) : Start("explorer.exe");
            case "editor":
                return Tool(ToolCatalog.VsCode) is { } code ? Start(code) : Start("notepad.exe");
            case "calculator": return Start("calc.exe");
            case "taskmanager": return Start("taskmgr.exe");
            case "settings": return Open("ms-settings:");
            case "explorer": return Start("explorer.exe");
            case "mail":
                return AppPath("OUTLOOK.EXE") is { } outlook ? Start(outlook)
                    : Alias("olk.exe") is { } olk ? Start(olk) : Web("https://outlook.office.com/mail/");
            case "calendar":
                return AppPath("OUTLOOK.EXE") is { } o2 ? Start(o2, "/select outlook:calendar")
                    : Web("https://outlook.office.com/calendar/");
            case "teams":
                return Alias("ms-teams.exe") is { } teams ? Start(teams) : Open("msteams:") || Web("https://teams.microsoft.com/");
            case "notes":
                return AppPath("ONENOTE.EXE") is { } onenote ? Start(onenote) : Web("https://www.onenote.com/notebooks");
            case "word":
                return AppPath("WINWORD.EXE") is { } word ? Start(word) : Web("https://www.office.com/launch/word");
            case "signal":
                return First(Path.Combine(_loc.LocalAppData, "Programs", "signal-desktop", "Signal.exe")) is { } signal
                    ? Start(signal) : Open("https://signal.org/download/");
            case "telegram":
                return First(Path.Combine(_loc.AppData, "Telegram Desktop", "Telegram.exe"), Alias("Telegram.exe")) is { } tg
                    ? Start(tg) : Web("https://web.telegram.org/");
            case "whatsapp":
                return Alias("WhatsApp.exe") is { } wa ? Start(wa) : Web("https://web.whatsapp.com/");
            case "spotify":
                return First(Path.Combine(_loc.AppData, "Spotify", "Spotify.exe"), Alias("Spotify.exe")) is { } sp
                    ? Start(sp) : Web("https://open.spotify.com/");
            case "obsidian":
                return First(Path.Combine(_loc.LocalAppData, "Programs", "Obsidian", "Obsidian.exe")) is { } obs
                    ? Start(obs) : Open("https://obsidian.md/download");
            case "bitwarden":
                return First(Path.Combine(_loc.LocalAppData, "Programs", "Bitwarden", "Bitwarden.exe")) is { } bw
                    ? Start(bw) : Web("https://vault.bitwarden.com/");
        }
        return false;
    }

    /// <summary>Windows Terminal with PowerShell 7 (never relies on the default profile, which may be PowerShell 5.1).</summary>
    public bool Terminal(string? command)
    {
        var shell = command ?? Tool(ToolCatalog.PowerShell) ?? ProcessRunner.SystemExe(Path.Combine("WindowsPowerShell", "v1.0", "powershell.exe"));
        var wt = Tool(ToolCatalog.Terminal);
        return wt != null ? Start(wt, $"-w -1 new-tab \"{shell}\"") : Start(shell);
    }

    /// <summary>Omarchy's web apps: the site in its own window without tabs (Edge or Chrome --app).</summary>
    public bool Web(string url)
    {
        var browser = DefaultBrowser();
        var chromium = browser != null && Path.GetFileName(browser).ToLowerInvariant() is "msedge.exe" or "chrome.exe" or "brave.exe" or "vivaldi.exe";
        var exe = chromium ? browser : AppPath("msedge.exe");
        return exe != null ? Start(exe, $"--app=\"{url}\"") : Open(url);
    }

    bool Browser(bool privateWindow)
    {
        var exe = DefaultBrowser() ?? AppPath("msedge.exe");
        if (exe == null) return Open("https://");
        var name = Path.GetFileName(exe).ToLowerInvariant();
        var args = name switch
        {
            "firefox.exe" => privateWindow ? "-private-window" : "-new-window",
            "msedge.exe" => privateWindow ? "--inprivate" : "--new-window",
            _ => privateWindow ? "--incognito" : "--new-window",
        };
        return Start(exe, args);
    }

    /// <summary>Executable of the default browser for https links (from its ProgId's open command).</summary>
    public static string? DefaultBrowser()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var choice = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            if (choice?.GetValue("ProgId") is not string progId) return null;
            using var cmd = Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command");
            if (cmd?.GetValue(null) is not string line) return null;
            var exe = line.StartsWith('"') ? line[1..line.IndexOf('"', 1)] : line.Split(' ')[0];
            return File.Exists(exe) ? exe : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Programs registered under App Paths (Office, Edge) – also found when they are not on PATH.</summary>
    public static string? AppPath(string exe)
    {
        if (!OperatingSystem.IsWindows()) return null;
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe);
                if (key?.GetValue(null) is string path && File.Exists(path.Trim('"'))) return path.Trim('"');
            }
            catch { }
        }
        return null;
    }

    string? Alias(string exe)
    {
        var path = Path.Combine(WindowsApps, exe);
        return File.Exists(path) ? path : null;
    }

    string? Tool(ToolDef tool) => ToolCatalog.Detect(tool, _loc, _settings(), AppState.Load()).Path is { } p && File.Exists(p) ? p : null;

    static string? First(params string?[] paths) => paths.FirstOrDefault(p => p != null && File.Exists(p));

    public static bool Start(string exe, string args = "")
    {
        try
        {
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn(L.T("launcher.failed", ("target", exe), ("detail", ex.Message)));
            return false;
        }
    }

    /// <summary>A link or settings page; the scheme was checked when the shortcut was saved.</summary>
    public static bool Open(string target) => Start(target);
}

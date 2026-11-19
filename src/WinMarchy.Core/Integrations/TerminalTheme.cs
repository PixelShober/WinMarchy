using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using WinMarchy.Core.Themes;
using WinMarchy.Core.Tools;

namespace WinMarchy.Core.Integrations;

/// <summary>
/// Windows Terminal colour scheme "WinMarchy" plus PowerShell 7 syntax colours (PSReadLine).
/// Every ANSI colour is lifted to at least 4.5:1 contrast on the background, and ANSI black never
/// equals a dark background – many CLIs print options and hints in ANSI 30.
/// </summary>
public static class TerminalTheme
{
    public const string SchemeName = "WinMarchy";
    const string HookStart = "# BEGIN WINMARCHY PSREADLINE";
    const string HookEnd = "# END WINMARCHY PSREADLINE";

    public sealed record Scheme(string Background, string Foreground, string Cursor, string Selection, string Muted,
        string Red, string Green, string Yellow, string Blue, string Magenta, string Cyan,
        string BrightRed, string BrightGreen, string BrightYellow, string BrightBlue, string BrightMagenta, string BrightCyan);

    public static Scheme Build(Palette p)
    {
        var bg = p.Background;
        var fg = ColorMath.Contrast(p.Foreground, bg) >= 7 ? p.Foreground : (ColorMath.IsDark(bg) ? "#f2f4fa" : "#161b22");
        string Leg(string c) => ColorMath.Legible(c, bg);
        return new Scheme(bg, fg, ColorMath.Legible(p.Accent, bg, 3), ColorMath.Mix(bg, fg, 0.30), Leg(ColorMath.Mix(bg, fg, 0.68)),
            Leg(p.Red), Leg(p.Green), Leg(p.Yellow), Leg(p.Blue), Leg(p.Magenta), Leg(p.Cyan),
            Leg(p.BrightRed), Leg(p.BrightGreen), Leg(p.BrightYellow), Leg(p.BrightBlue), Leg(p.BrightMagenta), Leg(p.BrightCyan));
    }

    public static JsonObject SchemeJson(Scheme s) => new()
    {
        ["name"] = SchemeName,
        ["background"] = s.Background, ["foreground"] = s.Foreground, ["cursorColor"] = s.Cursor, ["selectionBackground"] = s.Selection,
        ["black"] = s.Muted, ["red"] = s.Red, ["green"] = s.Green, ["yellow"] = s.Yellow, ["blue"] = s.Blue,
        ["purple"] = s.Magenta, ["cyan"] = s.Cyan, ["white"] = s.Foreground,
        ["brightBlack"] = s.Muted, ["brightRed"] = s.BrightRed, ["brightGreen"] = s.BrightGreen, ["brightYellow"] = s.BrightYellow,
        ["brightBlue"] = s.BrightBlue, ["brightPurple"] = s.BrightMagenta, ["brightCyan"] = s.BrightCyan, ["brightWhite"] = s.Foreground,
    };

    /// <summary>Puts the scheme into settings.json and makes it the default (also for profiles that set their own).</summary>
    public static string Patch(string original, Scheme scheme)
    {
        var root = Jsonc.ParseObject(original);
        var schemes = root["schemes"] as JsonArray ?? new JsonArray();
        for (int i = schemes.Count - 1; i >= 0; i--)
            if (schemes[i] is JsonObject o && (string?)o["name"] == SchemeName) schemes.RemoveAt(i);
        schemes.Add(SchemeJson(scheme));
        root["schemes"] = schemes;

        // "profiles" is an object { defaults, list } today; very old files have a plain array.
        if (root["profiles"] is JsonArray oldList)
        {
            foreach (var profile in oldList.OfType<JsonObject>()) profile["colorScheme"] = SchemeName;
        }
        else
        {
            var profiles = Jsonc.Obj(root, "profiles");
            Jsonc.Obj(profiles, "defaults")["colorScheme"] = SchemeName;
            if (profiles["list"] is JsonArray list)
                foreach (var profile in list.OfType<JsonObject>())
                    if (profile.ContainsKey("colorScheme")) profile["colorScheme"] = SchemeName;
        }
        return Json.WriteLike(root, original);
    }

    public static void Apply(IntegrationContext ctx, Palette palette)
    {
        var scheme = Build(palette);
        int done = 0;
        foreach (var file in ctx.Loc.TerminalSettingsCandidates.Where(File.Exists))
        {
            try
            {
                var original = FileSafety.ReadShared(file);
                ctx.WriteFile(file, Patch(original, scheme));
                done++;
                ctx.Report(L.T("integrations.terminal.done", ("path", file)));
            }
            catch (Exception ex)
            {
                Log.Warn(L.T("integrations.terminal.failed", ("path", file), ("detail", ex.Message)));
            }
        }
        if (done == 0) ctx.Report(L.T("integrations.terminal.notFound"));
    }

    // ------------------------------------------------------------------ PSReadLine

    public static string ColorScript(Scheme s)
    {
        var colors = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["Default"] = s.Foreground, ["Command"] = s.Green, ["Parameter"] = s.Yellow, ["Operator"] = s.Cyan,
            ["Keyword"] = s.Magenta, ["Number"] = s.Yellow, ["String"] = s.Green, ["Variable"] = s.Foreground,
            ["Type"] = s.Blue, ["Member"] = s.Cyan, ["Comment"] = s.Muted, ["Emphasis"] = s.Foreground,
            ["Error"] = s.Red, ["Selection"] = s.Foreground, ["InlinePrediction"] = s.Muted,
        };
        var sb = new StringBuilder();
        sb.Append("# Generated by WinMarchy from the current Omarchy theme. New PowerShell 7 tabs load these colours.\r\n");
        sb.Append("$e = [char]27\r\n");
        sb.Append("if (Get-Module -ListAvailable PSReadLine) {\r\n");
        sb.Append("  try {\r\n");
        sb.Append("    Set-PSReadLineOption -Colors @{\r\n");
        foreach (var (name, hex) in colors)
        {
            var (r, g, b) = ColorMath.Rgb(hex);
            sb.Append($"      {name} = \"$e[38;2;{r};{g};{b}m\"\r\n");
        }
        sb.Append("    }\r\n  } catch { }\r\n}\r\n");
        return sb.ToString();
    }

    /// <summary>The colours PowerShell 7 loads (PSReadLine).</summary>
    public static string ColorFile => Path.Combine(Paths.RuntimeDir, "psreadline-colors.ps1");

    /// <summary>
    /// WinMarchy's block in the profile: the colours and, once Starship or Fastfetch is set up, what a new tab starts with. Each
    /// file is only loaded if it is there, so removing one never breaks the profile.
    /// </summary>
    public static string ProfileHook(string colorFile, string shellFile)
    {
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        return $"{HookStart}\r\n$winmarchyColors = {Q(colorFile)}\r\nif (Test-Path -LiteralPath $winmarchyColors) {{ . $winmarchyColors }}\r\n" +
               $"$winmarchyShell = {Q(shellFile)}\r\nif (Test-Path -LiteralPath $winmarchyShell) {{ . $winmarchyShell }}\r\n{HookEnd}\r\n";
    }

    /// <summary>Adds the hook once; an existing hook is replaced (the file paths may have moved).</summary>
    internal static string WithHook(string profile, string colorFile, string? shellFile = null)
    {
        var hook = ProfileHook(colorFile, shellFile ?? Paths.ShellScript);
        int start = profile.IndexOf(HookStart, StringComparison.Ordinal);
        int end = profile.IndexOf(HookEnd, StringComparison.Ordinal);
        if (start >= 0 && end > start)
        {
            var after = end + HookEnd.Length;
            while (after < profile.Length && profile[after] is '\r' or '\n') after++;
            return profile[..start] + hook + profile[after..];
        }
        var trimmed = profile.TrimEnd();
        return trimmed.Length == 0 ? hook : trimmed + "\r\n\r\n" + hook;
    }

    public static void ApplyPowerShell(IntegrationContext ctx, Palette palette)
    {
        // A UTF-8 BOM, so Windows PowerShell 5.1 reads it correctly as well.
        ctx.WriteFile(ColorFile, ColorScript(Build(palette)), bom: true);
        var profiles = EnsureProfileHook(ctx);
        if (profiles.Count > 0) ctx.Report(L.T("integrations.powershell.done", ("path", string.Join(" · ", profiles))));
    }

    /// <summary>
    /// WinMarchy's block in the profiles of PowerShell 7 and of Windows PowerShell 5.1, wherever the
    /// execution policy lets PowerShell load a profile; otherwise PowerShell would print an error on
    /// every start. Returns the profiles that have the block.
    /// </summary>
    public static IReadOnlyList<string> EnsureProfileHook(IntegrationContext ctx)
    {
        var done = new List<string>();
        var pwsh = ToolCatalog.Detect(ToolCatalog.PowerShell, ctx.Loc, ctx.Settings, AppState.Load()).Installed;
        if (PowerShellPolicy.BlocksProfiles() is { } core)
        {
            if (pwsh) ctx.Report(L.T("integrations.powershell.blocked", ("policy", core)));
        }
        else
        {
            WriteHook(ctx, ctx.Loc.PowerShellProfile);
            done.Add(ctx.Loc.PowerShellProfile);
        }
        var desktop = PowerShellPolicy.WindowsPowerShell();
        if (PowerShellPolicy.LoadsProfiles(desktop))
        {
            WriteHook(ctx, ctx.Loc.WindowsPowerShellProfile);
            done.Add(ctx.Loc.WindowsPowerShellProfile);
        }
        // Only worth saying when it is the PowerShell the terminal opens.
        else if (!pwsh)
        {
            ctx.Report(L.T("integrations.powershell.desktopBlocked", ("policy", desktop)));
        }
        return done;
    }

    static void WriteHook(IntegrationContext ctx, string profile)
    {
        var current = File.Exists(profile) ? File.ReadAllText(profile) : "";
        var updated = WithHook(current, ColorFile);
        if (updated != current) ctx.WriteFile(profile, updated, bom: true);
    }
}

/// <summary>Execution policies: under AllSigned or Restricted a profile would fail on every start.</summary>
public static class PowerShellPolicy
{
    /// <summary>PowerShell 7 set to AllSigned or Restricted by group policy (its own default is RemoteSigned).</summary>
    public static string? BlocksProfiles()
    {
        if (!OperatingSystem.IsWindows()) return null;
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            if (FromPolicy(hive, @"SOFTWARE\Policies\Microsoft\PowerShellCore") is { } p && !LoadsProfiles(p)) return p;
        }
        return null;
    }

    /// <summary>
    /// The execution policy of Windows PowerShell 5.1, worked out as PowerShell does it: group policy
    /// for the machine, then for the user, then the user's own setting, then the machine's.
    /// Restricted when nothing is set, the default on Windows 10 and 11.
    /// </summary>
    public static string WindowsPowerShell()
    {
        if (!OperatingSystem.IsWindows()) return "Restricted";
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            if (FromPolicy(hive, @"SOFTWARE\Policies\Microsoft\Windows\PowerShell") is { } p) return p;
        }
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            if (Read(hive, @"SOFTWARE\Microsoft\PowerShell\1\ShellIds\Microsoft.PowerShell", "ExecutionPolicy") is string p && p.Length > 0 &&
                !p.Equals("Undefined", StringComparison.OrdinalIgnoreCase)) return p;
        }
        return "Restricted";
    }

    /// <summary>A local, unsigned profile runs under these.</summary>
    public static bool LoadsProfiles(string policy) =>
        policy.Equals("RemoteSigned", StringComparison.OrdinalIgnoreCase) || policy.Equals("Unrestricted", StringComparison.OrdinalIgnoreCase) ||
        policy.Equals("Bypass", StringComparison.OrdinalIgnoreCase);

    /// <summary>"Turn on Script Execution" in group policy: off is Restricted, on names the policy.</summary>
    static string? FromPolicy(RegistryKey hive, string key) =>
        Read(hive, key, "EnableScripts") switch
        {
            0 => "Restricted",
            1 => Read(hive, key, "ExecutionPolicy") as string ?? "AllSigned",
            _ => null,
        };

    static object? Read(RegistryKey hive, string key, string name)
    {
        try
        {
            using var k = hive.OpenSubKey(key);
            return k?.GetValue(name);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Not readable: as if not set.
            return null;
        }
    }
}

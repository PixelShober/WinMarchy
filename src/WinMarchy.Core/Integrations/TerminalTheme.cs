using System.Text;
using System.Text.Json.Nodes;
using WinMarchy.Core.Themes;

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
    /// WinMarchy's block in the profile: the colours and, once Starship is set up, the prompt. Each
    /// file is only loaded if it is there, so removing one never breaks the profile.
    /// </summary>
    public static string ProfileHook(string colorFile, string promptFile)
    {
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        return $"{HookStart}\r\n$winmarchyColors = {Q(colorFile)}\r\nif (Test-Path -LiteralPath $winmarchyColors) {{ . $winmarchyColors }}\r\n" +
               $"$winmarchyPrompt = {Q(promptFile)}\r\nif (Test-Path -LiteralPath $winmarchyPrompt) {{ . $winmarchyPrompt }}\r\n{HookEnd}\r\n";
    }

    /// <summary>Adds the hook once; an existing hook is replaced (the file paths may have moved).</summary>
    internal static string WithHook(string profile, string colorFile, string? promptFile = null)
    {
        var hook = ProfileHook(colorFile, promptFile ?? Paths.PromptScript);
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
        if (PowerShellPolicy.BlocksProfiles() is { } policy)
        {
            ctx.Report(L.T("integrations.powershell.blocked", ("policy", policy)));
            return;
        }
        // A UTF-8 BOM, so Windows PowerShell 5.1 would read it correctly as well.
        ctx.WriteFile(ColorFile, ColorScript(Build(palette)), bom: true);
        WriteHook(ctx);
        ctx.Report(L.T("integrations.powershell.done", ("path", ctx.Loc.PowerShellProfile)));
    }

    /// <summary>
    /// The profile block without new colours (Starship's setup). False if a group policy keeps
    /// PowerShell from running profiles; then nothing is written.
    /// </summary>
    public static bool EnsureProfileHook(IntegrationContext ctx)
    {
        if (PowerShellPolicy.BlocksProfiles() is { } policy)
        {
            ctx.Report(L.T("integrations.powershell.blocked", ("policy", policy)));
            return false;
        }
        WriteHook(ctx);
        return true;
    }

    static void WriteHook(IntegrationContext ctx)
    {
        var profile = ctx.Loc.PowerShellProfile;
        var current = File.Exists(profile) ? File.ReadAllText(profile) : "";
        var updated = WithHook(current, ColorFile);
        if (updated != current) ctx.WriteFile(profile, updated, bom: true);
    }
}

/// <summary>Execution policy set by group policy: under AllSigned or Restricted a profile would fail on every start.</summary>
public static class PowerShellPolicy
{
    public static string? BlocksProfiles()
    {
        if (!OperatingSystem.IsWindows()) return null;
        foreach (var hive in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
        {
            try
            {
                using var key = hive.OpenSubKey(@"SOFTWARE\Policies\Microsoft\PowerShellCore");
                if (key?.GetValue("EnableScripts") is int enabled)
                {
                    if (enabled == 0) return "Restricted";
                    if (key.GetValue("ExecutionPolicy") is string p && p is "AllSigned" or "Restricted") return p;
                }
            }
            catch
            {
                // not readable: assume no policy
            }
        }
        return null;
    }
}

using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace WinMarchy.Core.Platform;

/// <summary>A policy that is set on this PC and touches something WinMarchy does.</summary>
/// <param name="Id">Which check found it (text under policies.effects.&lt;id&gt;).</param>
/// <param name="Location">"HKLM\…\Name" as regedit shows it.</param>
/// <param name="Value">The value, shortened; empty when only the key's presence counts.</param>
public sealed record PolicyFinding(string Id, string Location, string Value)
{
    public string Effect => L.T("policies.effects." + Id, ("value", Value));
}

/// <summary>
/// Looks for group policies and MDM settings that override what WinMarchy sets: wallpaper, lock
/// screen, colours, taskbar, allowed programs, scripts, shortcuts. Only reads; HKLM is readable for
/// normal users.
/// </summary>
public static class PolicyCheck
{
    /// <summary>Reads a value (name) or, with name null, whether the key exists ("" if so).</summary>
    public delegate object? Reader(string hive, string key, string? name);

    sealed record Check(string Id, string Hive, string Key, string? Name, Func<object, bool>? Applies = null);

    const string ExplorerPolicies = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    const string SystemPolicies = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    const string ActiveDesktop = @"Software\Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop";
    const string Personalization = @"SOFTWARE\Policies\Microsoft\Windows\Personalization";

    static bool On(object v) => v is int i ? i != 0 : v is string s && s != "0" && s.Length > 0;

    static readonly Check[] Checks =
    {
        new("wallpaper", "HKCU", SystemPolicies, "Wallpaper"),
        new("wallpaperLocked", "HKCU", ActiveDesktop, "NoChangingWallPaper", On),
        new("wallpaperLocked", "HKLM", ActiveDesktop, "NoChangingWallPaper", On),
        new("wallpaperMdm", "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP", "DesktopImagePath"),
        new("colorsLocked", "HKCU", SystemPolicies, "NoColorChoice", On),
        new("colorsLocked", "HKCU", SystemPolicies, "NoDispAppearancePage", On),
        new("colorsLocked", "HKCU", SystemPolicies, "NoVisualStyleChoice", On),
        new("colorsLocked", "HKCU", ExplorerPolicies, "NoThemesTab", On),
        new("accentForced", "HKLM", Personalization, "PersonalColors_Accent"),
        new("accentForced", "HKLM", Personalization, "PersonalColors_Background"),
        new("lockScreen", "HKLM", Personalization, "LockScreenImage"),
        new("lockScreenLocked", "HKLM", Personalization, "NoChangingLockScreen", On),
        new("lockScreenLocked", "HKCU", Personalization, "NoChangingLockScreen", On),
        new("lockScreenOff", "HKLM", Personalization, "NoLockScreen", On),
        new("lockScreenMdm", "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP", "LockScreenImagePath"),
        new("spotlight", "HKCU", @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsSpotlightFeatures", On),
        new("taskbarLocked", "HKCU", ExplorerPolicies, "NoSetTaskbar", On),
        new("taskbarLocked", "HKCU", ExplorerPolicies, "TaskbarLockAll", On),
        new("taskbarLocked", "HKCU", ExplorerPolicies, "LockTaskbar", On),
        new("winKeys", "HKCU", ExplorerPolicies, "NoWinKeys", On),
        new("winKeys", "HKLM", ExplorerPolicies, "NoWinKeys", On),
        new("allowList", "HKCU", ExplorerPolicies, "RestrictRun", On),
        new("blockList", "HKCU", ExplorerPolicies, "DisallowRun", On),
        new("appLocker", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\SrpV2", null),
        new("srp", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\Safer\CodeIdentifiers", null),
        new("runIgnored", "HKLM", ExplorerPolicies, "DisableCurrentUserRun", On),
        new("runIgnored", "HKCU", ExplorerPolicies, "DisableCurrentUserRun", On),
        new("scriptHost", "HKCU", @"Software\Microsoft\Windows Script Host\Settings", "Enabled", v => !On(v)),
        new("scriptHost", "HKLM", @"SOFTWARE\Microsoft\Windows Script Host\Settings", "Enabled", v => !On(v)),
        new("powershell", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\PowerShell", "ExecutionPolicy"),
        new("powershell", "HKCU", @"Software\Policies\Microsoft\Windows\PowerShell", "ExecutionPolicy"),
        new("cmd", "HKCU", @"Software\Policies\Microsoft\Windows\System", "DisableCMD", On),
        new("lockWorkstation", "HKCU", SystemPolicies, "DisableLockWorkstation", On),
        new("taskManager", "HKCU", SystemPolicies, "DisableTaskMgr", On),
        new("registryTools", "HKCU", SystemPolicies, "DisableRegistryTools", On),
        new("notifications", "HKCU", @"Software\Policies\Microsoft\Windows\Explorer", "DisableNotificationCenter", On),
        new("webView2", "HKLM", @"SOFTWARE\Policies\Microsoft\Edge\WebView2", null),
        new("webView2", "HKCU", @"Software\Policies\Microsoft\Edge\WebView2", null),
        new("proxy", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "ProxyServer"),
        new("proxyScript", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "AutoConfigURL"),
    };

    /// <summary>Every check against the given registry; testable without Windows.</summary>
    public static IReadOnlyList<PolicyFinding> Evaluate(Reader read)
    {
        var found = new List<PolicyFinding>();
        foreach (var c in Checks)
        {
            object? value;
            try { value = read(c.Hive, c.Key, c.Name); }
            catch { continue; }
            if (value == null || (c.Applies != null && !c.Applies(value))) continue;
            // A proxy address alone means nothing; it counts while ProxyEnable is on.
            if (c.Id == "proxy" && !(read(c.Hive, c.Key, "ProxyEnable") is int enabled && enabled != 0)) continue;
            var location = c.Hive + @"\" + c.Key + (c.Name != null ? @"\" + c.Name : "");
            found.Add(new PolicyFinding(c.Id, location, Shorten(value)));
        }
        return found;
    }

    static string Shorten(object value)
    {
        var text = value switch
        {
            string s => s,
            string[] list => string.Join("; ", list),
            byte[] bytes => $"{bytes.Length} Bytes",
            _ => value.ToString() ?? "",
        };
        return text.Length > 120 ? text[..117] + "…" : text;
    }

    /// <summary>The real registry, plus whether the PC is enrolled in an MDM such as Intune.</summary>
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<PolicyFinding> Run()
    {
        var found = Evaluate(ReadRegistry).ToList();
        if (MdmProvider() is { } provider) found.Add(new PolicyFinding("mdm", @"HKLM\SOFTWARE\Microsoft\Enrollments", provider));
        return found;
    }

    [SupportedOSPlatform("windows")]
    static object? ReadRegistry(string hive, string key, string? name)
    {
        using var root = hive == "HKLM" ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                                        : RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
        using var k = root.OpenSubKey(key);
        if (k == null) return null;
        if (name == null) return k.ValueCount + k.SubKeyCount > 0 ? "" : null;
        return k.GetValue(name);
    }

    [SupportedOSPlatform("windows")]
    static string? MdmProvider()
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var enrollments = root.OpenSubKey(@"SOFTWARE\Microsoft\Enrollments");
            if (enrollments == null) return null;
            foreach (var id in enrollments.GetSubKeyNames())
            {
                using var e = enrollments.OpenSubKey(id);
                if (e?.GetValue("ProviderID") is string p && p.Length > 0 && e.GetValue("UPN") is string upn && upn.Length > 0)
                    return p;
            }
        }
        catch { }
        return null;
    }

    /// <summary>The group policy objects Windows applied for this user (gpresult, no admin rights needed).</summary>
    public static async Task<string> GpResultAsync(CancellationToken ct)
    {
        var gp = ProcessRunner.SystemExe("gpresult.exe");
        var r = await ProcessRunner.RunAsync(gp, new[] { "/r", "/scope:user" }, ct, timeoutSec: 45, output: OutputKind.Oem, logOutput: false);
        return r.Output.Trim();
    }

    /// <summary>The report that goes to C:\WinMarchy\Logs.</summary>
    public static string Report(IReadOnlyList<PolicyFinding> findings, string? gpresult)
    {
        var sb = new StringBuilder();
        sb.AppendLine(L.T("policies.reportTitle", ("time", DateTime.Now.ToString("g", L.Culture)), ("machine", Environment.MachineName)));
        sb.AppendLine();
        if (findings.Count == 0) sb.AppendLine(L.T("policies.none"));
        foreach (var f in findings)
        {
            sb.AppendLine("• " + f.Effect);
            sb.AppendLine("  " + f.Location + (f.Value.Length > 0 ? " = " + f.Value : ""));
        }
        if (!string.IsNullOrWhiteSpace(gpresult))
        {
            sb.AppendLine();
            sb.AppendLine("gpresult /r /scope:user");
            sb.AppendLine(gpresult);
        }
        return sb.ToString();
    }
}

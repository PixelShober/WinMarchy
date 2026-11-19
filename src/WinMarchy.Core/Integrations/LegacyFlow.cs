using System.Text.Json.Nodes;

namespace WinMarchy.Core.Integrations;

/// <summary>
/// Flow Launcher, the launcher behind the Win key in earlier WinMarchy versions. WinMarchy no longer
/// installs, themes or starts it; Fluent Search took its place. A Flow that WinMarchy set up (it
/// listens on Alt+F24) must not run any more, or both would open. It stays installed (it can be
/// removed under Windows Settings → Apps), but it is ended and no longer starts with Windows. Both
/// changes go into the restore point, so undo brings Flow's start with Windows back. A Flow the user
/// set up on another key is none of WinMarchy's business.
/// </summary>
public static class LegacyFlow
{
    /// <summary>The process name, without ".exe".</summary>
    public const string Process = "Flow.Launcher";

    /// <summary>Where Flow enters itself to start with Windows.</summary>
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    internal const string RunValue = "Flow.Launcher";

    /// <summary>Flow's settings for the signed-in user.</summary>
    internal static string SettingsFile(Locations loc) => Path.Combine(loc.AppData, "FlowLauncher", "Settings", "Settings.json");

    /// <summary>The hotkey WinMarchy gave Flow; the one AutoHotkey presses for the launcher.</summary>
    const string WinMarchyHotkey = "Alt + F24";

    /// <summary>Whether Flow is set up the way WinMarchy did it: on Alt+F24.</summary>
    public static bool SetUpByWinMarchy(Locations loc)
    {
        var file = SettingsFile(loc);
        if (!File.Exists(file)) return false;
        try
        {
            return Jsonc.ParseObject(FileSafety.ReadShared(file))["Hotkey"] is JsonValue v && v.TryGetValue<string>(out var key) && key == WinMarchyHotkey;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException)
        {
            return false;
        }
    }

    /// <summary>Whether a Flow that WinMarchy set up still needs retiring: it runs, or starts with Windows.</summary>
    public static bool Present(Locations loc, IUserRegistry registry) =>
        SetUpByWinMarchy(loc) && (System.Diagnostics.Process.GetProcessesByName(Process).Length > 0 || registry.Read(RunKey, RunValue) != null ||
                                  StartsWithWindows(FileSafety.ReadShared(SettingsFile(loc))));

    /// <summary>
    /// Ends a Flow that WinMarchy set up and takes it out of the start with Windows: its entry under
    /// "Run" and its own setting. Returns whether anything changed.
    /// </summary>
    public static async Task<bool> RetireAsync(IntegrationContext ctx, CancellationToken ct)
    {
        if (!SetUpByWinMarchy(ctx.Loc)) return false;
        bool changed = await StopAsync(ct);
        if (ctx.Registry.Read(RunKey, RunValue) != null)
        {
            ctx.Backups.CaptureRegistry(ctx.Point, RunKey, RunValue);
            ctx.Registry.Delete(RunKey, RunValue);
            changed = true;
        }
        var settings = SettingsFile(ctx.Loc);
        if (File.Exists(settings) && FileSafety.ReadShared(settings) is var text && StartsWithWindows(text))
        {
            ctx.WriteFile(settings, WithoutAutostart(text));
            changed = true;
        }
        if (changed) Log.Info(L.T("integrations.flow.retired"));
        return changed;
    }

    /// <summary>Ends Flow, which writes its settings when it exits. Returns whether it was running.</summary>
    public static async Task<bool> StopAsync(CancellationToken ct)
    {
        bool any = false;
        foreach (var p in System.Diagnostics.Process.GetProcessesByName(Process))
        {
            using (p)
            {
                any = true;
                try
                {
                    p.Kill();
                    await p.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
                {
                    Log.Debug("Flow Launcher: " + ex.Message);
                }
            }
        }
        return any;
    }

    /// <summary>Whether Flow's settings have it start with Windows (on by default).</summary>
    internal static bool StartsWithWindows(string settings)
    {
        try
        {
            return Jsonc.ParseObject(settings)["StartFlowLauncherOnSystemStartup"] is not JsonValue v || !v.TryGetValue<bool>(out var on) || on;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>Flow's settings with the start with Windows switched off; everything else as it was.</summary>
    internal static string WithoutAutostart(string settings)
    {
        var root = Jsonc.ParseObject(settings);
        root["StartFlowLauncherOnSystemStartup"] = false;
        return Json.WriteLike(root, settings);
    }
}

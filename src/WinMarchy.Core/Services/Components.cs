using System.Diagnostics;
using WinMarchy.Core.Backup;
using WinMarchy.Core.GlazeWm;
using WinMarchy.Core.Integrations;
using WinMarchy.Core.Tools;

namespace WinMarchy.Core.Services;

/// <summary>Starting, stopping and reloading GlazeWM, Zebar and the AutoHotkey script.</summary>
public sealed class Components
{
    readonly Locations _loc;
    readonly Func<UserSettings> _settings;

    public Components(Locations loc, Func<UserSettings> settings)
    {
        _loc = loc;
        _settings = settings;
    }

    public ToolStatus Status(ToolDef tool) => ToolCatalog.Detect(tool, _loc, _settings(), AppState.Load());

    public string? GlazeExe => Status(ToolCatalog.GlazeWm).Path;
    public string? ZebarExe => Status(ToolCatalog.Zebar).Path;
    public string? AhkExe => Status(ToolCatalog.AutoHotkey).Path;

    /// <summary>The exe that GlazeWM, Zebar and AutoHotkey call back: the installed copy if there is one.</summary>
    public static string SelfExe => File.Exists(Paths.InstalledExe) ? Paths.InstalledExe : Environment.ProcessPath ?? Paths.InstalledExe;

    public static bool IsRunning(string processName) => Process.GetProcessesByName(processName).Length > 0;

    public bool StartGlaze()
    {
        if (IsRunning("glazewm")) return true;
        var exe = GlazeExe;
        if (exe == null) return false;
        return Platform.AppLauncher.Start(exe, "start");
    }

    /// <summary>Reload through the open connection, else through GlazeWM's command line.</summary>
    public async Task ReloadGlazeAsync(GlazeIpc? ipc, CancellationToken ct)
    {
        if (!IsRunning("glazewm")) return;
        if (ipc is { Connected: true })
        {
            await ipc.CommandAsync("wm-reload-config", ct);
            return;
        }
        if (ToolCatalog.GlazeCli(GlazeExe) is { } cli)
            await ProcessRunner.RunAsync(cli, new[] { "command", "wm-reload-config" }, ct, timeoutSec: 15);
    }

    /// <summary>Starts Flow Launcher if it is installed and not running. True if it was started.</summary>
    public bool StartFlow()
    {
        if (IsRunning("Flow.Launcher")) return false;
        if (Status(ToolCatalog.Flow).Path is not { } flow)
        {
            Log.Trace("flow: not installed", "flow");
            return false;
        }
        Log.Trace("flow: starting " + flow, "flow");
        return Platform.AppLauncher.Start(flow);
    }

    public void StartBar()
    {
        if (ZebarExe is not { } zebar) return;
        Platform.AppLauncher.Start(zebar, $"start-widget-preset --pack {ZebarPack.PackName} --widget-name {ZebarPack.Widget} --preset default");
    }

    public void StopBar()
    {
        foreach (var p in Process.GetProcessesByName("zebar"))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
        }
    }

    // ------------------------------------------------------------------ AutoHotkey

    /// <summary>
    /// Checks the script with AutoHotkey's /Validate before anything changes. A script with an error
    /// never replaces the running one.
    /// </summary>
    public async Task<(bool Ok, string Output)> ValidateAhkAsync(string script, CancellationToken ct)
    {
        var ahk = AhkExe ?? throw new WinMarchyException("ahkMissing");
        var stage = Paths.TempFile("winmarchy-hotkeys", "ahk");
        await File.WriteAllTextAsync(stage, script, new System.Text.UTF8Encoding(true), ct);
        try
        {
            var r = await ProcessRunner.RunAsync(ahk, new[] { "/ErrorStdOut=UTF-8", "/Validate", stage }, ct, timeoutSec: 30);
            return (r.Ok, r.Output);
        }
        finally
        {
            try { File.Delete(stage); } catch { }
        }
    }

    /// <summary>Our own script, the Winarchy Lite one and the old "FlowLauncher Windows Key" script all hook LWin.</summary>
    public static IEnumerable<Process> OurAhkProcesses()
    {
        foreach (var p in Process.GetProcesses())
        {
            if (!p.ProcessName.StartsWith("AutoHotkey", StringComparison.OrdinalIgnoreCase)) continue;
            string? cmd = CommandLine(p);
            if (cmd != null && (cmd.Contains("winmarchy-hotkeys.ahk", StringComparison.OrdinalIgnoreCase) ||
                                cmd.Contains("omarchy-hotkeys.ahk", StringComparison.OrdinalIgnoreCase) ||
                                cmd.Contains("FlowLauncher Windows Key.ahk", StringComparison.OrdinalIgnoreCase)))
                yield return p;
        }
    }

    public void StopAhk()
    {
        foreach (var p in OurAhkProcesses())
        {
            try { p.Kill(); p.WaitForExit(3000); } catch { }
        }
    }

    public bool StartAhk()
    {
        if (AhkExe is not { } ahk || !File.Exists(Paths.AhkScript)) return false;
        StopAhk();
        return Platform.AppLauncher.Start(ahk, $"\"{Paths.AhkScript}\"");
    }

    /// <summary>The command line of another process (via WMI, late-bound).</summary>
    static string? CommandLine(Process p)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var locator = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (locator == null) return null;
            dynamic wmi = Activator.CreateInstance(locator)!;
            dynamic svc = wmi.ConnectServer(".", "root\\cimv2");
            foreach (dynamic item in svc.ExecQuery($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {p.Id}"))
                return (string?)item.CommandLine;
        }
        catch { }
        return null;
    }

    // ------------------------------------------------------------------ autostart

    /// <summary>One entry in the user's Startup folder: WinMarchy starts GlazeWM, Zebar and AutoHotkey itself.</summary>
    public void SetAutostart(BackupStore backups, RestorePoint point, bool on)
    {
        var link = _loc.AutostartShortcut;
        backups.CaptureFile(point, link);
        if (on) FileSafety.CreateShortcut(link, SelfExe, "--background", Path.GetDirectoryName(SelfExe)!, L.T("app.shortcutDescription"));
        else if (File.Exists(link)) File.Delete(link);
    }

    /// <summary>
    /// Winarchy Lite's autostart entry and its hotkey script would fight over the Win key. Both are
    /// stopped and the entry is removed (it is in the restore point).
    /// </summary>
    public IReadOnlyList<string> RetireLegacy(BackupStore backups, RestorePoint point)
    {
        var done = new List<string>();
        foreach (var p in OurAhkProcesses().Where(p => !(CommandLine(p) ?? "").Contains("winmarchy-hotkeys.ahk", StringComparison.OrdinalIgnoreCase)))
        {
            try { p.Kill(); done.Add(L.T("legacy.stoppedScript", ("pid", p.Id))); } catch { }
        }
        if (Directory.Exists(_loc.Startup))
        {
            foreach (var file in Directory.EnumerateFiles(_loc.Startup))
            {
                var name = Path.GetFileName(file);
                bool legacy = name.Equals(Path.GetFileName(_loc.LegacyHotkeysShortcut), StringComparison.OrdinalIgnoreCase) ||
                              name.Equals("FlowLauncher Windows Key.ahk", StringComparison.OrdinalIgnoreCase) ||
                              (name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && FileSafety.ReadShortcut(file) is { } s &&
                               (s.Target + " " + s.Arguments).Contains("FlowLauncher Windows Key.ahk", StringComparison.OrdinalIgnoreCase));
                if (!legacy) continue;
                backups.CaptureFile(point, file);
                File.Delete(file);
                done.Add(L.T("legacy.removedAutostart", ("name", name)));
            }
        }
        return done;
    }
}

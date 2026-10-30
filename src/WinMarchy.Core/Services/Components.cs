using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WinMarchy.Core.Backup;
using WinMarchy.Core.GlazeWm;
using WinMarchy.Core.Integrations;
using WinMarchy.Core.Tools;

namespace WinMarchy.Core.Services;

/// <summary>Starting, stopping and reloading GlazeWM, the bar (YASB or Zebar) and the AutoHotkey script.</summary>
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
    public string? YasbExe => Status(ToolCatalog.Yasb).Path;
    public string? AhkExe => Status(ToolCatalog.AutoHotkey).Path;

    /// <summary>The bar that runs now (see <see cref="Bars.Chosen"/>).</summary>
    public string Bar => Bars.Chosen(_settings(), _loc);

    /// <summary>The chosen bar's program, if it is installed.</summary>
    public string? BarExe => Status(Bars.Tool(Bar)).Path;

    public bool BarRunning => IsRunning(Bars.Process(Bar));

    /// <summary>The exe that GlazeWM, the bar and AutoHotkey call back: the installed copy if there is one.</summary>
    public static string SelfExe => File.Exists(Paths.InstalledExe) ? Paths.InstalledExe : Environment.ProcessPath ?? Paths.InstalledExe;

    public static bool IsRunning(string processName) => Process.GetProcessesByName(processName).Length > 0;

    public bool StartGlaze()
    {
        if (IsRunning("glazewm")) return true;
        var exe = GlazeExe;
        if (exe == null) return false;
        return Platform.AppLauncher.Start(exe, "start");
    }

    /// <summary>
    /// Ends a GlazeWM that no longer answers and starts it again. Only the window manager itself is
    /// ended: its watcher (glazewm-watcher.exe) stays and shows the windows GlazeWM had hidden on
    /// other workspaces, as it does after a crash. GlazeWM then takes them on again.
    /// </summary>
    public async Task<bool> RestartGlazeAsync(CancellationToken ct)
    {
        var exe = GlazeExe;
        foreach (var p in Process.GetProcessesByName("glazewm"))
        {
            using (p)
            {
                string? path = null;
                try { path = p.MainModule?.FileName; } catch { }
                // Not the command line (cli\glazewm.exe), if it happens to run.
                if (path != null && exe != null && !path.Equals(exe, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    p.Kill();
                    p.WaitForExit(3000);
                    Log.Info(L.T("agent.glazeEnded", ("pid", p.Id)));
                }
                catch (Exception ex)
                {
                    Log.Warn(L.T("agent.glazeEndFailed", ("detail", Net.Describe(ex))));
                }
            }
        }
        await Task.Delay(1500, ct);
        return StartGlaze();
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

    /// <summary>Starts the chosen bar. False if it is not installed.</summary>
    public bool StartBar()
    {
        if (BarExe is not { } exe) return false;
        if (Bar == Bars.Zebar)
            return Platform.AppLauncher.Start(exe, $"start-widget-preset --pack {ZebarPack.PackName} --widget-name {ZebarPack.Widget} --preset default");
        return Platform.AppLauncher.Start(exe);
    }

    /// <summary>Ends both bars: the one not chosen must not stay either (after a switch).</summary>
    public void StopBar()
    {
        // YASB first through its own command line, so it can let go of the tray icons; whatever still runs is ended.
        if (IsRunning("yasb") && YasbExe is { } yasb && Path.Combine(Path.GetDirectoryName(yasb)!, "yasbc.exe") is var cli && File.Exists(cli))
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(cli, "stop --silent") { UseShellExecute = false, CreateNoWindow = true });
                p?.WaitForExit(5000);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Log.Debug("yasbc stop: " + ex.Message);
            }
        }
        foreach (var name in new[] { "yasb", "zebar" })
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                }
            }
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
        var titles = AhkWindowTitles();
        foreach (var p in Process.GetProcesses())
        {
            if (!p.ProcessName.StartsWith("AutoHotkey", StringComparison.OrdinalIgnoreCase)) continue;
            var script = ScriptOf(p, titles);
            if (script != null && (script.Contains("winmarchy-hotkeys.ahk", StringComparison.OrdinalIgnoreCase) ||
                                   script.Contains("omarchy-hotkeys.ahk", StringComparison.OrdinalIgnoreCase) ||
                                   script.Contains("FlowLauncher Windows Key.ahk", StringComparison.OrdinalIgnoreCase)))
                yield return p;
        }
    }

    /// <summary>
    /// Which script an AutoHotkey process runs. Its hidden main window is titled
    /// "C:\…\script.ahk - AutoHotkey v2.0.x", which needs no WMI (often blocked in companies); the
    /// command line is the fallback.
    /// </summary>
    static string? ScriptOf(Process p, IReadOnlyDictionary<int, string>? titles = null) =>
        (titles ?? AhkWindowTitles()).TryGetValue(p.Id, out var title) ? title : CommandLine(p);

    /// <summary>Titles of AutoHotkey's main windows by process id.</summary>
    static Dictionary<int, string> AhkWindowTitles()
    {
        var found = new Dictionary<int, string>();
        if (!OperatingSystem.IsWindows()) return found;
        EnumWindows((hwnd, _) =>
        {
            var cls = new StringBuilder(64);
            if (GetClassName(hwnd, cls, cls.Capacity) > 0 && cls.ToString() == "AutoHotkey")
            {
                var title = new StringBuilder(1024);
                GetWindowText(hwnd, title, title.Capacity);
                GetWindowThreadProcessId(hwnd, out var pid);
                if (title.Length > 0) found.TryAdd((int)pid, title.ToString());
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    public void StopAhk()
    {
        foreach (var p in OurAhkProcesses())
        {
            try { p.Kill(); p.WaitForExit(3000); } catch { }
        }
    }

    /// <summary>
    /// Starts the hotkey script or swaps it for the newly written one. The script has "#SingleInstance
    /// Force": the new instance takes over from the running one by itself, so there is no moment in
    /// which Windows gets the Win key and opens the Start menu. Other scripts that hook LWin go first.
    /// </summary>
    public bool StartAhk()
    {
        if (AhkExe is not { } ahk || !File.Exists(Paths.AhkScript)) return false;
        var titles = AhkWindowTitles();
        foreach (var p in OurAhkProcesses().Where(p => !IsOwnScript(p, titles)))
        {
            try { p.Kill(); p.WaitForExit(3000); } catch { }
        }
        return Platform.AppLauncher.Start(ahk, $"\"{Paths.AhkScript}\"");
    }

    static bool IsOwnScript(Process p, IReadOnlyDictionary<int, string>? titles = null) =>
        (ScriptOf(p, titles) ?? "").Contains("winmarchy-hotkeys.ahk", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>One entry in the user's Startup folder: WinMarchy starts GlazeWM, the bar and AutoHotkey itself.</summary>
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
        foreach (var p in OurAhkProcesses().Where(p => !IsOwnScript(p)))
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

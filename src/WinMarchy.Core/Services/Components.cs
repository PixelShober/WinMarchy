using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WinMarchy.Core.Backup;
using WinMarchy.Core.GlazeWm;
using WinMarchy.Core.Integrations;
using WinMarchy.Core.Tools;

namespace WinMarchy.Core.Services;

/// <summary>Starting, stopping and reloading GlazeWM, the bar (YASB) and the AutoHotkey script.</summary>
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

    /// <summary>The bar's program (YASB), if it is installed.</summary>
    public string? BarExe => YasbExe;

    public bool BarRunning => IsRunning(Bars.Process);

    /// <summary>The exe that GlazeWM, the bar and AutoHotkey call back: the installed copy if there is one.</summary>
    public static string SelfExe => File.Exists(Paths.InstalledExe) ? Paths.InstalledExe : Environment.ProcessPath ?? Paths.InstalledExe;

    public static bool IsRunning(string processName) => Process.GetProcessesByName(processName).Length > 0;

    // ------------------------------------------------------------------ GlazeWM processes

    /// <summary>
    /// The window manager's processes that still run, with their exe (null where Windows does not
    /// say). Left out: GlazeWM's command line (cli\glazewm.exe, also called glazewm), and a process
    /// that has ended but is still listed for a moment because its watcher holds on to it. Counting
    /// such a one as running would keep a restart from starting GlazeWM at all.
    /// </summary>
    public static List<(int Pid, string? Path)> GlazeProcesses()
    {
        var list = new List<(int, string?)>();
        foreach (var p in Process.GetProcessesByName("glazewm"))
        {
            using (p)
            {
                try
                {
                    if (p.HasExited) continue;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Not allowed to look (elevated): it runs.
                }
                var path = ImagePath(p.Id);
                if (path != null && path.Contains(Path.DirectorySeparatorChar + "cli" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                list.Add((p.Id, path));
            }
        }
        return list;
    }

    /// <summary>Whether the window manager runs (see <see cref="GlazeProcesses"/>).</summary>
    public static bool GlazeRunning => GlazeProcesses().Count > 0;

    /// <summary>
    /// Starts GlazeWM unless it runs already. Once WinMarchy's build is installed, another GlazeWM
    /// that runs instead (an official release installed on its own) is ended first: it lacks what
    /// WinMarchy needs, and two window managers would fight over the windows.
    /// </summary>
    public bool StartGlaze()
    {
        var exe = GlazeExe;
        var running = GlazeProcesses();
        if (exe == null) return running.Count > 0;
        bool fork = ToolCatalog.IsWinMarchyBuild(exe);
        bool own = false;
        foreach (var (pid, path) in running)
        {
            // A path Windows does not tell (an elevated process) counts as WinMarchy's own.
            if (path == null || path.Equals(exe, StringComparison.OrdinalIgnoreCase) || !fork)
            {
                own = true;
                continue;
            }
            if (EndProcess(pid)) Log.Info(L.T("agent.glazeOtherEnded", ("path", path)));
            else own = true;
        }
        if (own) return true;
        Log.Info(L.T("agent.glazeStarting", ("path", exe)));
        return Platform.AppLauncher.Start(exe, "start");
    }

    /// <summary>
    /// Ends GlazeWM and starts it again, for a GlazeWM that no longer answers. Only the window manager
    /// itself is ended: its watcher (glazewm-watcher.exe) stays and shows the windows GlazeWM had
    /// hidden on other workspaces, as it does after a crash; GlazeWM then takes them on again. The new
    /// one starts only once the old one is really gone: while it still holds its lock, a new GlazeWM
    /// refuses to run.
    /// </summary>
    public async Task<bool> RestartGlazeAsync(CancellationToken ct)
    {
        foreach (var (pid, _) in GlazeProcesses())
            if (EndProcess(pid)) Log.Info(L.T("agent.glazeEnded", ("pid", pid)));
        for (int i = 0; i < 50 && GlazeRunning; i++) await Task.Delay(100, ct);
        if (GlazeRunning)
        {
            Log.Warn(L.T("agent.glazeStillRunning"));
            return false;
        }
        await Task.Delay(1000, ct);
        return StartGlaze();
    }

    /// <summary>Ends a process and waits for it a moment. False if Windows does not let it end.</summary>
    static bool EndProcess(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill();
            p.WaitForExit(3000);
            return true;
        }
        catch (ArgumentException)
        {
            return true;    // gone already
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
        {
            Log.Warn(L.T("agent.glazeEndFailed", ("detail", Net.Describe(ex))));
            return false;
        }
    }

    /// <summary>The last lines GlazeWM wrote into its errors.log (%USERPROFILE%\.glzr\glazewm), newest last.</summary>
    public IReadOnlyList<string> GlazeErrors(int lines) => GlazeErrors(_loc, lines);

    /// <summary>
    /// The last lines of GlazeWM's errors.log, newest last. GlazeWM names windows with the start of
    /// their title; that part is left out, the log and the diagnostic report carry no window titles.
    /// </summary>
    public static IReadOnlyList<string> GlazeErrors(Locations loc, int lines)
    {
        var file = loc.GlazeWmErrorLog;
        if (!File.Exists(file)) return Array.Empty<string>();
        try
        {
            return FileSafety.ReadShared(file).Split('\n').Select(l => WithoutTitles(l.TrimEnd())).Where(l => l.Length > 0).TakeLast(lines).ToList();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// "Window(id=…, process=msedge, class=…, title=Angebot Müller ...)" → "…, title=…)". GlazeWM cuts
    /// a title to 17 characters and "...", or leaves it as it is if it is short.
    /// </summary>
    public static string WithoutTitles(string line) =>
        System.Text.RegularExpressions.Regex.Replace(line, @"title=(?:.{0,17}\.\.\.|.{0,20}?)\)", "title=…)");

    /// <summary>The exe of a process, also for one that runs elevated; null if Windows does not say.</summary>
    static string? ImagePath(int pid)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var handle = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            int size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    /// <summary>Reload through the open connection, else through GlazeWM's command line.</summary>
    public async Task ReloadGlazeAsync(GlazeIpc? ipc, CancellationToken ct)
    {
        if (!GlazeRunning) return;
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

    /// <summary>Starts the bar. False if it is not installed.</summary>
    public bool StartBar() => BarExe is { } exe && Platform.AppLauncher.Start(exe);

    /// <summary>Ends the bar.</summary>
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
        foreach (var p in Process.GetProcessesByName(Bars.Process))
        {
            using (p)
            {
                try { p.Kill(entireProcessTree: true); } catch { }
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

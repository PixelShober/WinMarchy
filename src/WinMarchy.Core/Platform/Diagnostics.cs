using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WinMarchy.Core.GlazeWm;
using WinMarchy.Core.Integrations;
using WinMarchy.Core.Services;
using WinMarchy.Core.Tools;

namespace WinMarchy.Core.Platform;

/// <summary>
/// "Diagnosebericht" on the Log page: what WinMarchy sees right now, in one text file to send along
/// with a fault report. GlazeWM's monitors, workspaces and windows (process names only, no window
/// titles), which programs run, what the hotkey script and GlazeWM's config contain, Flow's own log,
/// the taskbar and the latest warnings.
/// </summary>
public static class Diagnostics
{
    /// <param name="screens">Windows' view of the monitors, from the app (WinForms is not in Core).</param>
    public static async Task<string> BuildAsync(GlazeIpc? ipc, string? glazeProblem, IEnumerable<string> screens, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var loc = Locations.Current;
        var s = UserSettings.Load();
        void Head(string title) => sb.AppendLine().AppendLine("== " + title + " ==");
        void Line(string text) => sb.AppendLine(text);
        void Guard(string title, Action body)
        {
            Head(title);
            try { body(); }
            catch (Exception ex) { Line("(" + Net.Describe(ex) + ")"); }
        }

        Line($"WinMarchy {AppInfo.Version} ({AppInfo.Mode}) · {DateTime.Now:yyyy-MM-dd HH:mm:ss} · {Environment.MachineName} · {Environment.OSVersion.VersionString}");
        Line($"Root: {Paths.Root} · verbose log: {(Log.Verbose ? "on" : "off")}");

        Guard("Processes and tools", () =>
        {
            foreach (var name in new[] { "glazewm", "yasb", "zebar", "AutoHotkey64", "AutoHotkey32", "AutoHotkey", "Flow.Launcher", "Greenshot", "WinMarchy" })
                foreach (var p in Process.GetProcessesByName(name))
                {
                    string started = "", path = "";
                    try { started = p.StartTime.ToString("HH:mm:ss"); } catch { }
                    try { path = p.MainModule?.FileName ?? ""; } catch { }
                    Line($"{name} pid {p.Id} since {started} {path}");
                }
            foreach (var tool in ToolCatalog.All)
            {
                var st = ToolCatalog.Detect(tool, loc, s, AppState.Load());
                var build = tool == ToolCatalog.GlazeWm && st.Path != null ? (ToolCatalog.IsWinMarchyBuild(st.Path) ? " winmarchy build" : " official build") : "";
                Line($"{tool.Id}: {(st.Path ?? "not found")} {st.Version}{build} {(st.Running ? "running" : "")}");
            }
        });

        Guard("Settings", () => Line(Json.Serialize(s, indented: true)));
        Guard("State", () => Line(Json.Serialize(AppState.Load(), indented: true)));
        Guard("Work modes (windows they started, screens before the last switch)", () => Line(Json.Serialize(WorkModeStore.Load(), indented: true)));
        Guard("Screens (Windows)", () => { foreach (var screen in screens) Line(screen); });

        Head("GlazeWM");
        Line(ipc is { Connected: true } ? "connected" : "not connected" + (glazeProblem != null ? ": " + glazeProblem : ""));
        if (ipc != null) Line($"unanswered requests in a row: {ipc.Unanswered}");
        Line("answers on a fresh connection: " + (await GlazeIpc.ProbeAsync(ct) ? "yes" : "no"));
        if (ipc is { Connected: true })
        {
            try
            {
                var monitors = await ipc.QueryAsync("monitors", ct);
                int index = 0;
                foreach (var m in GlazeTree.List(monitors, "monitors"))
                {
                    Line($"Monitor {index++}: {GlazeTree.Str(m, "deviceName") ?? GlazeTree.Str(m, "hardwareId") ?? "?"} " +
                         $"{GlazeTree.Int(m, "x")},{GlazeTree.Int(m, "y")} {GlazeTree.Int(m, "width")}×{GlazeTree.Int(m, "height")}" +
                         (GlazeTree.Bool(m, "hasFocus") ? " (focus)" : ""));
                    foreach (var ws in GlazeTree.Children(m).Where(c => GlazeTree.Type(c) == "workspace"))
                    {
                        Line($"  Workspace {GlazeTree.Str(ws, "name")}: {GlazeTree.Str(ws, "tilingDirection")}" +
                             (GlazeTree.Bool(ws, "isDisplayed") ? ", displayed" : "") + (GlazeTree.Bool(ws, "hasFocus") ? ", focus" : ""));
                        foreach (var w in GlazeTree.Windows(ws))
                        {
                            var onTop = GlazeTree.ShownOnTop(w) ? ", on top" : "";
                            Line($"    {GlazeTree.Str(w, "processName")} [{GlazeTree.WindowState(w)}{onTop}] " +
                                 $"{GlazeTree.Int(w, "x")},{GlazeTree.Int(w, "y")} {GlazeTree.Int(w, "width")}×{GlazeTree.Int(w, "height")} " +
                                 $"hwnd {GlazeTree.Handle(w)}" + (GlazeTree.Bool(w, "hasFocus") ? " (focus)" : ""));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Line("query: " + Net.Describe(ex));
            }
        }
        Guard("GlazeWM config", () =>
        {
            var file = loc.GlazeWmConfig;
            if (!File.Exists(file)) { Line(file + " missing"); return; }
            Line(file + ", " + File.GetLastWriteTime(file).ToString("yyyy-MM-dd HH:mm:ss"));
            var lines = File.ReadAllLines(file);
            bool inWorkspaces = false;
            foreach (var l in lines)
            {
                if (l.StartsWith("workspaces:")) inWorkspaces = true;
                else if (inWorkspaces && l.Length > 0 && !l.StartsWith(' ')) inWorkspaces = false;
                if (inWorkspaces || l.Contains("hide_method") || l.Contains("focus_follows")) Line(l);
            }
            Line($"keybindings in the config: {lines.Count(l => l.TrimStart().StartsWith("bindings:"))}");
        });
        Guard("Hotkey script", () =>
        {
            Line(File.Exists(Paths.AhkScript) ? Paths.AhkScript + ", " + File.GetLastWriteTime(Paths.AhkScript).ToString("yyyy-MM-dd HH:mm:ss") : Paths.AhkScript + " missing");
            foreach (var p in Components.OurAhkProcesses()) Line($"running: pid {p.Id}");
            if (!File.Exists(Paths.AhkScript)) return;
            var lines = File.ReadAllLines(Paths.AhkScript);
            Line($"{lines.Count(l => l.StartsWith("Hotkey("))} hotkeys");
            // The keys the fault reports were about.
            foreach (var key in new[] { "\"$#!l\"", "\"$#j\"", "\"$#^d\"", "\"$#+s\"", "\"$PrintScreen\"", "\"$#Space\"", "LWin" })
                foreach (var l in lines.Where(l => l.Contains(key))) Line(l);
        });
        Guard("Flow Launcher", () =>
        {
            var exe = ToolCatalog.Detect(ToolCatalog.Flow, loc, s, AppState.Load()).Path;
            var settings = FlowTheme.SettingsFile(loc, exe);
            Line($"{exe ?? "not found"} · {settings}{(File.Exists(settings) ? "" : " (missing)")}");
            if (File.Exists(settings))
            {
                using var doc = JsonDocument.Parse(FileSafety.ReadShared(settings), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                foreach (var key in new[] { "Hotkey", "Theme", "ColorScheme", "BackdropType" })
                    if (doc.RootElement.TryGetProperty(key, out var v)) Line($"{key}: {v}");
            }
            var logs = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(settings)!)!, "Logs");
            var newest = Directory.Exists(logs)
                ? Directory.EnumerateFiles(logs, "*.txt", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTime).FirstOrDefault()
                : null;
            if (newest == null) { Line("no Flow log found"); return; }
            Line("Flow log " + newest + ", last lines:");
            foreach (var l in FileSafety.ReadShared(newest).Split('\n').TakeLast(60)) Line("  " + l.TrimEnd());
        });
        Guard("Taskbar", () =>
        {
            Line($"auto-hide: {Taskbar.IsAutoHide()} · visible: {Taskbar.IsVisible()} · hide wanted: {s.HideTaskbar}");
        });
        Guard("Bar", () =>
        {
            Line($"chosen: {Integrations.Bars.Chosen(s, loc)} (setting: {s.Bar ?? "not chosen"}) · shown: {s.ShowBar}");
            Line($"YASB config: {(Integrations.YasbBar.IsInstalled(loc) ? "WinMarchy's" : File.Exists(loc.YasbConfig) ? "someone else's" : "missing")} · {loc.YasbDir}");
            // YASB writes what it did not like in its config into its own log.
            var yasbLog = Path.Combine(loc.YasbDir, "yasb.log");
            if (File.Exists(yasbLog))
            {
                Line("yasb.log, last lines:");
                foreach (var l in FileSafety.ReadShared(yasbLog).Split('\n').TakeLast(40)) Line("  " + l.TrimEnd());
            }
            if (Integrations.ZebarPack.IsInstalled(loc))
            {
                Line(File.Exists(loc.ZebarSettings) ? FileSafety.ReadShared(loc.ZebarSettings).Trim() : loc.ZebarSettings + " missing");
                var config = Path.Combine(loc.ZebarPackDir, "config.js");
                Line(File.Exists(config) ? FileSafety.ReadShared(config).Trim() : config + " missing");
            }
        });
        Guard("Shell and Explorer extras", () =>
        {
            Line($"shell start script: {(File.Exists(Paths.ShellScript) ? Paths.ShellScript : "not set up")} · Fastfetch config: {(File.Exists(Paths.FastfetchConfig) ? "yes" : "no")}");
            Line($"execution policy: Windows PowerShell {Integrations.PowerShellPolicy.WindowsPowerShell()}, PowerShell 7 {Integrations.PowerShellPolicy.BlocksProfiles() ?? "RemoteSigned (default)"}");
            foreach (var profile in new[] { loc.PowerShellProfile, loc.WindowsPowerShellProfile })
                Line($"{profile}: {(File.Exists(profile) && FileSafety.ReadShared(profile).Contains("WINMARCHY", StringComparison.Ordinal) ? "WinMarchy block" : File.Exists(profile) ? "no WinMarchy block" : "missing")}");
            Line($"location for desktop apps (Wi-Fi list): {(LocationAccess.DesktopAppsAllowed() ? "allowed" : "off")}");
            if (OperatingSystem.IsWindows())
                Line("ExplorerBlurMica: " + (Integrations.ExplorerBlur.RegisteredDll(new WindowsUserRegistry()) ?? "not registered for this user"));
        });
        Guard("Greenshot", () =>
        {
            var exe = ToolCatalog.Detect(ToolCatalog.Greenshot, loc, s, AppState.Load()).Path;
            var ini = Screenshots.GreenshotIni(exe, loc);
            Line($"{exe ?? "not found"} · {ini ?? "no Greenshot.ini"}");
            if (ini != null) Line("region key: " + Screenshots.GreenshotHotkey(FileSafety.ReadShared(ini), "region"));
        });
        Guard("Warnings and errors", () =>
        {
            foreach (var e in Log.Recent(120, LogLevel.Warning)) Line(e.ToString());
        });
        return sb.ToString();
    }
}

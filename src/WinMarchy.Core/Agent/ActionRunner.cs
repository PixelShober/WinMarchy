using System.Diagnostics;
using WinMarchy.Core.GlazeWm;
using WinMarchy.Core.Hotkeys;
using WinMarchy.Core.Integrations;
using WinMarchy.Core.Platform;
using WinMarchy.Core.Tools;

namespace WinMarchy.Core.Agent;

/// <summary>What the running WinMarchy offers to actions: GlazeWM, menus, notifications, toggles.</summary>
public interface IAgentHost
{
    GlazeIpc? Glaze { get; }
    LayoutEngine? Layouts { get; }
    WindowCommands? Windows { get; }
    AppLauncher Apps { get; }
    UserSettings Settings { get; }

    /// <summary>WinMarchy is paused: Windows looks and works as without it, only the menu and the pause key work.</summary>
    bool Paused { get; }

    void Notify(string text);
    void ShowMenu(string? name);
    void ShowThemeMenu();
    void ShowKeybindings();
    void OpenMain(string? page);
    void ToggleTaskbar();
    void ToggleBar();
    void ToggleKeepAwake();
    Task NextWallpaperAsync(CancellationToken ct);
    Task ToggleGapsAsync(CancellationToken ct);
    Task ReloadGlazeAsync(CancellationToken ct);

    /// <summary>Pauses WinMarchy, or lets it go on.</summary>
    Task TogglePauseAsync(CancellationToken ct);
}

/// <summary>
/// Runs one action ("wm:…", "app:…", "do:…" …), no matter whether it came from AutoHotkey, GlazeWM,
/// the bar, a menu or the command line. Every action is parsed and checked again here; a sender can
/// only pick from the same list the hotkey editor offers.
/// </summary>
public sealed class ActionRunner
{
    readonly IAgentHost _host;

    public ActionRunner(IAgentHost host) => _host = host;

    public async Task RunAsync(string text, CancellationToken ct)
    {
        if (!HotkeyAction.TryParse(text, out var action, out var error))
        {
            Log.Warn(L.T("actions.rejected", ("action", text), ("detail", error)));
            return;
        }
        if (_host.Paused && !WorksWhilePaused(action))
        {
            Log.Trace("action while paused, not run: " + action, "action");
            _host.Notify(L.T("pause.blocked"));
            return;
        }
        var clock = Stopwatch.StartNew();
        Log.Trace("action " + action, "action");
        try
        {
            switch (action.Kind)
            {
                case ActionKind.Wm: await WmAsync(action, ct); break;
                case ActionKind.App: await AppAsync(action.AppId, action.AppUrls, ct); break;
                case ActionKind.Web: _host.Apps.Web(action.Value); break;
                case ActionKind.Open: AppLauncher.Open(action.Value); break;
                case ActionKind.Send: KeySender.Send(KeyCombo.Parse(action.Value)); break;
                case ActionKind.Run: Run(action.Value); break;
                case ActionKind.Do: await DoAsync(action.Value, ct); break;
            }
        }
        catch (WinMarchyException ex) when (ex.Code is "glazeNotConnected" or "glazeNoAnswer")
        {
            // GlazeWM hangs or restarts: the agent says so once and takes care of it; every key pressed meanwhile would only repeat it.
            Log.Debug(L.T("actions.failed", ("action", text), ("detail", ex.Message)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn(L.T("actions.failed", ("action", text), ("detail", Net.Describe(ex))));
        }
        finally
        {
            Log.Trace($"action {action} done in {clock.ElapsedMilliseconds} ms", "action");
        }
    }

    /// <summary>"do:…" functions that leave Windows' look and the windows alone.</summary>
    static readonly IReadOnlySet<string> HarmlessCommands = new HashSet<string>(StringComparer.Ordinal)
    {
        "menu", "keybindings", "pause", "lock", "copy", "paste", "show-time", "show-battery", "screenshot", "about", "keep-awake", "scale-up", "scale-down",
    };

    /// <summary>
    /// What still runs while WinMarchy is paused: its menus (not the wallpaper one), the pause key,
    /// programs and links, keys sent on, and functions that change nothing of WinMarchy's look or the
    /// windows. Not: GlazeWM's commands, the launcher (it is off), bar, taskbar, theme and wallpaper.
    /// </summary>
    public static bool WorksWhilePaused(HotkeyAction action) => action.Kind switch
    {
        ActionKind.Wm => false,
        ActionKind.App => action.AppId != "launcher",
        ActionKind.Do => HarmlessCommands.Contains(action.Value.Split(' ', 2)[0]) && action.Value != "menu wallpaper",
        _ => true,
    };

    async Task WmAsync(HotkeyAction action, CancellationToken ct)
    {
        if (_host.Glaze is { Connected: true } ipc)
        {
            if (action.WmCommands.Select(GlazeConfig.WorkspaceOf).Max() is { } highest && !await WorkspaceExistsAsync(ipc, highest, ct)) return;
            foreach (var command in action.WmCommands) await ipc.CommandAsync(command, ct);
            return;
        }
        // No connection (GlazeWM restarting): its own command line still works.
        var glaze = ToolCatalog.GlazeCli(ToolCatalog.Detect(ToolCatalog.GlazeWm, Locations.Current, _host.Settings, AppState.Load()).Path);
        if (glaze == null)
        {
            _host.Notify(L.T("actions.glazeMissing"));
            return;
        }
        foreach (var command in action.WmCommands)
        {
            var psi = new ProcessStartInfo(glaze) { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("command");
            foreach (var part in command.Split(' ', StringSplitOptions.RemoveEmptyEntries)) psi.ArgumentList.Add(part);
            Process.Start(psi)?.Dispose();
        }
    }

    /// <summary>
    /// Whether workspace <paramref name="number"/> exists with the screens connected now (five on one
    /// screen, six on more). If not, the user is told, and GlazeWM is not asked for it.
    /// </summary>
    async Task<bool> WorkspaceExistsAsync(GlazeIpc ipc, int number, CancellationToken ct)
    {
        if (number <= GlazeConfig.WorkspaceLimit(1)) return true;
        var screens = GlazeTree.List(await ipc.QueryAsync("monitors", ct), "monitors").Count();
        int limit = GlazeConfig.WorkspaceLimit(screens);
        if (number <= limit) return true;
        _host.Notify(L.T(screens <= 1 ? "tiling.workspaceLimitOne" : "tiling.workspaceLimit", ("number", number), ("count", limit)));
        return false;
    }

    async Task AppAsync(string id, IReadOnlyList<string> urls, CancellationToken ct)
    {
        if (id == "winmarchy")
        {
            _host.OpenMain(null);
            return;
        }
        if (id == "launcher")
        {
            await LauncherAsync(ct);
            return;
        }
        if (!_host.Apps.Launch(id, urls)) _host.Notify(L.T("actions.appMissing", ("app", L.T("apps." + id))));
    }

    /// <summary>
    /// Fluent Search listens on Alt+F24 (set by WinMarchy). If it is not running yet, it is started first.
    /// </summary>
    async Task LauncherAsync(CancellationToken ct)
    {
        var tool = ToolCatalog.FluentSearch;
        if (Process.GetProcessesByName(Integrations.FluentSearchConfig.Process).Length == 0)
        {
            var exe = ToolCatalog.Detect(tool, Locations.Current, _host.Settings, AppState.Load()).Path;
            if (exe == null)
            {
                _host.Notify(L.T("actions.launcherMissing", ("tool", tool.Name)));
                return;
            }
            AppLauncher.Start(exe);
            // It takes its hotkey only once it has loaded.
            await Task.Delay(3000, ct);
        }
        KeySender.Send(KeyCombo.Parse("Alt+F24"));
    }

    void Run(string value)
    {
        var path = value.Trim();
        if (path.Length == 0 || !File.Exists(Environment.ExpandEnvironmentVariables(path)))
        {
            _host.Notify(L.T("actions.scriptMissing"));
            return;
        }
        path = Environment.ExpandEnvironmentVariables(path);
        if (path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            // Runs under the machine's execution policy; WinMarchy never bypasses it.
            var shell = ToolCatalog.Detect(ToolCatalog.PowerShell, Locations.Current, _host.Settings, AppState.Load()).Path ?? "powershell.exe";
            AppLauncher.Start(shell, $"-NoProfile -File \"{path}\"");
        }
        else AppLauncher.Start(path);
    }

    /// <summary>Omarchy's "About": Fastfetch's system information in a terminal that stays open.</summary>
    void About()
    {
        var fastfetch = ToolCatalog.Detect(ToolCatalog.Fastfetch, Locations.Current, _host.Settings, AppState.Load()).Path;
        if (fastfetch == null)
        {
            _host.Notify(L.T("actions.fastfetchMissing"));
            return;
        }
        static string Q(string path) => "'" + path.Replace("'", "''") + "'";
        var config = File.Exists(Paths.FastfetchConfig) ? " --config " + Q(Paths.FastfetchConfig) : "";
        if (!_host.Apps.Shell("& " + Q(fastfetch) + config)) _host.Notify(L.T("actions.appMissing", ("app", "Fastfetch")));
    }

    /// <summary>
    /// One of the bar's drop-down menus (YASB). YASB listens for a key combination nobody presses by
    /// hand, and WinMarchy sends it. Without the location for desktop apps, Windows' own Wi-Fi list.
    /// </summary>
    void BarMenu(string name)
    {
        if (!YasbBar.Menus.TryGetValue(name, out var menu))
        {
            _host.Notify(L.T("actions.barMenuUnknown", ("menu", name)));
            return;
        }
        if (name == "network" && !LocationAccess.DesktopAppsAllowed())
        {
            AppLauncher.Open(YasbBar.NetworkListAction["open:".Length..]);
            return;
        }
        if (Process.GetProcessesByName(Bars.Process).Length == 0)
        {
            _host.Notify(L.T("actions.barMenuNoYasb"));
            return;
        }
        if (!Bars.IsOn(_host.Settings, menu.Module))
        {
            _host.Notify(L.T("actions.barMenuOff", ("entry", L.T("bar.modules." + menu.Module))));
            return;
        }
        KeySender.Send(KeyCombo.Parse(menu.Keys));
    }

    /// <summary>
    /// A button of the user's own in the bar (YASB): it calls by number, so the action itself never
    /// passes through cmd.exe. The action is checked like any other.
    /// </summary>
    async Task BarShortcutAsync(string? arg, CancellationToken ct)
    {
        var list = _host.Settings.BarShortcuts;
        if (arg == null || !int.TryParse(arg, out var n) || n < 1 || n > list.Count) return;
        var action = list[n - 1].Action;
        if (action.StartsWith("do:bar-shortcut", StringComparison.OrdinalIgnoreCase)) return;
        await RunAsync(action, ct);
    }

    async Task DoAsync(string value, CancellationToken ct)
    {
        var parts = value.Split(' ', 2, StringSplitOptions.TrimEntries);
        var command = parts[0];
        var arg = parts.Length > 1 ? parts[1] : null;
        switch (command)
        {
            case "menu": _host.ShowMenu(arg); break;
            case "theme-menu": _host.ShowThemeMenu(); break;
            case "keybindings": _host.ShowKeybindings(); break;
            case "wallpaper-next": await _host.NextWallpaperAsync(ct); break;
            case "wallpaper-menu": _host.ShowMenu("wallpaper"); break;
            case "bar-toggle": _host.ToggleBar(); break;
            case "taskbar-toggle": _host.ToggleTaskbar(); break;
            case "keep-awake": _host.ToggleKeepAwake(); break;
            case "pause": await _host.TogglePauseAsync(ct); break;
            case "gaps-toggle": await _host.ToggleGapsAsync(ct); break;
            case "reload": await _host.ReloadGlazeAsync(ct); break;
            case "lock": Win32.LockWorkstation(); break;
            case "copy": KeySender.Send(KeyCombo.Parse(ForegroundIsTerminal() ? "Ctrl+Shift+C" : "Ctrl+C")); break;
            case "paste": KeySender.Send(KeyCombo.Parse(ForegroundIsTerminal() ? "Ctrl+Shift+V" : "Ctrl+V")); break;
            case "show-time": _host.Notify(DateTime.Now.ToString(L.Language == "de" ? "dddd, d. MMMM · HH:mm" : "dddd, MMMM d · h:mm tt", L.Culture)); break;
            case "show-battery": _host.Notify(BatteryText()); break;
            case "screenshot": await Screenshots.TakeAsync(_host.Settings, arg, _host.Notify, ct); break;
            case "about": About(); break;
            case "bar-shortcut": await BarShortcutAsync(arg, ct); break;
            case "bar-menu": BarMenu(arg ?? "logo"); break;
            case "scale-up":
            case "scale-down":
                var scale = DisplayScale.Step(command == "scale-up" ? 1 : -1);
                _host.Notify(scale is { } s ? L.T("actions.scale", ("percent", s)) : L.T("actions.scaleFailed"));
                break;
            default:
                await GlazeDoAsync(command, arg, ct);
                break;
        }
    }

    /// <summary>The functions that need GlazeWM's IPC connection.</summary>
    async Task GlazeDoAsync(string command, string? arg, CancellationToken ct)
    {
        if (_host.Glaze is not { Connected: true } || _host.Layouts is not { } layouts || _host.Windows is not { } windows)
        {
            Log.Trace($"{command}: no GlazeWM connection", "action");
            _host.Notify(L.T("actions.glazeNotRunning"));
            return;
        }
        switch (command)
        {
            // Older profiles still have "layout back"; with three layouts it simply cycles too. Both act on the screen with the focus.
            case "layout-next":
            case "layout-prev":
                await layouts.CycleAsync(ct);
                break;
            case "layout":
                await layouts.SetFocusedScreenAsync(LayoutEngine.Parse(arg), ct);
                break;
            case "float-toggle": await windows.FloatToggleAsync(corner: arg == "corner", ct); break;
            case "scratch-toggle": await windows.ScratchToggleAsync(ct); break;
            case "scratch-store": await windows.ScratchStoreAsync(ct); break;
            case "toggle-split": await windows.ToggleSplitAsync(ct); break;
            case "focus": await windows.FocusAsync(Direction(arg), ct); break;
            case "swap": await windows.SwapAsync(Direction(arg), ct); break;
            case "to-workspace" when arg != null && int.TryParse(arg, out var n) && n >= 1:
                if (n <= GlazeConfig.WorkspaceCount && _host.Glaze is { } glaze && await WorkspaceExistsAsync(glaze, n, ct)) await windows.ToWorkspaceAsync(arg, ct);
                else if (n > GlazeConfig.WorkspaceCount) _host.Notify(L.T("tiling.workspaceLimit", ("number", n), ("count", GlazeConfig.WorkspaceCount)));
                break;
            // "desktop-new" from the time Win+Ctrl+D had only one set of programs.
            case "work-mode" or "desktop-new":
                if (WorkMode.NumberOf("do:" + command + (arg != null ? " " + arg : "")) is not { } number)
                {
                    _host.Notify(L.T("workModes.unknown", ("mode", arg ?? "")));
                    break;
                }
                await new WorkModeRunner(windows, layouts, _host.Notify).RunAsync(number, _host.Settings, action => _ = RunAsync(action, ct), ct);
                break;
            case "desktop-next": await windows.DesktopStepAsync(1, ct); break;
            case "desktop-prev": await windows.DesktopStepAsync(-1, ct); break;
            case "workspace-next": await windows.WorkspaceStepAsync(1, ct); break;
            case "workspace-prev": await windows.WorkspaceStepAsync(-1, ct); break;
            case "move-window-monitor": await windows.MoveWindowToMonitorAsync(arg ?? "right", ct); break;
            case "restore-minimized":
                var restored = await windows.RestoreMinimizedAsync(ct);
                _host.Notify(restored == 0 ? L.T("actions.noneMinimized") : L.T("actions.restoredMinimized", ("count", restored)));
                break;
            case "transparency-toggle": await windows.ToggleTransparencyAsync(ct); break;
        }
    }

    static string Direction(string? arg) => arg is "left" or "right" or "up" or "down" ? arg : "right";

    static bool ForegroundIsTerminal()
    {
        var exe = ForegroundProcess.Name()?.ToLowerInvariant();
        return exe is "windowsterminal" or "wezterm-gui" or "alacritty" or "mintty";
    }

    static string BatteryText()
    {
        if (Power.Battery() is not { } b) return L.T("actions.noBattery");
        if (b.Charging) return L.T("actions.batteryCharging", ("percent", b.Percent));
        return b.Remaining is { } left
            ? L.T("actions.batteryLeft", ("percent", b.Percent), ("hours", (int)left.TotalHours), ("minutes", left.Minutes))
            : L.T("actions.battery", ("percent", b.Percent));
    }
}

/// <summary>Name of the process that owns the window in front.</summary>
public static class ForegroundProcess
{
    public static string? Name()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}

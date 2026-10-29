using System.Diagnostics;
using WinMarchy.Core.GlazeWm;
using WinMarchy.Core.Hotkeys;
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
        Log.Debug("action " + action);
        try
        {
            switch (action.Kind)
            {
                case ActionKind.Wm: await WmAsync(action, ct); break;
                case ActionKind.App: await AppAsync(action.Value, ct); break;
                case ActionKind.Web: _host.Apps.Web(action.Value); break;
                case ActionKind.Open: AppLauncher.Open(action.Value); break;
                case ActionKind.Send: KeySender.Send(KeyCombo.Parse(action.Value)); break;
                case ActionKind.Run: Run(action.Value); break;
                case ActionKind.Do: await DoAsync(action.Value, ct); break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn(L.T("actions.failed", ("action", text), ("detail", Net.Describe(ex))));
        }
    }

    async Task WmAsync(HotkeyAction action, CancellationToken ct)
    {
        if (_host.Glaze is { Connected: true } ipc)
        {
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

    async Task AppAsync(string id, CancellationToken ct)
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
        if (!_host.Apps.Launch(id)) _host.Notify(L.T("actions.appMissing", ("app", L.T("apps." + id))));
    }

    /// <summary>Flow Launcher listens on Alt+F24 (set by WinMarchy). If it is not running yet, it is started first.</summary>
    async Task LauncherAsync(CancellationToken ct)
    {
        if (Process.GetProcessesByName("Flow.Launcher").Length == 0)
        {
            var flow = ToolCatalog.Detect(ToolCatalog.Flow, Locations.Current, _host.Settings, AppState.Load()).Path;
            if (flow == null)
            {
                _host.Notify(L.T("actions.flowMissing"));
                return;
            }
            AppLauncher.Start(flow);
            await Task.Delay(1500, ct);
        }
        KeySender.Send(KeyCombo.Parse("Alt+F24"));
    }

    void Run(string value)
    {
        var path = value.Replace("%personal-script%", _host.Settings.PersonalScriptPath ?? "", StringComparison.OrdinalIgnoreCase).Trim();
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
            case "bar-toggle": _host.ToggleBar(); break;
            case "taskbar-toggle": _host.ToggleTaskbar(); break;
            case "keep-awake": _host.ToggleKeepAwake(); break;
            case "gaps-toggle": await _host.ToggleGapsAsync(ct); break;
            case "reload": await _host.ReloadGlazeAsync(ct); break;
            case "lock": Win32.LockWorkstation(); break;
            case "copy": KeySender.Send(KeyCombo.Parse(ForegroundIsTerminal() ? "Ctrl+Shift+C" : "Ctrl+C")); break;
            case "paste": KeySender.Send(KeyCombo.Parse(ForegroundIsTerminal() ? "Ctrl+Shift+V" : "Ctrl+V")); break;
            case "show-time": _host.Notify(DateTime.Now.ToString(L.Language == "de" ? "dddd, d. MMMM · HH:mm" : "dddd, MMMM d · h:mm tt", L.Culture)); break;
            case "show-battery": _host.Notify(BatteryText()); break;
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
            _host.Notify(L.T("actions.glazeNotRunning"));
            return;
        }
        switch (command)
        {
            case "layout-next": await layouts.CycleAsync(back: false, ct); break;
            case "layout-prev": await layouts.CycleAsync(back: true, ct); break;
            case "layout":
                var ws = await _host.Glaze.QueryAsync("workspaces", ct);
                if (GlazeTree.FocusedWorkspace(ws) is { } f && GlazeTree.Str(f, "name") is { } name)
                    await layouts.SetAsync(name, LayoutEngine.Parse(arg), ct);
                break;
            case "scratch-toggle": await windows.ScratchToggleAsync(ct); break;
            case "scratch-store": await windows.ScratchStoreAsync(ct); break;
            case "workspace-next": await windows.WorkspaceStepAsync(1, ct); break;
            case "workspace-prev": await windows.WorkspaceStepAsync(-1, ct); break;
            case "move-window-monitor": await windows.MoveWindowToMonitorAsync(arg ?? "right", ct); break;
            case "restore-minimized": await windows.RestoreMinimizedAsync(ct); break;
            case "transparency-toggle": await windows.ToggleTransparencyAsync(ct); break;
        }
    }

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

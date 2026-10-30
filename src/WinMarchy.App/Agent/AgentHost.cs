using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WinMarchy.App.Overlays;
using WinMarchy.Core;
using WinMarchy.Core.Agent;
using WinMarchy.Core.GlazeWm;
using WinMarchy.Core.Hotkeys;
using WinMarchy.Core.Platform;
using WinMarchy.Core.Updates;
using Forms = System.Windows.Forms;

namespace WinMarchy.App.Agent;

/// <summary>
/// WinMarchy while it runs in the background (tray): receives the shortcuts from AutoHotkey, holds the
/// connection to GlazeWM for layouts and scratchpad, keeps the taskbar hidden, starts GlazeWM, the bar
/// and the hotkey script at login and installs updates when nobody is looking.
/// </summary>
public sealed class AgentHost : IAgentHost, IDisposable
{
    readonly ActionRunner _runner;
    readonly CancellationTokenSource _cts = new();
    readonly DispatcherTimer _taskbarTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(6) };
    readonly Forms.NotifyIcon _tray = new();
    readonly TaskbarGuard _guard = new();
    readonly DispatcherTimer _flowTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    DateTime _flowStarted = DateTime.MinValue;
    HwndSource? _window;
    LayoutEngine? _layouts;
    WindowCommands? _windows;
    bool _keepAwake;
    bool _gapsOff;
    bool _taskbarShownByUser;
    bool _restoring;
    bool _taskbarHidden;
    bool _setupDone = AppState.Load().SetupDone;
    string _status = "";
    int _glazeFailures;
    string? _lastGlazeError;
    DateTime _lastGlazeRestart = DateTime.MinValue;
    bool _hangReported;

    public AgentHost()
    {
        _runner = new ActionRunner(this);
        Apps = new AppLauncher(Locations.Current, () => AppModel.Settings);
    }

    public GlazeIpc? Glaze { get; private set; }
    public LayoutEngine? Layouts => _layouts;
    public WindowCommands? Windows => _windows;
    public AppLauncher Apps { get; }
    public UserSettings Settings => AppModel.Settings;

    /// <summary>GlazeWM runs, but WinMarchy cannot reach it (after two tries): the reason, else null.</summary>
    public string? GlazeProblem { get; private set; }

    /// <summary>Cached: the taskbar check runs twice a second and should not read a file each time.</summary>
    bool SetupDone => _setupDone;

    // ------------------------------------------------------------------ start and stop

    public void Start()
    {
        CreateMessageWindow();
        CreateTray();
        _taskbarTimer.Tick += (_, _) => EnforceTaskbar();
        _taskbarTimer.Start();
        AppModel.SettingsChanged += EnforceTaskbar;
        _ = Task.Run(() => GlazeLoopAsync(_cts.Token));

        if (SetupDone) StartComponents();
        HookMinimize();
        // The program list for Win+Space's search, read once in the background.
        StartApps.Refresh();
        _flowTimer.Tick += (_, _) => KeepFlowRunning();
        _flowTimer.Start();
        try { AppModel.Engine.CheckFilesDefault(); }
        catch (Exception ex) { Log.Warn(Net.Describe(ex)); }

        // The first check waits until login is over; then every six hours.
        _updateTimer.Tick += (_, _) => _ = CheckUpdatesAsync();
        _updateTimer.Start();
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        first.Tick += (_, _) =>
        {
            first.Stop();
            _ = CheckUpdatesAsync();
        };
        first.Start();
        AppModel.Updater.Changed += s => Application.Current?.Dispatcher.InvokeAsync(() => OnUpdate(s));
        Log.Info(L.T("agent.started", ("version", AppInfo.Version)));
    }

    /// <summary>GlazeWM, the bar and the hotkey script, whichever is not running yet.</summary>
    void StartComponents()
    {
        var c = AppModel.Engine.Components;
        try
        {
            if (!c.StartGlaze()) Log.Info(L.T("agent.glazeMissing"));
            if (Settings.ShowBar && !c.BarRunning) c.StartBar();
            if (Settings.HotkeyEngine == "autohotkey" && File.Exists(Paths.AhkScript) && !Core.Services.Components.OurAhkProcesses().Any()) c.StartAhk();
            // Print belongs to Greenshot when the user chose it; it has to run for that.
            if (Settings.ScreenshotTool == Screenshots.Greenshot && !Core.Services.Components.IsRunning("Greenshot") &&
                c.Status(Core.Tools.ToolCatalog.Greenshot).Path is { } greenshot) AppLauncher.Start(greenshot);
            // Win opens Flow Launcher; after the setup it has never run yet.
            Log.Trace($"components: glazewm {Core.Services.Components.IsRunning("glazewm")}, {c.Bar} {c.BarRunning}, " +
                      $"ahk {Core.Services.Components.OurAhkProcesses().Any()}, flow {Core.Services.Components.IsRunning("Flow.Launcher")}", "agent");
            if (c.StartFlow())
            {
                _flowStarted = DateTime.Now;
                Log.Info(L.T("agent.flowStarted"));
            }
        }
        catch (Exception ex)
        {
            Log.Warn(L.T("agent.startFailed", ("detail", Net.Describe(ex))));
        }
    }

    /// <summary>
    /// Win alone opens Flow Launcher, so it has to run. If it is gone (crashed, or a restart after a
    /// theme change did not come up), it is started again; not more than every 30 seconds, and not while
    /// a job runs (a theme change restarts it on purpose, the setup installs it).
    /// </summary>
    void KeepFlowRunning()
    {
        if (!SetupDone || _restoring || AppModel.Jobs.AnyRunning) return;
        if (Core.Services.Components.IsRunning("Flow.Launcher")) return;
        if (DateTime.Now - _flowStarted < TimeSpan.FromSeconds(30))
        {
            Log.Trace("flow: not running, started less than 30 s ago, waiting", "flow");
            return;
        }
        try
        {
            if (AppModel.Engine.Components.StartFlow())
            {
                _flowStarted = DateTime.Now;
                Log.Info(L.T("agent.flowRestarted"));
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Flow: " + Net.Describe(ex));
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _flowTimer.Stop();
        _guard.Dispose();
        _taskbarTimer.Stop();
        _updateTimer.Stop();
        // Without WinMarchy nobody brings the taskbar back after Explorer restarts: show it again.
        if (_taskbarHidden) ShowTaskbarAgain();
        if (_keepAwake) Power.KeepAwake(false);
        if (_minimizeHook != IntPtr.Zero) UnhookWinEvent(_minimizeHook);
        _tray.Visible = false;
        _tray.Dispose();
        _window?.Dispose();
        if (Glaze is { } g) _ = g.DisposeAsync().AsTask();
    }

    // ------------------------------------------------------------------ messages from AutoHotkey and second starts

    public const long ActionMessage = 0x4D4E4957;   // "WINM"
    public const long OpenMessage = 0x4D4E4950;     // "PINM": bring the window up, optional page
    public const long QuitMessage = 0x4D4E4951;     // "QINM": a newer WinMarchy takes over

    void CreateMessageWindow()
    {
        // A hidden top-level window (not message-only: AutoHotkey's WinExist must find it by title).
        var p = new HwndSourceParameters(AhkScript.AgentTitle) { Width = 0, Height = 0, WindowStyle = 0, ExtendedWindowStyle = 0x80 /* WS_EX_TOOLWINDOW */ };
        _window = new HwndSource(p);
        _window.AddHook(WndProc);
        // If WinMarchy ever runs elevated, AutoHotkey (not elevated) may still talk to it.
        ChangeWindowMessageFilterEx(_window.Handle, WM_COPYDATA, 1 /* MSGFLT_ALLOW */, IntPtr.Zero);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_COPYDATA) return IntPtr.Zero;
        var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
        var kind = cds.dwData.ToInt64();
        if (kind != ActionMessage && kind != OpenMessage && kind != QuitMessage) return IntPtr.Zero;
        var chars = Math.Min(cds.cbData / 2, 4096);
        var text = chars > 0 && cds.lpData != IntPtr.Zero ? Marshal.PtrToStringUni(cds.lpData, chars).TrimEnd('\0').Trim() : "";
        handled = true;
        Log.Trace((kind == ActionMessage ? "received " : kind == QuitMessage ? "asked to quit" : "open window ") + text, "agent");
        // Answer at once (AutoHotkey waits for the reply); the work happens right after.
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (kind == QuitMessage)
            {
                Log.Info(L.T("install.handover"));
                App.Quit();
            }
            else if (kind == ActionMessage) RunAction(text);
            else App.ShowMain(text.Length > 0 ? text : null);
        });
        return new IntPtr(1);
    }

    public void RunAction(string action) => _ = _runner.RunAsync(action, _cts.Token);

    public Task RunActionAsync(string action) => _runner.RunAsync(action, _cts.Token);

    // ------------------------------------------------------------------ tray

    void CreateTray()
    {
        try
        {
            var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/winmarchy.ico"))?.Stream;
            if (stream != null) _tray.Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        }
        catch
        {
            _tray.Icon = System.Drawing.SystemIcons.Application;
        }
        _tray.Text = "WinMarchy";
        var menu = new Forms.ContextMenuStrip { RenderMode = Forms.ToolStripRenderMode.System };
        void Add(string key, Action click) => menu.Items.Add(L.T(key), null, (_, _) => Application.Current.Dispatcher.InvokeAsync(click));
        Add("tray.open", () => App.ShowMain(null));
        Add("tray.menu", () => MenuWindow.ShowMenu(null));
        Add("tray.keybindings", MenuWindow.ShowKeybindings);
        Add("tray.theme", MenuWindow.ShowThemes);
        menu.Items.Add(new Forms.ToolStripSeparator());
        Add("tray.reload", () => _ = ReloadGlazeAsync(_cts.Token));
        Add("tray.bar", ToggleBar);
        Add("tray.taskbar", ToggleTaskbar);
        menu.Items.Add(new Forms.ToolStripSeparator());
        Add("tray.quit", App.Quit);
        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) Application.Current.Dispatcher.InvokeAsync(() => App.ShowMain(null));
        };
        _tray.Visible = true;
    }

    // ------------------------------------------------------------------ GlazeWM

    /// <summary>Connects whenever GlazeWM is up; after a reload or restart it simply connects again.</summary>
    async Task GlazeLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var glaze = Glaze;
                if (glaze is not { Connected: true })
                {
                    if (Core.Services.Components.IsRunning("glazewm")) await ConnectAsync(ct);
                    else
                    {
                        _glazeFailures = 0;
                        GlazeProblem = null;
                    }
                }
                else if (glaze.Unanswered >= 2) await HandleStallAsync(glaze, ct);
                else await RefreshStatusAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Debug("GlazeWM: " + Net.Describe(ex));
            }
            try { await Task.Delay(TimeSpan.FromSeconds(2), ct); } catch (OperationCanceledException) { return; }
        }
    }

    async Task ConnectAsync(CancellationToken ct)
    {
        var ipc = new GlazeIpc();
        if (!await ipc.ConnectAsync(ct))
        {
            GlazeFailed(ipc.LastError ?? "?");
            await ipc.DisposeAsync();
            return;
        }
        // Layouts and the window functions take turns: a multi-step command is never interleaved
        // with the layout engine's reaction to the events it causes.
        var gate = new SemaphoreSlim(1, 1);
        var layouts = new LayoutEngine(ipc, () => LayoutEngine.Parse(Settings.DefaultLayout), Notify, gate, () => Settings);
        var windows = new WindowCommands(ipc, Notify, gate, layouts, () => Settings);
        ipc.Event += e => _ = layouts.OnEventAsync(e, ct);
        ipc.Event += e => _ = windows.OnEventAsync(e, ct);
        ipc.Disconnected += () =>
        {
            if (Glaze != ipc) return;
            Glaze = null;
            _layouts = null;
            _windows = null;
            Log.Info(L.T("agent.glazeDisconnected"));
        };
        try
        {
            await ipc.SubscribeAsync(LayoutEngine.Events, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            GlazeFailed(Net.Describe(ex));
            await ipc.DisposeAsync();
            return;
        }
        _layouts = layouts;
        _windows = windows;
        Glaze = ipc;
        _glazeFailures = 0;
        _lastGlazeError = null;
        GlazeProblem = null;
        Log.Info(L.T("agent.glazeConnected"));
        try
        {
            await windows.RecoverAsync(ct);
            await layouts.ForgetClosedAsync(ct);
            await windows.EnforceMonitorsAsync(Settings.WorkspaceMonitors, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Log.Debug("Recover: " + Net.Describe(ex)); }
        await RefreshStatusAsync(ct);
    }

    /// <summary>
    /// GlazeWM has left two requests in a row unanswered. If it answers on a new connection, only ours
    /// was stuck: connect again. If not, GlazeWM hangs and every shortcut would wait for nothing, so it
    /// is restarted – at most once in five minutes; a second hang soon after is only reported.
    /// </summary>
    async Task HandleStallAsync(GlazeIpc ipc, CancellationToken ct)
    {
        if (await GlazeIpc.ProbeAsync(ct))
        {
            Log.Warn(L.T("agent.glazeReconnecting"));
            await ipc.DisposeAsync();
            return;
        }
        if (DateTime.Now - _lastGlazeRestart < TimeSpan.FromMinutes(5))
        {
            if (_hangReported) return;
            _hangReported = true;
            Log.Warn(L.T("agent.glazeHungAgain"));
            Notify(L.T("agent.glazeHungAgain"));
            return;
        }
        await RestartHungGlazeAsync(ipc, ct);
    }

    async Task RestartHungGlazeAsync(GlazeIpc ipc, CancellationToken ct)
    {
        _lastGlazeRestart = DateTime.Now;
        _hangReported = false;
        Log.Warn(L.T("agent.glazeHung"));
        Notify(L.T("agent.glazeRestarting"));
        await ipc.DisposeAsync();
        if (!await AppModel.Engine.Components.RestartGlazeAsync(ct)) Notify(L.T("actions.glazeMissing"));
    }

    /// <summary>Each new reason goes to the log once; the window shows it from the second failure on.</summary>
    void GlazeFailed(string detail)
    {
        _glazeFailures++;
        if (detail != _lastGlazeError)
        {
            _lastGlazeError = detail;
            Log.Warn(L.T("agent.glazeConnectFailed", ("detail", detail)));
        }
        if (_glazeFailures >= 2) GlazeProblem = detail;
    }

    async Task RefreshStatusAsync(CancellationToken ct)
    {
        if (Glaze is not { Connected: true } ipc || _layouts is not { } layouts) return;
        var data = await ipc.QueryAsync("workspaces", ct);
        if (GlazeTree.FocusedWorkspace(data) is { } ws && GlazeTree.Str(ws, "name") is { } name)
            _status = L.T("status.connected", ("workspace", name), ("layout", LayoutEngine.Name(layouts.ModeOf(name))));
    }

    /// <summary>The line under the logo: workspace and layout.</summary>
    public string StatusLine() => _status.Length > 0 ? _status : L.T("status.glazeConnected");

    public async Task ReloadGlazeAsync(CancellationToken ct)
    {
        if (!Core.Services.Components.IsRunning("glazewm"))
        {
            if (!AppModel.Engine.Components.StartGlaze()) Notify(L.T("actions.glazeMissing"));
            return;
        }
        try
        {
            await AppModel.Engine.Components.ReloadGlazeAsync(Glaze, ct);
            Notify(L.T("agent.reloaded"));
        }
        catch (WinMarchyException ex) when (ex.Code == "glazeNoAnswer" && Glaze is { } hung)
        {
            // Reloading is what one tries when nothing moves any more: if GlazeWM hangs, restart it.
            await RestartHungGlazeAsync(hung, ct);
        }
    }

    public async Task ToggleGapsAsync(CancellationToken ct)
    {
        if (!SetupDone)
        {
            Notify(L.T("agent.setupFirst"));
            return;
        }
        _gapsOff = !_gapsOff;
        await AppModel.Engine.SetGapsAsync(_gapsOff, ct);
        Notify(_gapsOff ? L.T("agent.gapsOff") : L.T("agent.gapsOn"));
    }

    /// <summary>Tiling page: config, reload, then taskbar and bar as the switches say.</summary>
    public async Task ApplyTilingAsync()
    {
        _gapsOff = false;
        await AppModel.Engine.ApplyTilingAsync(_cts.Token);
        // Workspaces already open on the wrong screen move to the one they are now pinned to.
        if (_windows is { } windows)
        {
            try { await windows.EnforceMonitorsAsync(Settings.WorkspaceMonitors, _cts.Token); }
            catch (Exception ex) when (ex is not OperationCanceledException) { Log.Warn(L.T("tiling.monitorsFailed", ("detail", Net.Describe(ex)))); }
        }
        _taskbarShownByUser = false;
        EnforceTaskbar();
        var running = AppModel.Engine.Components.BarRunning;
        if (Settings.ShowBar && !running && SetupDone) AppModel.Engine.Components.StartBar();
        else if (!Settings.ShowBar && running) AppModel.Engine.Components.StopBar();
    }

    // ------------------------------------------------------------------ taskbar, bar, awake

    /// <summary>
    /// Hidden while the setting is on and the setup is done. The auto-hide state from before WinMarchy
    /// is written down once, so "restore everything" and quitting put it back.
    /// </summary>
    void EnforceTaskbar()
    {
        bool want = Settings.HideTaskbar && !_taskbarShownByUser && !_restoring && SetupDone;
        if (want)
        {
            if (!_taskbarHidden)
            {
                AppState.Update(s => s.TaskbarAutoHideBefore ??= Taskbar.IsAutoHide());
                Taskbar.SetAutoHide(true);
                _taskbarHidden = true;
            }
            // The guard's own thread does the hiding, also while this one is busy.
            _guard.Hide = true;
        }
        else if (_taskbarHidden)
        {
            ShowTaskbarAgain();
        }
    }

    void ShowTaskbarAgain()
    {
        _guard.Hide = false;
        Taskbar.Show();
        if (AppState.Load().TaskbarAutoHideBefore is { } before) Taskbar.SetAutoHide(before);
        _taskbarHidden = false;
    }

    public void ToggleTaskbar()
    {
        if (!Settings.HideTaskbar)
        {
            Notify(L.T("agent.taskbarSettingOff"));
            return;
        }
        _taskbarShownByUser = !_taskbarShownByUser;
        EnforceTaskbar();
        Notify(_taskbarShownByUser ? L.T("agent.taskbarShown") : L.T("agent.taskbarHidden"));
    }

    public void ToggleBar()
    {
        var c = AppModel.Engine.Components;
        if (c.BarRunning)
        {
            c.StopBar();
            return;
        }
        if (!c.StartBar()) Notify(L.T("agent.barMissing", ("tool", Core.Integrations.Bars.Tool(c.Bar).Name)));
    }

    public void ToggleKeepAwake()
    {
        _keepAwake = !_keepAwake;
        Application.Current.Dispatcher.Invoke(() => Power.KeepAwake(_keepAwake));
        Notify(_keepAwake ? L.T("agent.awakeOn") : L.T("agent.awakeOff"));
    }

    public async Task NextWallpaperAsync(CancellationToken ct)
    {
        var name = await Task.Run(AppModel.Engine.NextWallpaper, ct);
        Notify(name != null ? L.T("agent.wallpaper", ("name", Path.GetFileNameWithoutExtension(name))) : L.T("agent.noWallpapers"));
    }

    // ------------------------------------------------------------------ menus and window

    public void Notify(string text) => Osd.Show(text);

    public void ShowMenu(string? name) => MenuWindow.ShowMenu(name);

    public void ShowThemeMenu() => MenuWindow.ShowThemes();

    public void ShowKeybindings() => MenuWindow.ShowKeybindings();

    public void OpenMain(string? page) => Application.Current.Dispatcher.InvokeAsync(() => App.ShowMain(page));

    // ------------------------------------------------------------------ setup and restore

    /// <summary>The setup has just finished: start what is not running yet and connect.</summary>
    public void AfterSetup()
    {
        _setupDone = AppState.Load().SetupDone;
        if (!_setupDone) return;
        StartComponents();
        _taskbarShownByUser = false;
        EnforceTaskbar();
    }

    /// <summary>
    /// Before a full restore: stop hiding the taskbar, and if WinMarchy's own copy of GlazeWM is to be
    /// removed, end it (a running exe cannot be deleted).
    /// </summary>
    public void PrepareForRestore(bool removeTools)
    {
        _restoring = true;
        EnforceTaskbar();
        if (removeTools && AppState.Load().InstalledTools.Contains("glazewm") && Glaze is { Connected: true } ipc)
        {
            // Off the UI thread: the connection's awaits must not wait for this (blocked) thread.
            try { Task.Run(() => ipc.CommandAsync("wm-exit", CancellationToken.None)).Wait(3000); } catch { }
        }
    }

    public void AfterRestoreAll()
    {
        _setupDone = AppState.Load().SetupDone;
        _restoring = false;
        _gapsOff = false;
        _taskbarHidden = false;
        Notify(L.T("agent.restored"));
    }

    // ------------------------------------------------------------------ minimizing like Omarchy

    WinEventProc? _minimizeProc;
    IntPtr _minimizeHook;

    /// <summary>Windows reports every minimize; with the setting on, the window comes straight back.</summary>
    void HookMinimize()
    {
        _minimizeProc = OnMinimizeStart;
        _minimizeHook = SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZESTART, IntPtr.Zero, _minimizeProc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    /// <summary>
    /// Omarchy has no minimizing: a window GlazeWM manages is restored into its tile. Windows that hide
    /// themselves in the tray (no longer visible) and windows GlazeWM ignores stay as they are.
    /// </summary>
    void OnMinimizeStart(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) return;
        if (!Settings.BlockMinimize || _windows is not { } windows) return;
        var handle = hwnd.ToInt64();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, _cts.Token);
                var h = new IntPtr(handle);
                if (!IsIconic(h) || !IsWindowVisible(h) || !await windows.ManagesAsync(handle, _cts.Token)) return;
                Log.Trace($"minimize blocked: hwnd {handle}", "agent");
                ShowWindow(h, SW_RESTORE);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { Log.Debug("Minimize: " + Net.Describe(ex)); }
        });
    }

    const int SW_RESTORE = 9;
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);

    // ------------------------------------------------------------------ updates

    async Task CheckUpdatesAsync()
    {
        if (!Settings.AutoUpdate || AppInfo.IsPrerelease) return;
        await AppModel.Updater.CheckAsync(download: true, _cts.Token);
    }

    /// <summary>
    /// A verified update is ready. With the window closed and nothing running, WinMarchy switches
    /// right away (GlazeWM, bar and hotkeys keep running meanwhile); otherwise the window shows a banner.
    /// </summary>
    void OnUpdate(UpdateStatus s)
    {
        if (!s.Ready) return;
        if (App.MainVisible || AppModel.Jobs.AnyRunning)
        {
            if (!App.MainVisible) Notify(L.T("update.readyNotice", ("version", s.Latest)));
            return;
        }
        Log.Info(L.T("update.installingQuietly", ("version", s.Latest)));
        App.RestartForUpdate(background: true);
    }

    // ------------------------------------------------------------------ native

    const int WM_COPYDATA = 0x004A;

    [StructLayout(LayoutKind.Sequential)]
    struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [DllImport("user32.dll")]
    static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, int msg, int action, IntPtr changeInfo);

    const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016, WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;

    delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    static extern bool UnhookWinEvent(IntPtr hook);
}

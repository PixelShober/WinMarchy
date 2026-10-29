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
            if (Settings.ShowBar && !Core.Services.Components.IsRunning("zebar")) c.StartBar();
            if (Settings.HotkeyEngine == "autohotkey" && File.Exists(Paths.AhkScript) && !Core.Services.Components.OurAhkProcesses().Any()) c.StartAhk();
            // Win and Win+Space open Flow Launcher; after the setup it has never run yet.
            if (c.StartFlow()) Log.Info(L.T("agent.flowStarted"));
        }
        catch (Exception ex)
        {
            Log.Warn(L.T("agent.startFailed", ("detail", Net.Describe(ex))));
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
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
        if (kind != ActionMessage && kind != OpenMessage) return IntPtr.Zero;
        var chars = Math.Min(cds.cbData / 2, 4096);
        var text = chars > 0 && cds.lpData != IntPtr.Zero ? Marshal.PtrToStringUni(cds.lpData, chars).TrimEnd('\0').Trim() : "";
        handled = true;
        // Answer at once (AutoHotkey waits for the reply); the work happens right after.
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (kind == ActionMessage) RunAction(text);
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
                if (Glaze is not { Connected: true })
                {
                    if (Core.Services.Components.IsRunning("glazewm")) await ConnectAsync(ct);
                    else
                    {
                        _glazeFailures = 0;
                        GlazeProblem = null;
                    }
                }
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
        var layouts = new LayoutEngine(ipc, () => LayoutEngine.Parse(Settings.DefaultLayout), Notify, gate);
        var windows = new WindowCommands(ipc, Notify, gate);
        ipc.Event += e => _ = layouts.OnEventAsync(e, ct);
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
        await RefreshStatusAsync(ct);
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
        await AppModel.Engine.Components.ReloadGlazeAsync(Glaze, ct);
        Notify(L.T("agent.reloaded"));
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
        _taskbarShownByUser = false;
        EnforceTaskbar();
        var running = Core.Services.Components.IsRunning("zebar");
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
            Taskbar.EnforceHidden();
        }
        else if (_taskbarHidden)
        {
            ShowTaskbarAgain();
        }
    }

    void ShowTaskbarAgain()
    {
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
        if (Core.Services.Components.IsRunning("zebar"))
        {
            c.StopBar();
            return;
        }
        if (c.ZebarExe == null)
        {
            Notify(L.T("agent.zebarMissing"));
            return;
        }
        c.StartBar();
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

    /// <summary>Windows reports every minimize; with the setting on, the window goes into the scratchpad.</summary>
    void HookMinimize()
    {
        _minimizeProc = OnMinimizeStart;
        _minimizeHook = SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZESTART, IntPtr.Zero, _minimizeProc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    void OnMinimizeStart(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) return;
        if (!Settings.MinimizeToScratchpad || _windows is not { } windows) return;
        var handle = hwnd.ToInt64();
        _ = Task.Run(async () =>
        {
            try { await windows.ScratchStoreAsync(_cts.Token, handle); }
            catch (Exception ex) when (ex is not OperationCanceledException) { Log.Debug("Minimize → scratchpad: " + Net.Describe(ex)); }
        });
    }

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

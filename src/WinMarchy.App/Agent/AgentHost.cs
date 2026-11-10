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
    readonly DispatcherTimer _launcherTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    DateTime _launcherStarted = DateTime.MinValue;
    HwndSource? _window;
    ScreenshotWatch? _screenshots;
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
    /// <summary>The GlazeWM WinMarchy is connected to: when it ends, its exit code tells a crash from a quit.</summary>
    System.Diagnostics.Process? _glazeProcess;
    /// <summary>When WinMarchy started GlazeWM again after a crash, to stop after three in ten minutes.</summary>
    readonly List<DateTime> _glazeCrashes = new();
    /// <summary>Until then a GlazeWM that does not answer is waited for, not restarted: the PC just woke up, was unlocked or changed its screens.</summary>
    DateTime _glazeGraceUntil = DateTime.MinValue;
    bool _graceSaid;
    bool _paused;
    bool _pauseBusy;
    Forms.ToolStripItem? _trayPause;

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

    /// <summary>WinMarchy is paused: Windows looks and works as without it; only the menu and the pause key work.</summary>
    public bool Paused => _paused;

    /// <summary>Paused or resumed (Start page, menu, tray).</summary>
    public event Action? PausedChanged;

    /// <summary>GlazeWM runs, but WinMarchy cannot reach it (after two tries): the reason, else null.</summary>
    public string? GlazeProblem { get; private set; }

    /// <summary>Cached: the taskbar check runs twice a second and should not read a file each time.</summary>
    bool SetupDone => _setupDone;

    // ------------------------------------------------------------------ start and stop

    public void Start()
    {
        CreateMessageWindow();
        _screenshots = new ScreenshotWatch(() => Settings);
        CreateTray();
        _taskbarTimer.Tick += (_, _) => EnforceTaskbar();
        _taskbarTimer.Start();
        AppModel.SettingsChanged += EnforceTaskbar;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _ = Task.Run(() => GlazeLoopAsync(_cts.Token));
        EndPauseOfLastRun();

        if (SetupDone)
        {
            StartComponents();
            TakeGreenshot();
            _ = KeepGlazeCurrentAsync(_cts.Token);
        }
        HookMinimize();
        // The program list for Win+Space's search, read once in the background.
        StartApps.Refresh();
        _launcherTimer.Tick += (_, _) => KeepLauncherRunning();
        _launcherTimer.Start();
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
            if (File.Exists(Paths.AhkScript) && !Core.Services.Components.OurAhkProcesses().Any()) c.StartAhk();
            // Print belongs to Greenshot when the user chose it; it has to run for that.
            if (Settings.ScreenshotTool == Screenshots.Greenshot && !Core.Services.Components.IsRunning("Greenshot") &&
                c.Status(Core.Tools.ToolCatalog.Greenshot).Path is { } greenshot) AppLauncher.Start(greenshot);
            // Win opens Fluent Search; after the setup it has never run yet.
            Log.Trace($"components: glazewm {Core.Services.Components.GlazeRunning}, yasb {c.BarRunning}, " +
                      $"ahk {Core.Services.Components.OurAhkProcesses().Any()}, launcher {Core.Services.Components.IsRunning(Core.Integrations.FluentSearchConfig.Process)}", "agent");
            if (c.StartLauncher())
            {
                _launcherStarted = DateTime.Now;
                Log.Info(L.T("agent.launcherStarted"));
            }
        }
        catch (Exception ex)
        {
            Log.Warn(L.T("agent.startFailed", ("detail", Net.Describe(ex))));
        }
    }

    /// <summary>
    /// Greenshot is the screenshot tool once it is installed, unless Snipping Tool was chosen on
    /// purpose; and its captures go to the clipboard. Settings from before either get it set up once
    /// (a job with its own restore point, like a switch on the Settings page).
    /// </summary>
    void TakeGreenshot()
    {
        var s = Settings;
        if (AppModel.Engine.Components.Status(Core.Tools.ToolCatalog.Greenshot).Path is not { } exe) return;
        bool wanted = s.ScreenshotTool == Screenshots.Greenshot || !s.ScreenshotToolChosen;
        var fixedIni = Screenshots.GreenshotFixedIni(exe, Locations.Current);
        var current = File.Exists(fixedIni) ? FileSafety.ReadShared(fixedIni) : null;
        if (!wanted || (s.ScreenshotTool == Screenshots.Greenshot && Screenshots.GreenshotFixed(current, clipboard: true) == current)) return;
        Log.Info(L.T("agent.greenshotTakesOver"));
        AppModel.Run("screenshots", "hotkeys", L.T("jobs.screenshots"), AppModel.Engine.ScreenshotToolSteps(Screenshots.Greenshot, byUser: false), j =>
        {
            if (j.State == JobState.Done) Notify(L.T("agent.greenshotTakesOver"));
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Win alone opens Fluent Search, so it has to run. If it is gone (crashed, or a restart after a
    /// theme change did not come up), it is started again; not more than every 30 seconds, and not while
    /// a job runs (a theme change restarts it on purpose, the setup installs it).
    /// </summary>
    void KeepLauncherRunning()
    {
        if (!SetupDone || _restoring || _paused || AppModel.Jobs.AnyRunning) return;
        if (Core.Services.Components.IsRunning(Core.Integrations.FluentSearchConfig.Process)) return;
        if (DateTime.Now - _launcherStarted < TimeSpan.FromSeconds(30))
        {
            Log.Trace("launcher: not running, started less than 30 s ago, waiting", "launcher");
            return;
        }
        try
        {
            if (AppModel.Engine.Components.StartLauncher())
            {
                _launcherStarted = DateTime.Now;
                Log.Info(L.T("agent.launcherRestarted"));
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Fluent Search: " + Net.Describe(ex));
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _glazeProcess?.Dispose();
        _launcherTimer.Stop();
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
        _screenshots?.Dispose();
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
        _trayPause = menu.Items.Add(L.T("tray.pause"), null, (_, _) => Application.Current.Dispatcher.InvokeAsync(() => _ = TogglePauseAsync(_cts.Token)));
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
                    if (Core.Services.Components.GlazeRunning) await ConnectAsync(ct);
                    else
                    {
                        _glazeFailures = 0;
                        GlazeProblem = null;
                        await KeepGlazeRunningAsync(ct);
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
        var layouts = new LayoutEngine(ipc, Notify, gate, () => Settings, (screen, mode) => AppModel.Update(s => s.SetScreenLayout(screen, mode)));
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
        WatchGlazeProcess();
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
    /// is restarted – at most once in five minutes; a second hang soon after is only reported. Right
    /// after the PC woke up, was unlocked or changed its screens, GlazeWM gets a minute first: it may
    /// just be busy with that.
    /// </summary>
    async Task HandleStallAsync(GlazeIpc ipc, CancellationToken ct)
    {
        if (await GlazeIpc.ProbeAsync(ct))
        {
            Log.Warn(L.T("agent.glazeReconnecting"));
            await ipc.DisposeAsync();
            return;
        }
        if (DateTime.Now < _glazeGraceUntil)
        {
            if (!_graceSaid) Log.Info(L.T("agent.glazeBusyAfterWake"));
            _graceSaid = true;
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
        // What GlazeWM itself wrote in the last minutes before it stopped answering, and which build it is: the next fix starts there.
        var errors = AppModel.Engine.Components.GlazeErrors(3, since: DateTime.Now.AddMinutes(-15));
        var version = AppState.Load().ToolTags.GetValueOrDefault(Core.Tools.ToolCatalog.GlazeWm.Id) ?? AppModel.Engine.Components.Status(Core.Tools.ToolCatalog.GlazeWm).Version ?? "?";
        Log.Info(L.T("agent.glazeHungDetail", ("version", version), ("detail", errors.Count > 0 ? string.Join(" | ", errors) : L.T("agent.glazeNoErrors"))));
        Notify(L.T("agent.glazeRestarting"));
        // WinMarchy ends this one itself: its end is no crash.
        ForgetGlazeProcess();
        await ipc.DisposeAsync();
        if (!await AppModel.Engine.Components.RestartGlazeAsync(ct))
        {
            Notify(L.T("actions.glazeMissing"));
            return;
        }
        await CheckGlazeAnswersAsync(ct);
    }

    /// <summary>
    /// After a start: GlazeWM should answer within twenty seconds. If it does not, its errors.log
    /// usually says why (another GlazeWM still held its lock, the configuration has an error); its
    /// last lines go into the log, and GlazeWM gets one more start.
    /// </summary>
    async Task CheckGlazeAnswersAsync(CancellationToken ct, bool again = true)
    {
        var c = AppModel.Engine.Components;
        var started = DateTime.Now.AddSeconds(-5);
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            if (await GlazeIpc.ProbeAsync(ct))
            {
                Log.Info(L.T("agent.glazeBack"));
                return;
            }
        }
        // Only what this start wrote; older lines would point at an earlier problem.
        var errors = c.GlazeErrors(3, since: started);
        Log.Warn(L.T("agent.glazeNoAnswerAfterStart", ("detail", errors.Count > 0 ? string.Join(" | ", errors) : L.T("agent.glazeNoErrors"))));
        if (!again)
        {
            Notify(L.T("agent.glazeStillDown"));
            return;
        }
        if (!await c.RestartGlazeAsync(ct))
        {
            Notify(L.T("agent.glazeStillDown"));
            return;
        }
        await CheckGlazeAnswersAsync(ct, again: false);
    }

    // ------------------------------------------------------------------ GlazeWM crashed

    /// <summary>Keeps a handle to the GlazeWM WinMarchy is connected to, so its exit code can be read once it ends.</summary>
    void WatchGlazeProcess()
    {
        var exe = AppModel.Engine.Components.GlazeExe;
        var running = Core.Services.Components.GlazeProcesses();
        var (pid, _) = running.FirstOrDefault(p => exe != null && string.Equals(p.Path, exe, StringComparison.OrdinalIgnoreCase));
        if (pid == 0 && running.Count > 0) pid = running[0].Pid;
        if (pid == 0 || _glazeProcess?.Id == pid) return;
        ForgetGlazeProcess();
        try
        {
            var p = System.Diagnostics.Process.GetProcessById(pid);
            // Opens the handle now: only a handle taken while it runs can read the exit code later.
            if (!p.HasExited) _glazeProcess = p;
            else p.Dispose();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Gone already, or not WinMarchy's to watch (elevated): then a crash is not noticed here.
        }
    }

    void ForgetGlazeProcess()
    {
        _glazeProcess?.Dispose();
        _glazeProcess = null;
    }

    /// <summary>
    /// GlazeWM is gone. Ended by the user (its tray menu: exit code 0) it stays off; ended by
    /// WinMarchy itself (setup, updates, restore, its own restart) nothing happens here either. A
    /// crash gets a new start, at most three in ten minutes; after that WinMarchy only says so.
    /// </summary>
    async Task KeepGlazeRunningAsync(CancellationToken ct)
    {
        if (_glazeProcess is not { } gone) return;
        int? code = null;
        try
        {
            if (!gone.HasExited) return;
            code = gone.ExitCode;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
        ForgetGlazeProcess();
        // Ended by WinMarchy itself (an update ends it the way its tray menu does, with code 0).
        if (_restoring || _paused || AppModel.Jobs.AnyRunning || App.ExitRequested) return;
        if (code == 0)
        {
            Log.Info(L.T("agent.glazeQuit"));
            return;
        }
        if (!SetupDone) return;
        var now = DateTime.Now;
        _glazeCrashes.RemoveAll(t => now - t > TimeSpan.FromMinutes(10));
        var detail = code is { } c ? "0x" + c.ToString("X8") : "?";
        if (_glazeCrashes.Count >= 3)
        {
            Log.Warn(L.T("agent.glazeCrashesTooOften", ("code", detail)));
            Notify(L.T("agent.glazeCrashesTooOften", ("code", detail)));
            return;
        }
        _glazeCrashes.Add(now);
        var errors = AppModel.Engine.Components.GlazeErrors(3, since: now.AddMinutes(-15));
        Log.Warn(L.T("agent.glazeCrashed", ("code", detail), ("detail", errors.Count > 0 ? string.Join(" | ", errors) : L.T("agent.glazeNoErrors"))));
        Notify(L.T("agent.glazeRestartingCrash"));
        await Task.Delay(TimeSpan.FromSeconds(1), ct);
        if (!AppModel.Engine.Components.StartGlaze())
        {
            Notify(L.T("actions.glazeMissing"));
            return;
        }
        await CheckGlazeAnswersAsync(ct);
    }

    // ------------------------------------------------------------------ waking up

    void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume) GiveGlazeTime("resume");
    }

    void OnSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason is Microsoft.Win32.SessionSwitchReason.SessionUnlock or Microsoft.Win32.SessionSwitchReason.ConsoleConnect
            or Microsoft.Win32.SessionSwitchReason.RemoteConnect) GiveGlazeTime(e.Reason.ToString());
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e) => GiveGlazeTime("display");

    void GiveGlazeTime(string why)
    {
        _glazeGraceUntil = DateTime.Now.AddMinutes(1);
        _graceSaid = false;
        Log.Trace("glazewm: a minute's patience after " + why, "agent");
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
        if (!Core.Services.Components.GlazeRunning)
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
        // Then every shown workspace takes the layout of its screen.
        if (_layouts is { } layouts)
        {
            try { await layouts.ApplyScreensAsync(_cts.Token); }
            catch (Exception ex) when (ex is not OperationCanceledException) { Log.Warn(L.T("tiling.layoutsFailed", ("detail", Net.Describe(ex)))); }
        }
        _taskbarShownByUser = false;
        EnforceTaskbar();
        var running = AppModel.Engine.Components.BarRunning;
        if (Settings.ShowBar && !running && SetupDone && !_paused) AppModel.Engine.Components.StartBar();
        else if (!Settings.ShowBar && running) AppModel.Engine.Components.StopBar();
    }

    // ------------------------------------------------------------------ taskbar, bar, awake

    /// <summary>
    /// Hidden while the setting is on and the setup is done. The auto-hide state from before WinMarchy
    /// is written down once, so "restore everything" and quitting put it back.
    /// </summary>
    void EnforceTaskbar()
    {
        bool want = Settings.HideTaskbar && !_taskbarShownByUser && !_restoring && !_paused && SetupDone;
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
        if (!c.StartBar()) Notify(L.T("agent.barMissing", ("tool", Core.Integrations.Bars.Tool.Name)));
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

    // ------------------------------------------------------------------ pause

    /// <summary>
    /// Pauses WinMarchy, or lets it go on (Win+Alt+Esc, the WinMarchy menu, the tray, the Start page).
    /// Paused, Windows looks and works as without WinMarchy; a restart of WinMarchy or Windows ends
    /// the pause.
    /// </summary>
    public async Task TogglePauseAsync(CancellationToken ct)
    {
        if (_pauseBusy) return;
        _pauseBusy = true;
        try
        {
            if (_paused) Resume();
            else await PauseAsync(ct);
        }
        finally
        {
            _pauseBusy = false;
        }
    }

    /// <summary>
    /// The hotkey script lets go of every key but the menu and the pause key, so Win opens the Start
    /// menu again. GlazeWM ends the way its tray menu ends it, which shows every window it had hidden
    /// on other workspaces. Bar and Fluent Search end, the taskbar shows, and Windows' colours and
    /// wallpaper are as they were before WinMarchy.
    /// </summary>
    async Task PauseAsync(CancellationToken ct)
    {
        if (!SetupDone)
        {
            Notify(L.T("pause.notSetUp"));
            return;
        }
        if (AppModel.Jobs.AnyRunning)
        {
            Notify(L.T("pause.jobRunning"));
            return;
        }
        _paused = true;
        Log.Info(L.T("pause.pausing"));
        if (!Core.Services.Components.SuspendAhk(true)) Log.Debug("pause: no hotkey script to suspend");
        // WinMarchy ends it on purpose: no crash to answer.
        ForgetGlazeProcess();
        if (Glaze is { Connected: true } ipc)
        {
            // GlazeWM may end before it answers: the wait is short, the check below says whether it is gone.
            try { await ipc.CommandAsync("wm-exit", ct).WaitAsync(TimeSpan.FromSeconds(3), ct); }
            catch (Exception ex) when (ex is WinMarchyException or TimeoutException) { Log.Debug("wm-exit: " + ex.Message); }
        }
        for (int i = 0; i < 30 && Core.Services.Components.GlazeRunning; i++) await Task.Delay(100, ct);
        if (Core.Services.Components.GlazeRunning) await Core.Services.Components.EndGlazeAsync(ct);
        AppModel.Engine.Components.StopBar();
        await Core.Integrations.FluentSearchConfig.StopAsync(ct);
        EnforceTaskbar();
        ShowPlainTaskbar();
        try
        {
            AppModel.Engine.ShowPlainWindows();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WinMarchyException)
        {
            Log.Warn(L.T("pause.lookFailed", ("detail", Net.Describe(ex))));
        }
        PauseShown();
        Notify(L.T("pause.paused"));
    }

    /// <summary>WinMarchy's colours and wallpaper come back, then the hotkeys, GlazeWM, bar, taskbar hiding and Fluent Search.</summary>
    void Resume()
    {
        Log.Info(L.T("pause.resuming"));
        try
        {
            AppModel.Engine.ShowWinMarchyLook();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WinMarchyException)
        {
            Log.Warn(L.T("pause.lookFailed", ("detail", Net.Describe(ex))));
        }
        _paused = false;
        TaskbarAsBeforePause();
        Core.Services.Components.SuspendAhk(false);
        StartComponents();
        _taskbarShownByUser = false;
        EnforceTaskbar();
        PauseShown();
        Notify(L.T("pause.resumed"));
    }

    /// <summary>
    /// A pause does not outlast WinMarchy: one that was still on when it last ended (closed, crashed,
    /// Windows restarted) ends now. Colours and wallpaper come back, and the hotkey script, which may
    /// still run suspended, takes its keys again.
    /// </summary>
    void EndPauseOfLastRun()
    {
        if (AppState.Load() is { PausedLook: not null } or { PausedFilesDefault: true })
        {
            Log.Info(L.T("pause.endedByRestart"));
            try { AppModel.Engine.ShowWinMarchyLook(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WinMarchyException)
            {
                Log.Warn(L.T("pause.lookFailed", ("detail", Net.Describe(ex))));
            }
        }
        TaskbarAsBeforePause();
        Core.Services.Components.SuspendAhk(false);
    }

    /// <summary>
    /// Paused, the taskbar is there as on any office PC: shown, and "Automatically hide the taskbar"
    /// off in Windows' settings, also where it was on before WinMarchy (an earlier tool may have
    /// turned it on). The setting from before the pause is noted first.
    /// </summary>
    void ShowPlainTaskbar()
    {
        bool autoHide = Taskbar.IsAutoHide();
        AppState.Update(s => s.PausedTaskbarAutoHide ??= autoHide);
        _guard.Hide = false;
        Taskbar.Show();
        if (autoHide) Taskbar.SetAutoHide(false);
    }

    /// <summary>The end of a pause (or of a run that ended paused): "Automatically hide the taskbar" as it was before.</summary>
    void TaskbarAsBeforePause()
    {
        if (AppState.Load().PausedTaskbarAutoHide is not { } before) return;
        if (before) Taskbar.SetAutoHide(true);
        AppState.Update(s => s.PausedTaskbarAutoHide = null);
    }

    /// <summary>The tray entry says what a click does now; the pages and the menu follow <see cref="PausedChanged"/>.</summary>
    void PauseShown()
    {
        if (_trayPause != null) _trayPause.Text = L.T(_paused ? "tray.resume" : "tray.pause");
        _tray.Text = _paused ? L.T("pause.trayTitle") : "WinMarchy";
        PausedChanged?.Invoke();
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
        if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero || _paused) return;
        if (_windows is not { } windows) return;
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

    /// <summary>The programs' latest releases (Start page), then WinMarchy's own update if updates are on.</summary>
    async Task CheckUpdatesAsync()
    {
        try
        {
            await Core.Tools.ToolUpdates.RefreshAsync(Core.Tools.ToolUpdates.MaxAge, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!Settings.AutoUpdate || AppInfo.IsPrerelease) return;
        await AppModel.Updater.CheckAsync(download: true, _cts.Token);
    }

    /// <summary>
    /// A new WinMarchy version writes GlazeWM's config, the hotkey script and the bar's config again
    /// (Engine.AfterUpdateSteps), and waits for it, so the GlazeWM update after it does not run into it.
    /// </summary>
    async Task RefreshGeneratedFilesAsync(CancellationToken ct)
    {
        if (!Core.Services.Engine.GeneratedFilesOutdated(AppState.Load())) return;
        Log.Info(L.T("jobs.afterUpdate", ("version", AppInfo.Version)));
        var started = await Application.Current.Dispatcher.InvokeAsync(() =>
            AppModel.Run("after-update", "setup", L.T("jobs.afterUpdate", ("version", AppInfo.Version)), AppModel.Engine.AfterUpdateSteps()) != null);
        if (!started) return;
        for (int i = 0; i < 240 && AppModel.Jobs.IsRunning("setup"); i++) await Task.Delay(500, ct);
    }

    /// <summary>
    /// Shortly after the start: GlazeWM is WinMarchy's build, in its latest release if updates are on
    /// (Settings). Runs as a job, so the Log page shows the steps; the crash watchdog leaves GlazeWM
    /// alone meanwhile. WinMarchy's own pre-releases update GlazeWM too: GlazeWM has releases of its own.
    /// </summary>
    async Task KeepGlazeCurrentAsync(CancellationToken ct)
    {
        try
        {
            await RefreshGeneratedFilesAsync(ct);
            // Login is over and the network is up by then.
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            if (AppModel.Jobs.IsRunning("setup")) return;
            if (await AppModel.Engine.GlazeUpkeepAsync(Settings.AutoUpdate, ct) is not { } upkeep) return;
            Log.Info(upkeep.Title);
            Application.Current?.Dispatcher.InvokeAsync(() => AppModel.Run("glazewm", "setup", upkeep.Title, upkeep.Steps, j =>
            {
                if (j.State == JobState.Done) Notify(L.T("agent.glazeUpToDate", ("version", AppState.Load().ToolTags.GetValueOrDefault(Core.Tools.ToolCatalog.GlazeWm.Id) ?? "?")));
                else Log.Warn(L.T("agent.glazeUpkeepFailed", ("detail", j.Status)));
                return Task.CompletedTask;
            }));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Mostly no network yet (VPN, proxy); the next start tries again.
            Log.Info(L.T("agent.glazeUpkeepFailed", ("detail", Net.Describe(ex))));
        }
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

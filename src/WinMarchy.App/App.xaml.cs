using System.Windows;
using System.Windows.Threading;
using WinMarchy.App.Agent;
using WinMarchy.App.Startup;
using WinMarchy.App.Ui;
using WinMarchy.Core;

namespace WinMarchy.App;

/// <summary>
/// Start: hand over to a running WinMarchy, copy to C:\WinMarchy\App, take a pending update, then the
/// agent (tray, shortcuts, GlazeWM) and – unless started in the background – the window.
/// </summary>
public partial class App : Application
{
    SingleInstance? _instance;
    static MainWindow? _main;

    public static bool ExitRequested { get; private set; }

    public static bool MainVisible => _main is { IsVisible: true } m && m.WindowState != WindowState.Minimized;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved: " + Net.Describe(args.Exception));
            args.SetObserved();
        };

        var cmd = StartArgs.Parse(e.Args);
        if (cmd.SelfTest != null)
        {
            Shutdown(await SelfTest.RunAsync(cmd.SelfTest));
            return;
        }

        L.Configure(UserSettings.Load().Language);

        // Another WinMarchy runs: AutoHotkey's fallback, GlazeWM, the bar or a second double click.
        if (!cmd.AfterUpdate && AgentClient.Forward(cmd))
        {
            Shutdown();
            return;
        }
        if (SelfInstaller.EnsureInstalled(e.Args) == SelfInstaller.Result.Relaunched)
        {
            Shutdown();
            return;
        }
        _instance = SingleInstance.Acquire(waitSeconds: cmd.AfterUpdate ? 15 : 0);
        if (_instance == null)
        {
            // The other one is still starting and has no agent window yet.
            AgentClient.Forward(cmd, waitMs: 8000);
            Shutdown();
            return;
        }
        if (cmd.AfterUpdate) ExeUpdate.CleanupOld();
        else if (ExeUpdate.Pending())
        {
            // Downloaded last time, not installed yet: switch before anything starts.
            try
            {
                ExeUpdate.Apply(e.Args);
                _instance.Dispose();
                _instance = null;
                Shutdown();
                return;
            }
            catch (Exception ex)
            {
                Log.Warn(L.T("update.applyFailed", ("detail", Net.Describe(ex))));
            }
        }

        Log.Cleanup();
        Log.Info(L.T("app.started", ("version", AppInfo.Version), ("mode", L.T("app.modes." + AppInfo.Mode))));
        AppModel.Initialize(new WicImageConverter());
        ThemeManager.Initialize(AppModel.Settings.ColorScheme, AppModel.CurrentTheme.Palette.Accent);

        var agent = new AgentHost();
        AppModel.Agent = agent;
        agent.Start();

        if (cmd.Action != null) await agent.RunActionAsync(cmd.Action);
        else if (!cmd.Background) ShowMain(cmd.Page);
        if (cmd.AfterUpdate) Toasts.Show(ToastKind.Ok, L.T("update.installed", ("version", AppInfo.Version)));
    }

    /// <summary>Opens (or brings up) the main window, optionally on a page.</summary>
    public static void ShowMain(string? route)
    {
        if (ExitRequested) return;
        if (_main == null)
        {
            _main = new MainWindow();
            Current.MainWindow = _main;
        }
        _main.BringToFront(route);
    }

    public static void Quit()
    {
        ExitRequested = true;
        AppModel.Agent?.Dispose();
        AppModel.Agent = null;
        _main?.Close();
        Current.Shutdown();
    }

    /// <summary>
    /// Switches to the downloaded version. Without a visible window the new one starts in the
    /// background as well, so an update at night does not open anything.
    /// </summary>
    public static void RestartForUpdate(bool background = false)
    {
        try
        {
            ExeUpdate.Apply(background || !MainVisible ? new[] { StartArgs.BackgroundSwitch } : Array.Empty<string>());
            Quit();
        }
        catch (Exception ex)
        {
            Toasts.Show(ToastKind.Error, L.T("update.applyFailed", ("detail", Net.Describe(ex))));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppModel.Agent?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(L.T("app.unexpectedError") + ": " + Net.Describe(e.Exception));
        Log.Debug(e.Exception.ToString());
        // In the background a message box would be in the way; the notification and the log are enough.
        if (MainVisible) MessageBox.Show(Net.Describe(e.Exception), L.T("app.unexpectedError"), MessageBoxButton.OK, MessageBoxImage.Error);
        else Overlays.Osd.Show(L.T("app.unexpectedError") + ": " + Net.Describe(e.Exception));
        e.Handled = true;
    }
}

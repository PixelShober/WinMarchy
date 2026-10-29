using System.Windows;
using WinMarchy.App.Agent;
using WinMarchy.Core;
using WinMarchy.Core.Backup;
using WinMarchy.Core.Integrations;
using WinMarchy.Core.Services;
using WinMarchy.Core.Themes;
using WinMarchy.Core.Updates;

namespace WinMarchy.App;

/// <summary>The one place pages, agent and overlays share: settings, engine, jobs, updates.</summary>
public static class AppModel
{
    static UserSettings _settings = new();

    public static UserSettings Settings => _settings;

    public static event Action? SettingsChanged;

    public static Engine Engine { get; private set; } = null!;
    public static JobManager Jobs { get; } = new();
    public static Updater Updater { get; } = new();
    public static AgentHost? Agent { get; set; }

    public static void Initialize(IImageConverter images)
    {
        _settings = UserSettings.Load();
        var wallpaper = OperatingSystem.IsWindows() ? new WindowsWallpaper() : null;
        var backups = new BackupStore(Paths.BackupDir, UserRegistry.Current, wallpaper);
        Engine = new Engine(Locations.Current, backups, () => _settings, Save, images)
        {
            Glaze = () => Agent?.Glaze,
        };
    }

    /// <summary>Saves and tells every page (theme accent, toggles).</summary>
    public static void Save(UserSettings s)
    {
        _settings = s;
        s.Save();
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            Ui.ThemeManager.Set(s.ColorScheme, CurrentTheme.Palette.Accent);
            SettingsChanged?.Invoke();
        });
    }

    public static void Update(Action<UserSettings> change)
    {
        var s = _settings.Clone();
        change(s);
        Save(s);
    }

    public static OmarchyTheme CurrentTheme => ThemeCatalog.Find(_settings.Theme) ?? ThemeCatalog.Default;

    /// <summary>Starts a job unless one of the same kind is running; the Log page and toasts follow it.</summary>
    public static Job? Run(string id, string kind, string title, List<SetupStep> steps, Func<Job, Task>? onFinished = null)
    {
        if (Jobs.IsRunning(kind))
        {
            Toasts.Show(ToastKind.Info, L.T("jobs.alreadyRunning", ("title", title)));
            return null;
        }
        return Jobs.Start(id, kind, title, steps, onFinished);
    }
}

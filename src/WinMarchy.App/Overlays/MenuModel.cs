using System.Diagnostics;
using WinMarchy.Core;

namespace WinMarchy.App.Overlays;

/// <summary>
/// One row of a menu: opens a submenu (<see cref="Menu"/>), runs a WinMarchy action (<see cref="Action"/>,
/// the same grammar as the shortcuts) or does something only the menu offers (<see cref="Run"/>).
/// </summary>
sealed record MenuEntry(string Icon, string Label)
{
    public string? Menu { get; init; }
    public string? Action { get; init; }
    public Action? Run { get; init; }
    public string? Detail { get; init; }
    public string? Keys { get; init; }
    public bool Current { get; init; }
}

/// <summary>
/// The menus behind Win+Alt+Space, built after Omarchy's (Apps, Learn, Trigger, Style, Setup, Update,
/// System). Everything points to what Windows already has – settings pages, Snipping Tool, the
/// launcher – so nothing here needs extra software or admin rights.
/// </summary>
static class MenuModel
{
    public const string Root = "root";

    public static string Title(string menu) => L.TryT("menu.titles." + menu) ?? "WinMarchy";

    static MenuEntry Item(string icon, string key, string action) => new(icon, L.T("menu." + key)) { Action = action };
    static MenuEntry Sub(string icon, string key, string menu) => new(icon, L.T("menu." + key)) { Menu = menu };
    static MenuEntry Do(string icon, string key, Action run) => new(icon, L.T("menu." + key)) { Run = run };

    static void OpenPage(string page) => App.ShowMain(page);

    public static IReadOnlyList<MenuEntry> Entries(string menu) => menu switch
    {
        "learn" => new[]
        {
            Item("keyboard", "learn.keybindings", "do:keybindings"),
            Item("book", "learn.omarchy", "web:https://omarchy.org/manual/"),
            Item("tiling", "learn.glazewm", "web:https://github.com/glzr-io/glazewm#readme"),
            Item("book", "learn.winmarchy", "web:https://github.com/" + AppInfo.GitHubRepo + "#readme"),
            Item("log", "learn.terminal", "web:https://learn.microsoft.com/windows/terminal/"),
            Item("log", "learn.powershell", "web:https://learn.microsoft.com/powershell/scripting/learn/ps101/00-introduction"),
        },
        "trigger" => new[]
        {
            Sub("camera", "trigger.capture", "capture"),
            Sub("toggle", "trigger.toggle", "toggle"),
            Sub("chip", "trigger.hardware", "hardware"),
            Item("circle", "trigger.emoji", "send:Win+Period"),
            Item("copy", "trigger.clipboard", "send:Win+V"),
        },
        "capture" => new[]
        {
            Item("camera", "capture.screenshot", "send:Win+Shift+S"),
            Item("play", "capture.record", "send:Win+Shift+R"),
            Do("folder", "capture.screenshotsFolder", () => Ui.UiKit.OpenFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots"))),
        },
        "toggle" => new[]
        {
            Item("coffee", "toggle.awake", "do:keep-awake"),
            Item("bell", "toggle.notifications", "open:ms-settings:notifications"),
            Item("moon", "toggle.nightlight", "open:ms-settings:nightlight"),
            Item("bar", "toggle.bar", "do:bar-toggle"),
            Item("bar", "toggle.taskbar", "do:taskbar-toggle"),
            Item("tiling", "toggle.layout", "do:layout-next"),
            Item("gap", "toggle.gaps", "do:gaps-toggle"),
            Item("eye", "toggle.transparency", "do:transparency-toggle"),
        },
        "hardware" => new[]
        {
            Item("monitor", "hardware.project", "send:Win+P"),
            Item("monitor", "hardware.displays", "open:ms-settings:display"),
            Item("volume", "hardware.sound", "open:ms-settings:sound"),
            Item("bluetooth", "hardware.bluetooth", "open:ms-settings:bluetooth"),
            Item("wifi", "hardware.wifi", "open:ms-availablenetworks:"),
            Item("power", "hardware.battery", "do:show-battery"),
        },
        "style" => new[]
        {
            Do("themes", "style.theme", MenuWindow.ShowThemes),
            Do("image", "style.background", MenuWindow.ShowWallpapers),
            Item("refresh", "style.nextBackground", "do:wallpaper-next"),
            Item("bar", "style.bar", "do:bar-toggle"),
            Do("tiling", "style.windows", () => OpenPage("tiling")),
            Do("themes", "style.winmarchy", () => OpenPage("themes")),
        },
        "setup" => new[]
        {
            Do("settings", "setup.winmarchy", () => OpenPage("start")),
            Do("keyboard", "setup.keybindings", () => OpenPage("hotkeys")),
            Item("monitor", "setup.displays", "open:ms-settings:display"),
            Item("keyboard", "setup.input", "open:ms-settings:keyboard"),
            Item("wifi", "setup.network", "open:ms-settings:network"),
            Item("bluetooth", "setup.bluetooth", "open:ms-settings:bluetooth"),
            Item("volume", "setup.sound", "open:ms-settings:sound"),
            Item("power", "setup.power", "open:ms-settings:powersleep"),
            Item("grid", "setup.defaults", "open:ms-settings:defaultapps"),
        },
        "update" => new[]
        {
            Do("download", "update.winmarchy", CheckForUpdate),
            Item("refresh", "update.glazewm", "do:reload"),
            Item("download", "update.windows", "open:ms-settings:windowsupdate"),
        },
        "system" => new[]
        {
            Item("lock", "system.lock", "do:lock"),
            Do("sleep", "system.suspend", Suspend),
            Do("logout", "system.logout", () => Shutdown("/l")),
            Do("refresh", "system.restart", () => Shutdown("/r /t 0")),
            Do("power", "system.shutdown", () => Shutdown("/s /t 0")),
        },
        _ => new[]
        {
            Item("grid", "root.apps", "app:launcher"),
            Sub("book", "root.learn", "learn"),
            Sub("play", "root.trigger", "trigger"),
            Sub("themes", "root.style", "style"),
            Sub("settings", "root.setup", "setup"),
            Do("undo", "root.restore", () => OpenPage("restore")),
            Sub("download", "root.update", "update"),
            Do("info", "root.about", () => OpenPage("start")),
            Sub("power", "root.system", "system"),
        },
    };

    static void CheckForUpdate()
    {
        Osd.Show(L.T("menu.update.checking"));
        _ = Task.Run(async () =>
        {
            await AppModel.Updater.CheckAsync(download: AppModel.Settings.AutoUpdate, CancellationToken.None);
            var s = AppModel.Updater.Status;
            Osd.Show(s.Error != null ? s.Error
                : s.Available ? L.T("settings.updateAvailable", ("version", s.Latest))
                : L.T("settings.upToDate"));
        });
    }

    static void Suspend()
    {
        // Windows decides whether that means sleep or hibernate (power settings, often set by IT).
        System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, false, false);
    }

    static void Shutdown(string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown.exe", args) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Osd.Show(Net.Describe(ex));
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using WinMarchy.App.Ui;
using WinMarchy.Core;
using WinMarchy.Core.Services;
using WinMarchy.Core.Themes;
using WinMarchy.Core.Tools;

namespace WinMarchy.App.Pages;

public partial class SetupPage : UserControl, IPage
{
    readonly Dictionary<string, CheckBox> _tools = new();
    readonly CheckBox _autostart;
    readonly CheckBox _taskbar;
    readonly CheckBox _bar;
    readonly CheckBox _wallpapers;

    public SetupPage()
    {
        InitializeComponent();
        var s = AppModel.Settings;
        _autostart = UiKit.Switch(L.T("setup.autostart"), s.StartWithWindows, L.T("setup.autostartHint"));
        _taskbar = UiKit.Switch(L.T("setup.taskbar"), s.HideTaskbar, L.T("setup.taskbarHint"));
        _bar = UiKit.Switch(L.T("setup.bar"), s.ShowBar, L.T("setup.barHint"));
        var bytes = AppModel.CurrentTheme.Backgrounds.Sum(b => b.Size);
        _wallpapers = UiKit.Switch(L.T("setup.wallpapers"), true, L.T("setup.wallpapersHint", ("size", Net.Mb(bytes)), ("theme", AppModel.CurrentTheme.Title)));
        foreach (var c in new[] { _autostart, _taskbar, _bar, _wallpapers }) Options.Children.Add(c);
        if (AppModel.Jobs.Get("setup") != null) Job.Follow("setup");
    }

    public void OnShown()
    {
        ToolChoices.Children.Clear();
        _tools.Clear();
        foreach (var tool in new[] { ToolCatalog.GlazeWm, ToolCatalog.Zebar, ToolCatalog.AutoHotkey, ToolCatalog.Flow, ToolCatalog.PowerShell })
        {
            var status = AppModel.Engine.Components.Status(tool);
            var label = new StackPanel();
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(UiKit.Text(tool.Name, "H3"));
            var badge = status.Installed ? UiKit.Badge(status.Version ?? L.T("tools.installedBadge"), "ok") : UiKit.Badge(tool.License, "muted");
            badge.Margin = new Thickness(8, 0, 0, 0);
            title.Children.Add(badge);
            label.Children.Add(title);
            label.Children.Add(UiKit.Text(L.T("setup.why." + tool.Id), "Small"));
            var box = new CheckBox
            {
                Content = label,
                IsChecked = !status.Installed && tool.Id != "pwsh",
                IsEnabled = !status.Installed,
                Margin = new Thickness(0, 0, 0, 14),
            };
            if (status.Installed) box.ToolTip = status.Path;
            _tools[tool.Id] = box;
            ToolChoices.Children.Add(box);
        }
        var detected = new[] { ToolCatalog.Terminal, ToolCatalog.Files, ToolCatalog.VsCode }
            .Select(t => (t, AppModel.Engine.Components.Status(t)))
            .Select(x => x.Item2.Installed ? L.T("setup.detected", ("tool", x.t.Name)) : L.T("setup.notDetected", ("tool", x.t.Name)));
        Detected.Text = string.Join(" · ", detected);
    }

    void OnStart(object sender, RoutedEventArgs e)
    {
        AppModel.Update(s =>
        {
            s.StartWithWindows = _autostart.IsChecked == true;
            s.HideTaskbar = _taskbar.IsChecked == true;
            s.ShowBar = _bar.IsChecked == true;
        });
        bool Want(string id) => _tools.TryGetValue(id, out var b) && b.IsEnabled && b.IsChecked == true;
        var options = new SetupOptions(Want("glazewm"), Want("zebar"), Want("autohotkey"), Want("flow"), Want("pwsh"), _wallpapers.IsChecked == true);
        var job = AppModel.Run("setup", "setup", L.T("jobs.setup"), AppModel.Engine.SetupSteps(options), async j =>
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (j.State == JobState.Done)
                {
                    Toasts.Show(ToastKind.Ok, L.T("setup.done"));
                    AppModel.Agent?.AfterSetup();
                }
                OnShown();
            });
        });
        if (job != null) Job.Follow(job.Id);
    }
}

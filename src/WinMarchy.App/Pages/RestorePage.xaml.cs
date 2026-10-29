using System.Windows;
using System.Windows.Controls;
using WinMarchy.App.Ui;
using WinMarchy.Core;
using WinMarchy.Core.Backup;
using WinMarchy.Core.Tools;

namespace WinMarchy.App.Pages;

public partial class RestorePage : UserControl, IPage
{
    public RestorePage()
    {
        InitializeComponent();
        if (AppModel.Jobs.Get("restore") != null) Job.Follow("restore");
    }

    public void OnShown()
    {
        var original = AppModel.Engine.Backups.LoadOriginal();
        OriginalText.Text = original == null || original.Items.Count == 0
            ? L.T("restore.nothingYet")
            : L.T("restore.originalText", ("count", original.Items.Count), ("date", original.Created.ToString("d", L.Culture)));
        ResetAll.IsEnabled = original is { Items.Count: > 0 } || AppState.Load().InstalledTools.Count > 0;
        var tools = AppState.Load().InstalledTools.Select(id => ToolCatalog.Get(id).Name).ToList();
        RemoveTools.IsEnabled = tools.Count > 0;
        RemoveToolsHint.Text = tools.Count > 0 ? string.Join(", ", tools) : L.T("restore.noTools");
        RenderPoints();
    }

    void RenderPoints()
    {
        Points.Children.Clear();
        var points = AppModel.Engine.Backups.List();
        if (points.Count == 0)
        {
            Points.Children.Add(UiKit.Text(L.T("restore.noPoints"), "Muted", new Thickness(20, 18, 20, 18)));
            return;
        }
        bool first = true;
        foreach (var p in points)
        {
            Points.Children.Add(PointRow(p, first));
            first = false;
        }
    }

    FrameworkElement PointRow(RestorePoint p, bool first)
    {
        var details = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var item in p.Items)
        {
            var line = UiKit.Text($"{L.T("restore.kinds." + item.Kind)} · {item.Target}" + (item.Existed ? "" : " · " + L.T("restore.wasMissing")), "Small");
            line.FontFamily = (System.Windows.Media.FontFamily)FindResource("MonoFont");
            line.TextTrimming = TextTrimming.CharacterEllipsis;
            line.ToolTip = item.Target;
            details.Children.Add(line);
        }

        var head = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        var toggle = new Button { Style = UiKit.S("SmallButton"), Content = L.T("restore.details") };
        toggle.Click += (_, _) => details.Visibility = details.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        var restore = new Button { Style = UiKit.S("SmallButton"), Content = L.T("restore.restore"), Margin = new Thickness(8, 0, 0, 0) };
        restore.Click += async (_, _) =>
        {
            if (await Dialogs.ConfirmAsync(L.T("restore.confirmPointTitle"), L.T("restore.confirmPoint", ("title", p.Title), ("count", p.Items.Count)), L.T("restore.restore")))
                Start(p, removeTools: false);
        };
        var delete = new Button { Style = UiKit.S("IconButton"), Content = new Icon { Kind = "trash", Size = 14 }, ToolTip = L.T("restore.delete"), Margin = new Thickness(4, 0, 0, 0) };
        delete.Click += async (_, _) =>
        {
            if (!await Dialogs.ConfirmAsync(L.T("restore.confirmDeleteTitle"), L.T("restore.confirmDelete", ("title", p.Title)), L.T("restore.delete"), danger: true)) return;
            AppModel.Engine.Backups.Delete(p.Id);
            RenderPoints();
        };
        buttons.Children.Add(toggle);
        buttons.Children.Add(restore);
        buttons.Children.Add(delete);
        DockPanel.SetDock(buttons, Dock.Right);
        head.Children.Add(buttons);

        var info = new StackPanel();
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(UiKit.Text(p.Title, "H3"));
        if (p.Restored != null)
        {
            var b = UiKit.Badge(L.T("restore.restoredAt", ("date", p.Restored.Value.ToString("g", L.Culture))), "muted");
            b.Margin = new Thickness(8, 0, 0, 0);
            title.Children.Add(b);
        }
        info.Children.Add(title);
        info.Children.Add(UiKit.Text(L.T("restore.pointLine", ("date", p.Created.ToString("g", L.Culture)), ("count", p.Items.Count)), "Small"));
        head.Children.Add(info);

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(details);
        var row = new Border { Child = stack, Padding = new Thickness(20, 14, 16, 14), BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0) };
        row.SetResourceReference(Border.BorderBrushProperty, "Border");
        return row;
    }

    async void OnResetAll(object sender, RoutedEventArgs e)
    {
        var original = AppModel.Engine.Backups.LoadOriginal() ?? new RestorePoint { Id = BackupStore.OriginalId, Kind = "original", Title = "original" };
        var remove = RemoveTools.IsChecked == true;
        var text = L.T("restore.confirmAll", ("count", original.Items.Count)) + (remove ? " " + L.T("restore.confirmAllTools") : "");
        if (!await Dialogs.ConfirmAsync(L.T("restore.confirmAllTitle"), text, L.T("restore.resetAll"), danger: true)) return;
        AppModel.Agent?.PrepareForRestore(remove);
        Start(original, remove);
    }

    void Start(RestorePoint point, bool removeTools)
    {
        var title = point.IsOriginal ? L.T("jobs.restoreAll") : L.T("jobs.restore", ("title", point.Title));
        var job = AppModel.Run("restore", "restore", title, AppModel.Engine.RestoreSteps(point, removeTools), async j =>
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (j.State == JobState.Done)
                {
                    Toasts.Show(ToastKind.Ok, point.IsOriginal ? L.T("restore.allDone") : L.T("restore.pointDone"));
                    if (point.IsOriginal) AppModel.Agent?.AfterRestoreAll();
                }
                OnShown();
            });
        });
        if (job != null) Job.Follow(job.Id);
    }

    void OnOpenFolder(object sender, RoutedEventArgs e) => UiKit.OpenFolder(Paths.BackupDir);
}

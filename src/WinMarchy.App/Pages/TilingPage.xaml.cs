using System.Windows;
using System.Windows.Controls;
using WinMarchy.App.Ui;
using WinMarchy.Core;
using WinMarchy.Core.GlazeWm;

namespace WinMarchy.App.Pages;

public partial class TilingPage : UserControl, IPage
{
    static readonly int[] Gaps = { 0, 5, 10, 15, 20, 30 };
    readonly Dictionary<string, RadioButton> _layouts = new();
    readonly CheckBox _square, _titleBars, _dim, _focusFollows, _taskbar, _bar;

    public TilingPage()
    {
        InitializeComponent();
        foreach (var mode in new[] { "dwindle", "columns", "floating", "manual" })
        {
            var content = new StackPanel();
            content.Children.Add(Diagram(mode));
            content.Children.Add(UiKit.Text(L.T("layouts." + mode), "H3", new Thickness(0, 10, 0, 2)));
            content.Children.Add(UiKit.Text(L.T("layouts." + mode + "Text"), "Small"));
            var option = new RadioButton { Style = UiKit.S("OptionCard"), GroupName = "layout", Content = content, Tag = mode };
            _layouts[mode] = option;
            Layouts.Children.Add(option);
        }
        foreach (var g in Gaps)
        {
            InnerGap.Items.Add(new ComboBoxItem { Content = $"{g} px", Tag = g });
            OuterGap.Items.Add(new ComboBoxItem { Content = $"{g} px", Tag = g });
        }
        var s = AppModel.Settings;
        _square = UiKit.Switch(L.T("tiling.square"), s.SquareCorners, L.T("tiling.squareHint"));
        _titleBars = UiKit.Switch(L.T("tiling.titleBars"), s.HideTitleBars, L.T("tiling.titleBarsHint"));
        _dim = UiKit.Switch(L.T("tiling.dim"), s.DimInactive, L.T("tiling.dimHint"));
        _focusFollows = UiKit.Switch(L.T("tiling.focusFollows"), s.FocusFollowsCursor, null);
        foreach (var c in new[] { _square, _titleBars, _dim, _focusFollows }) Look.Children.Add(c);
        _taskbar = UiKit.Switch(L.T("setup.taskbar"), s.HideTaskbar, L.T("tiling.taskbarHint"));
        _bar = UiKit.Switch(L.T("setup.bar"), s.ShowBar, L.T("setup.barHint"));
        Desktop.Children.Add(_taskbar);
        Desktop.Children.Add(_bar);
    }

    /// <summary>A tiny picture of the layout: tiles in the accent and two neutral shades.</summary>
    static FrameworkElement Diagram(string mode)
    {
        var canvas = new Canvas { Width = 120, Height = 70, HorizontalAlignment = HorizontalAlignment.Left };
        void Tile(double x, double y, double w, double h, string brush)
        {
            var b = new Border { Width = w, Height = h, BorderThickness = new Thickness(1.5) };
            b.SetResourceReference(Border.BorderBrushProperty, brush);
            b.SetResourceReference(Border.BackgroundProperty, brush == "Accent" ? "AccentSoft" : "Surface2");
            Canvas.SetLeft(b, x);
            Canvas.SetTop(b, y);
            canvas.Children.Add(b);
        }
        switch (mode)
        {
            case "dwindle":
                Tile(0, 0, 58, 70, "BorderStrong");
                Tile(62, 0, 58, 33, "BorderStrong");
                Tile(62, 37, 27, 33, "BorderStrong");
                Tile(93, 37, 27, 33, "Accent");
                break;
            case "columns":
                Tile(0, 0, 37, 70, "BorderStrong");
                Tile(41, 0, 38, 70, "BorderStrong");
                Tile(83, 0, 37, 70, "Accent");
                break;
            case "floating":
                Tile(6, 6, 70, 44, "BorderStrong");
                Tile(40, 22, 74, 44, "Accent");
                break;
            default:
                Tile(0, 0, 58, 70, "BorderStrong");
                Tile(62, 0, 58, 70, "Accent");
                break;
        }
        return canvas;
    }

    public void OnShown()
    {
        var s = AppModel.Settings;
        if (_layouts.TryGetValue(s.DefaultLayout, out var l)) l.IsChecked = true;
        Select(InnerGap, s.InnerGap);
        Select(OuterGap, s.OuterGap);
        _ = RenderWorkspacesAsync();
    }

    static void Select(ComboBox box, int value) =>
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().OrderBy(i => Math.Abs((int)i.Tag - value)).First();

    async Task RenderWorkspacesAsync()
    {
        Workspaces.Children.Clear();
        var agent = AppModel.Agent;
        if (agent?.Glaze is not { Connected: true } ipc || agent.Layouts is not { } layouts)
        {
            Workspaces.Children.Add(UiKit.Text(L.T("tiling.notConnected"), "Muted"));
            return;
        }
        try
        {
            var data = await ipc.QueryAsync("workspaces", CancellationToken.None);
            var list = GlazeTree.List(data, "workspaces")
                .Where(w => GlazeTree.Str(w, "name") != GlazeConfig.ScratchpadWorkspace)
                .OrderBy(w => GlazeTree.Str(w, "name"), Comparer<string?>.Create(GlazeTree.CompareNames)).ToList();
            foreach (var w in list)
            {
                var name = GlazeTree.Str(w, "name")!;
                var windows = GlazeTree.Windows(w).Count();
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var group = new Border { Style = UiKit.S("SegmentGroup") };
                var seg = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var mode in new[] { LayoutMode.Dwindle, LayoutMode.Columns, LayoutMode.Floating, LayoutMode.Manual })
                {
                    var b = new RadioButton { Style = UiKit.S("Segment"), GroupName = "ws-" + name, Content = LayoutEngine.Name(mode), IsChecked = layouts.ModeOf(name) == mode };
                    b.Checked += async (_, _) => await layouts.SetAsync(name, mode, CancellationToken.None);
                    seg.Children.Add(b);
                }
                group.Child = seg;
                DockPanel.SetDock(group, Dock.Right);
                row.Children.Add(group);
                var label = UiKit.Text(L.T("tiling.workspaceRow", ("name", name), ("count", windows)), GlazeTree.Bool(w, "hasFocus") ? "H3" : "Body");
                label.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(label);
                Workspaces.Children.Add(row);
            }
            if (list.Count == 0) Workspaces.Children.Add(UiKit.Text(L.T("tiling.noWorkspaces"), "Muted"));
        }
        catch (Exception ex)
        {
            Workspaces.Children.Add(UiKit.Text(Net.Describe(ex), "Muted"));
        }
    }

    void OnRefreshWorkspaces(object sender, RoutedEventArgs e) => _ = RenderWorkspacesAsync();

    async void OnApply(object sender, RoutedEventArgs e)
    {
        var layout = _layouts.FirstOrDefault(l => l.Value.IsChecked == true).Key ?? "dwindle";
        AppModel.Update(s =>
        {
            s.DefaultLayout = layout;
            s.InnerGap = InnerGap.SelectedItem is ComboBoxItem { Tag: int i } ? i : s.InnerGap;
            s.OuterGap = OuterGap.SelectedItem is ComboBoxItem { Tag: int o } ? o : s.OuterGap;
            s.SquareCorners = _square.IsChecked == true;
            s.HideTitleBars = _titleBars.IsChecked == true;
            s.DimInactive = _dim.IsChecked == true;
            s.FocusFollowsCursor = _focusFollows.IsChecked == true;
            s.HideTaskbar = _taskbar.IsChecked == true;
            s.ShowBar = _bar.IsChecked == true;
        });
        try
        {
            await (AppModel.Agent?.ApplyTilingAsync() ?? Task.CompletedTask);
            Toasts.Show(ToastKind.Ok, L.T("tiling.applied"));
        }
        catch (Exception ex)
        {
            Toasts.Show(ToastKind.Error, Net.Describe(ex));
        }
    }
}

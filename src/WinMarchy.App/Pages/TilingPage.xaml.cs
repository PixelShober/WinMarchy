using System.Windows;
using System.Windows.Controls;
using WinMarchy.App.Ui;
using WinMarchy.Core;
using WinMarchy.Core.GlazeWm;
using WinMarchy.Core.Hotkeys;
using WinMarchy.Core.Integrations;

namespace WinMarchy.App.Pages;

public partial class TilingPage : UserControl, IPage
{
    static readonly int[] Gaps = { 0, 5, 10, 15, 20, 30 };
    static readonly string[] LayoutIds = { "dwindle", "columns", "scrolling", "floating", "manual" };
    static readonly LayoutMode[] Modes = { LayoutMode.Dwindle, LayoutMode.Columns, LayoutMode.Scrolling, LayoutMode.Floating, LayoutMode.Manual };
    /// <summary>What Win+Ctrl+D can start on a monitor's new workspace.</summary>
    static readonly string[] DesktopApps =
    {
        "terminal", "browser", "browser2", "files", "editor", "notepad", "mail", "calendar", "notes", "word", "stackfield", "herdr", "pi",
        "calculator", "taskmanager",
    };
    readonly Dictionary<string, RadioButton> _layouts = new();
    readonly CheckBox _square, _titleBars, _dim, _focusFollows, _taskbar, _bar, _minimize, _fullscreen;
    readonly List<(TextBox Workspaces, ComboBox App)> _monitorRows = new();
    readonly Dictionary<string, CheckBox> _barModules = new();
    readonly TextBox _trayPinned = new();
    readonly StackPanel _trayPinnedPanel = new();
    readonly StackPanel _shortcutRows = new();
    readonly TextBlock _shortcutError = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
    readonly List<(TextBox Title, TextBox Action, CheckBox InBar)> _shortcuts = new();

    /// <summary>Suggestions in the action box of a bar entry; anything a shortcut can do works.</summary>
    static readonly string[] ActionSuggestions =
    {
        "app:terminal", "app:browser", "app:files", "app:editor", "app:mail", "app:calendar", "app:teams", "app:notes", "app:lunatask",
        "app:stackfield", "app:calculator", "app:taskmanager", "do:theme-menu", "do:wallpaper-menu", "do:keybindings", "do:menu system",
        "do:screenshot", "do:about", "do:lock", "open:ms-settings:", "web:https://", "run:",
    };

    public TilingPage()
    {
        InitializeComponent();
        foreach (var mode in LayoutIds)
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
        _fullscreen = UiKit.Switch(L.T("tiling.newWindowEndsFullscreen"), s.NewWindowEndsFullscreen, L.T("tiling.newWindowEndsFullscreenHint"));
        foreach (var c in new[] { _square, _titleBars, _dim, _focusFollows, _fullscreen }) Look.Children.Add(c);
        _taskbar = UiKit.Switch(L.T("setup.taskbar"), s.HideTaskbar, L.T("tiling.taskbarHint"));
        _bar = UiKit.Switch(L.T("setup.bar"), s.ShowBar, L.T("setup.barHint"));
        _minimize = UiKit.Switch(L.T("tiling.blockMinimize"), s.BlockMinimize, L.T("tiling.blockMinimizeHint"));
        for (int c = 1; c <= 3; c++) ScrollingColumns.Items.Add(new ComboBoxItem { Content = L.T("tiling.columnsCount", ("count", c)), Tag = c });
        Desktop.Children.Add(_taskbar);
        Desktop.Children.Add(_bar);
        // Which entries the bar shows; each one can be clicked.
        Desktop.Children.Add(UiKit.Text(L.T("tiling.barModules"), "H3", new Thickness(0, 2, 0, 6)));
        var modules = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (id, _) in Bars.Modules)
        {
            var box = new CheckBox { Content = L.T("bar.modules." + id), IsChecked = Bars.IsOn(s, id), Margin = new Thickness(0, 0, 14, 6) };
            _barModules[id] = box;
            modules.Children.Add(box);
        }
        Desktop.Children.Add(modules);
        // Zebar keeps its own list of pinned tray icons; YASB pins with Alt+click.
        _trayPinnedPanel.Children.Add(UiKit.Text(L.T("tiling.trayPinned"), "H3", new Thickness(0, 0, 0, 4)));
        _trayPinned.Text = string.Join(", ", s.BarTrayPinned);
        System.Windows.Automation.AutomationProperties.SetName(_trayPinned, L.T("tiling.trayPinned"));
        _trayPinnedPanel.Children.Add(_trayPinned);
        var trayHint = UiKit.Text(L.T("tiling.trayPinnedHint"), "Small", new Thickness(0, 4, 0, 12));
        trayHint.TextWrapping = TextWrapping.Wrap;
        _trayPinnedPanel.Children.Add(trayHint);
        Desktop.Children.Add(_trayPinnedPanel);
        BuildShortcuts(s);
        Desktop.Children.Add(_minimize);
        var touchpad = UiKit.Text(L.T("tiling.touchpadHint"), "Small", new Thickness(0, 4, 0, 8));
        touchpad.TextWrapping = TextWrapping.Wrap;
        Desktop.Children.Add(touchpad);
        var gestures = new Button { Content = L.T("tiling.touchpad"), HorizontalAlignment = HorizontalAlignment.Left };
        gestures.Click += (_, _) => Core.Platform.AppLauncher.Open("ms-settings:devices-touchpad");
        Desktop.Children.Add(gestures);
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
            case "scrolling":
                // Two columns on the screen, the next ones waiting at the edge.
                Tile(0, 0, 54, 70, "BorderStrong");
                Tile(58, 0, 54, 70, "Accent");
                Tile(116, 0, 4, 70, "BorderStrong");
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
        Select(ScrollingColumns, s.ScrollingColumns);
        ShowBarChoice();
        RenderMonitors();
        _ = RenderWorkspacesAsync();
    }

    // ------------------------------------------------------------------ bar

    /// <summary>What only one of the two bars can do follows the bar chosen in the settings.</summary>
    void ShowBarChoice()
    {
        var zebar = AppModel.Engine.Components.Bar == Bars.Zebar;
        foreach (var id in Bars.YasbOnly)
        {
            if (!_barModules.TryGetValue(id, out var box)) continue;
            box.IsEnabled = !zebar;
            box.ToolTip = zebar ? L.T("tiling.yasbOnly") : null;
        }
        _trayPinnedPanel.Visibility = zebar ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The user's own bar entries: in the menu under the logo, and as buttons if ticked (YASB).</summary>
    void BuildShortcuts(UserSettings s)
    {
        Desktop.Children.Add(UiKit.Text(L.T("tiling.shortcuts"), "H3", new Thickness(0, 4, 0, 4)));
        var hint = UiKit.Text(L.T("tiling.shortcutsHint"), "Small", new Thickness(0, 0, 0, 8));
        hint.TextWrapping = TextWrapping.Wrap;
        Desktop.Children.Add(hint);
        Desktop.Children.Add(_shortcutRows);
        foreach (var shortcut in s.BarShortcuts) AddShortcutRow(shortcut);
        var add = UiKit.IconTextButton("plus", L.T("tiling.shortcutAdd"));
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Click += (_, _) => AddShortcutRow(new BarShortcut());
        Desktop.Children.Add(add);
        _shortcutError.SetResourceReference(TextBlock.ForegroundProperty, "Error");
        _shortcutError.Style = UiKit.S("Small");
        Desktop.Children.Add(_shortcutError);
        Desktop.Children.Add(new Border { Height = 12 });
    }

    void AddShortcutRow(BarShortcut shortcut)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var width in new[]
                 {
                     new GridLength(1, GridUnitType.Star), new GridLength(8), new GridLength(1.4, GridUnitType.Star), new GridLength(6), new GridLength(150),
                     new GridLength(10), GridLength.Auto, new GridLength(6), GridLength.Auto,
                 })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        var title = new TextBox { Text = shortcut.Title };
        System.Windows.Automation.AutomationProperties.SetName(title, L.T("tiling.shortcutTitle"));
        var action = new TextBox { Text = shortcut.Action, Style = UiKit.S("MonoBox") };
        System.Windows.Automation.AutomationProperties.SetName(action, L.T("tiling.shortcutAction"));
        // A few starting points; the box takes anything a shortcut can do.
        var templates = new ComboBox();
        templates.Items.Add(new ComboBoxItem { Content = L.T("tiling.shortcutTemplate"), Tag = "" });
        foreach (var suggestion in ActionSuggestions) templates.Items.Add(new ComboBoxItem { Content = suggestion, Tag = suggestion });
        templates.SelectedIndex = 0;
        templates.SelectionChanged += (_, _) =>
        {
            if (templates.SelectedItem is not ComboBoxItem { Tag: string { Length: > 0 } chosen }) return;
            action.Text = chosen;
            action.Focus();
            action.CaretIndex = action.Text.Length;
            templates.SelectedIndex = 0;
        };
        System.Windows.Automation.AutomationProperties.SetName(templates, L.T("tiling.shortcutTemplate"));
        var inBar = new CheckBox { Content = L.T("tiling.shortcutInBar"), IsChecked = shortcut.InBar, VerticalAlignment = VerticalAlignment.Center };
        var remove = UiKit.IconTextButton("trash", L.T("tiling.shortcutRemove"));
        var row = (title, action, inBar);
        remove.Click += (_, _) =>
        {
            _shortcutRows.Children.Remove(grid);
            _shortcuts.Remove(row);
        };
        Grid.SetColumn(title, 0);
        Grid.SetColumn(action, 2);
        Grid.SetColumn(templates, 4);
        Grid.SetColumn(inBar, 6);
        Grid.SetColumn(remove, 8);
        foreach (UIElement child in new UIElement[] { title, action, templates, inBar, remove }) grid.Children.Add(child);
        _shortcutRows.Children.Add(grid);
        _shortcuts.Add(row);
    }

    /// <summary>The entries as typed; an action that would not run is named in the error.</summary>
    (List<BarShortcut> List, string? Error) ReadShortcuts()
    {
        var list = new List<BarShortcut>();
        foreach (var (title, action, inBar) in _shortcuts)
        {
            var name = title.Text.Trim();
            var what = action.Text.Trim();
            if (name.Length == 0 && what.Length == 0) continue;
            if (name.Length == 0) return (list, L.T("tiling.shortcutNoTitle", ("action", what)));
            if (!HotkeyAction.TryParse(what, out _, out var error)) return (list, $"{name}: {error}");
            list.Add(new BarShortcut { Title = name, Action = what, InBar = inBar.IsChecked == true });
        }
        return (list, null);
    }

    // ------------------------------------------------------------------ monitors

    /// <summary>Screens in GlazeWM's order: left to right, then top to bottom.</summary>
    static List<System.Windows.Forms.Screen> Screens() =>
        System.Windows.Forms.Screen.AllScreens.OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y).ToList();

    void RenderMonitors()
    {
        Monitors.Children.Clear();
        _monitorRows.Clear();
        MonitorsError.Visibility = Visibility.Collapsed;
        var s = AppModel.Settings;
        var screens = Screens();
        for (int i = 0; i < screens.Count; i++)
        {
            var screen = screens[i];
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

            var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(UiKit.Text(L.T("tiling.monitorName", ("number", i + 1)), "H3"));
            name.Children.Add(UiKit.Text(L.T(screen.Primary ? "tiling.monitorPrimary" : "tiling.monitorSize",
                ("width", screen.Bounds.Width), ("height", screen.Bounds.Height)), "Small"));
            grid.Children.Add(name);

            var mine = s.WorkspaceMonitors.Where(p => p.Value == i).Select(p => int.TryParse(p.Key, out var n) ? n : 0).Where(n => n > 0).OrderBy(n => n);
            var workspaces = new TextBox { Style = UiKit.S("MonoBox"), Text = Ranges(mine), ToolTip = L.T("tiling.monitorWorkspacesHint"), VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(workspaces, L.T("tiling.monitorWorkspaces"));
            Grid.SetColumn(workspaces, 1);
            grid.Children.Add(workspaces);

            var app = new ComboBox { VerticalAlignment = VerticalAlignment.Center, ToolTip = L.T("tiling.monitorAppHint") };
            app.Items.Add(new ComboBoxItem { Content = L.T("tiling.monitorAppNone"), Tag = "" });
            foreach (var id in DesktopApps) app.Items.Add(new ComboBoxItem { Content = L.T("tiling.monitorAppPrefix") + " " + L.T("apps." + id), Tag = "app:" + id });
            var chosen = s.DesktopApps.TryGetValue(i.ToString(), out var a) ? a : "";
            app.SelectedItem = app.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == chosen) ?? app.Items[0];
            Grid.SetColumn(app, 3);
            grid.Children.Add(app);

            Monitors.Children.Add(grid);
            _monitorRows.Add((workspaces, app));
        }
        var hint = UiKit.Text(L.T("tiling.monitorsHint"), "Small");
        hint.TextWrapping = TextWrapping.Wrap;
        Monitors.Children.Add(hint);
    }

    /// <summary>1, 2, 3, 5 → "1-3, 5".</summary>
    static string Ranges(IEnumerable<int> numbers)
    {
        var list = numbers.ToList();
        var parts = new List<string>();
        for (int i = 0; i < list.Count; i++)
        {
            int start = list[i];
            while (i + 1 < list.Count && list[i + 1] == list[i] + 1) i++;
            parts.Add(list[i] == start ? start.ToString() : $"{start}-{list[i]}");
        }
        return string.Join(", ", parts);
    }

    /// <summary>"1-3, 5" or "1 2 3" → 1, 2, 3, 5. Null if something is not a workspace number.</summary>
    static List<int>? ParseRanges(string text)
    {
        var result = new List<int>();
        foreach (var part in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var bounds = part.Split('-', 2);
            if (!int.TryParse(bounds[0], out var from)) return null;
            var to = from;
            if (bounds.Length == 2 && !int.TryParse(bounds[1], out to)) return null;
            if (from < 1 || to > GlazeConfig.WorkspaceCount || to < from) return null;
            for (int n = from; n <= to; n++) result.Add(n);
        }
        return result;
    }

    /// <summary>Workspaces 1–10 shared out evenly, left to right (two screens: 1–5 and 6–10).</summary>
    void OnSplitWorkspaces(object sender, RoutedEventArgs e)
    {
        int count = _monitorRows.Count;
        if (count == 0) return;
        int per = GlazeConfig.WorkspaceCount / count, extra = GlazeConfig.WorkspaceCount % count, next = 1;
        for (int i = 0; i < count; i++)
        {
            int take = per + (i < extra ? 1 : 0);
            _monitorRows[i].Workspaces.Text = take == 0 ? "" : Ranges(Enumerable.Range(next, take));
            next += take;
        }
    }

    /// <summary>The page's choice, or an error text when a workspace is given to two screens.</summary>
    (Dictionary<string, int> Map, Dictionary<string, string> Apps, string? Error) ReadMonitors()
    {
        var map = new Dictionary<string, int>();
        var apps = new Dictionary<string, string>();
        for (int i = 0; i < _monitorRows.Count; i++)
        {
            var numbers = ParseRanges(_monitorRows[i].Workspaces.Text);
            if (numbers == null) return (map, apps, L.T("tiling.monitorsInvalid", ("number", i + 1)));
            foreach (var n in numbers)
            {
                if (map.TryGetValue(n.ToString(), out var other))
                    return (map, apps, L.T("tiling.monitorsTwice", ("workspace", n), ("a", other + 1), ("b", i + 1)));
                map[n.ToString()] = i;
            }
            if (_monitorRows[i].App.SelectedItem is ComboBoxItem { Tag: string action } && action.Length > 0) apps[i.ToString()] = action;
        }
        return (map, apps, null);
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
                foreach (var mode in Modes)
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
        var (map, apps, error) = ReadMonitors();
        if (error != null)
        {
            MonitorsError.Text = error;
            MonitorsError.Visibility = Visibility.Visible;
            return;
        }
        MonitorsError.Visibility = Visibility.Collapsed;
        var (shortcuts, shortcutError) = ReadShortcuts();
        _shortcutError.Text = shortcutError ?? "";
        _shortcutError.Visibility = shortcutError == null ? Visibility.Collapsed : Visibility.Visible;
        if (shortcutError != null) return;
        AppModel.Update(s =>
        {
            s.WorkspaceMonitors = map;
            s.DesktopApps = apps;
            s.ScrollingColumns = ScrollingColumns.SelectedItem is ComboBoxItem { Tag: int c } ? c : s.ScrollingColumns;
            s.DefaultLayout = layout;
            s.InnerGap = InnerGap.SelectedItem is ComboBoxItem { Tag: int i } ? i : s.InnerGap;
            s.OuterGap = OuterGap.SelectedItem is ComboBoxItem { Tag: int o } ? o : s.OuterGap;
            s.SquareCorners = _square.IsChecked == true;
            s.HideTitleBars = _titleBars.IsChecked == true;
            s.DimInactive = _dim.IsChecked == true;
            s.FocusFollowsCursor = _focusFollows.IsChecked == true;
            s.NewWindowEndsFullscreen = _fullscreen.IsChecked == true;
            s.HideTaskbar = _taskbar.IsChecked == true;
            s.ShowBar = _bar.IsChecked == true;
            s.BlockMinimize = _minimize.IsChecked == true;
            s.BarModules = _barModules.ToDictionary(m => m.Key, m => m.Value.IsChecked == true);
            s.BarTrayPinned = _trayPinned.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            s.BarShortcuts = shortcuts;
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

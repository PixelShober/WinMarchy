using System.Windows;
using System.Windows.Controls;
using WinMarchy.App.Ui;
using WinMarchy.Core;
using WinMarchy.Core.GlazeWm;
using WinMarchy.Core.Hotkeys;
using WinMarchy.Core.Integrations;

namespace WinMarchy.App.Pages;

public partial class TilingPage : UserControl, IPage, IGuardedPage
{
    static readonly int[] Gaps = { 0, 5, 10, 15, 20, 30 };
    static readonly LayoutMode[] Modes = { LayoutMode.Dwindle, LayoutMode.Columns, LayoutMode.Scrolling };
    /// <summary>More screens than this are not shown, whatever settings.json says.</summary>
    const int MaxScreens = 8;
    /// <summary>Starting points for a work mode's programs; the box takes anything that opens a window.</summary>
    static readonly string[] ModeSuggestions =
    {
        "app:browser https://", "app:browser2 https://", "web:https://", "app:mail", "app:calendar", "app:teams", "app:terminal",
        "app:files", "app:editor", "app:notes", "app:word", "app:stackfield", "app:obsidian", "app:herdr", "run:",
    };
    readonly CheckBox _square, _titleBars, _dim, _focusFollows, _taskbar, _bar, _fullscreen;
    readonly List<(RadioButton Main, TextBox Workspaces, List<RadioButton> Layout)> _monitorRows = new();
    /// <summary>The rows' screen names, in the rows' order.</summary>
    List<string> _screenNames = new();
    /// <summary>The screen names of the work modes' program boxes, in their order.</summary>
    List<string> _slotNames = new();
    /// <summary>The monitor rows are being built: a main screen ticked meanwhile is no choice of the user's.</summary>
    bool _rendering;
    readonly List<(TextBox Name, List<TextBox> Apps)> _modeRows = new();
    readonly Dictionary<string, CheckBox> _barModules = new();
    readonly StackPanel _shortcutRows = new();
    readonly TextBlock _shortcutError = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
    readonly List<(TextBox Title, TextBox Action, CheckBox InBar)> _shortcuts = new();
    /// <summary>The page as it was filled from the settings; anything else is a change not taken over yet.</summary>
    string _shown = "";

    /// <summary>Suggestions in the action box of a bar entry; anything a shortcut can do works.</summary>
    static readonly string[] ActionSuggestions =
    {
        "app:terminal", "app:browser", "app:files", "app:editor", "app:mail", "app:calendar", "app:teams", "app:notes",
        "app:stackfield", "app:calculator", "app:taskmanager", "do:theme-menu", "do:wallpaper-menu", "do:keybindings", "do:menu system",
        "do:screenshot", "do:about", "do:lock", "open:ms-settings:", "web:https://", "run:",
    };

    public TilingPage()
    {
        InitializeComponent();
        // The three layouts as a legend; each screen picks its own further down.
        foreach (var mode in Modes)
        {
            var content = new StackPanel();
            content.Children.Add(Diagram(mode));
            content.Children.Add(UiKit.Text(LayoutEngine.Name(mode), "H3", new Thickness(0, 10, 0, 2)));
            var text = UiKit.Text(L.T("layouts." + LayoutEngine.Id(mode) + "Text"), "Small");
            text.TextWrapping = TextWrapping.Wrap;
            content.Children.Add(text);
            var card = new Border { Child = content, BorderThickness = new Thickness(2), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 12, 12) };
            card.SetResourceReference(Border.BackgroundProperty, "Surface");
            card.SetResourceReference(Border.BorderBrushProperty, "Border");
            Layouts.Children.Add(card);
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
        Desktop.Children.Add(_taskbar);
        Desktop.Children.Add(_bar);
        // Which entries the bar shows; each one can be clicked.
        Desktop.Children.Add(UiKit.Text(L.T("tiling.barModules"), "H3", new Thickness(0, 2, 0, 6)));
        var modules = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (id, _) in Bars.Modules)
        {
            var box = new CheckBox { Content = L.T("bar.modules." + id), Margin = new Thickness(0, 0, 14, 6) };
            _barModules[id] = box;
            modules.Children.Add(box);
        }
        Desktop.Children.Add(modules);
        BuildShortcuts();
    }

    /// <summary>A tiny picture of the layout: tiles in the accent and two neutral shades.</summary>
    static FrameworkElement Diagram(LayoutMode mode)
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
            case LayoutMode.Dwindle:
                Tile(0, 0, 58, 70, "BorderStrong");
                Tile(62, 0, 58, 33, "BorderStrong");
                Tile(62, 37, 27, 33, "BorderStrong");
                Tile(93, 37, 27, 33, "Accent");
                break;
            case LayoutMode.Columns:
                Tile(0, 0, 37, 70, "BorderStrong");
                Tile(41, 0, 38, 70, "BorderStrong");
                Tile(83, 0, 37, 70, "Accent");
                break;
            default:
                // Two columns on the screen, the next ones waiting at the edge.
                Tile(0, 0, 54, 70, "BorderStrong");
                Tile(58, 0, 54, 70, "Accent");
                Tile(116, 0, 4, 70, "BorderStrong");
                break;
        }
        return canvas;
    }

    public void OnShown()
    {
        var s = AppModel.Settings;
        Select(InnerGap, s.InnerGap);
        Select(OuterGap, s.OuterGap);
        foreach (var (box, on) in new[] { (_square, s.SquareCorners), (_titleBars, s.HideTitleBars), (_dim, s.DimInactive), (_focusFollows, s.FocusFollowsCursor),
                     (_fullscreen, s.NewWindowEndsFullscreen), (_taskbar, s.HideTaskbar), (_bar, s.ShowBar) })
            box.IsChecked = on;
        foreach (var (id, box) in _barModules) box.IsChecked = Bars.IsOn(s, id);
        _shortcutRows.Children.Clear();
        _shortcuts.Clear();
        _shortcutError.Visibility = Visibility.Collapsed;
        foreach (var shortcut in s.BarShortcuts) AddShortcutRow(shortcut);
        RenderMonitors();
        RenderWorkModes();
        _shown = Snapshot();
    }

    // ------------------------------------------------------------------ leaving with changes

    /// <summary>Everything the page lets the user change, as one text: a change shows as a different text.</summary>
    string Snapshot()
    {
        var parts = new List<string?>
        {
            (InnerGap.SelectedItem as ComboBoxItem)?.Tag?.ToString(), (OuterGap.SelectedItem as ComboBoxItem)?.Tag?.ToString(),
            "main=" + MainRow(),
        };
        parts.AddRange(new[] { _square, _titleBars, _dim, _focusFollows, _fullscreen, _taskbar, _bar }.Select(b => b.IsChecked?.ToString()));
        parts.AddRange(_barModules.Select(m => m.Key + "=" + m.Value.IsChecked));
        parts.AddRange(_monitorRows.Select(r => r.Workspaces.Text.Trim() + "|" + r.Layout.FindIndex(b => b.IsChecked == true)));
        parts.AddRange(_modeRows.Select(r => r.Name.Text.Trim() + "|" + string.Join("|", r.Apps.Select(a => a.Text.Trim()))));
        parts.AddRange(_shortcuts.Select(x => x.Title.Text.Trim() + "|" + x.Action.Text.Trim() + "|" + x.InBar.IsChecked));
        return string.Join("\n", parts);
    }

    /// <inheritdoc />
    public bool HasChanges => Snapshot() != _shown;

    bool Discard()
    {
        OnShown();
        return true;
    }

    /// <summary>
    /// Leaving the page with changes not taken over: take them over, throw them away, or stay. A
    /// change that cannot be taken over (a typing error) keeps the page open with the error shown.
    /// </summary>
    public async Task<bool> CanLeaveAsync()
    {
        if (!HasChanges) return true;
        var choice = await Dialogs.ChooseAsync(L.T("tiling.unsavedTitle"), L.T("tiling.unsavedText"), L.T("tiling.unsavedStay"),
            (L.T("tiling.unsavedDiscard"), "Ghost"), (L.T("tiling.apply"), "Primary"));
        return choice switch
        {
            // Thrown away: the page shows the saved settings again (the window may only hide and come back to it).
            0 => Discard(),
            1 => await ApplyAsync(),
            _ => false,
        };
    }

    /// <summary>The user's own bar entries: in the menu under the logo, and as buttons if ticked.</summary>
    void BuildShortcuts()
    {
        Desktop.Children.Add(UiKit.Text(L.T("tiling.shortcuts"), "H3", new Thickness(0, 4, 0, 4)));
        var hint = UiKit.Text(L.T("tiling.shortcutsHint"), "Small", new Thickness(0, 0, 0, 8));
        hint.TextWrapping = TextWrapping.Wrap;
        Desktop.Children.Add(hint);
        Desktop.Children.Add(_shortcutRows);
        var add = UiKit.IconTextButton("plus", L.T("tiling.shortcutAdd"));
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Click += (_, _) => AddShortcutRow(new BarShortcut());
        Desktop.Children.Add(add);
        _shortcutError.SetResourceReference(TextBlock.ForegroundProperty, "Error");
        _shortcutError.Style = UiKit.S("Small");
        Desktop.Children.Add(_shortcutError);
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
        var templates = TemplatePicker(action, ActionSuggestions);
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

    /// <summary>A drop-down of starting points that fills <paramref name="target"/> and leaves the cursor at its end.</summary>
    static ComboBox TemplatePicker(TextBox target, IEnumerable<string> suggestions)
    {
        var templates = new ComboBox { VerticalAlignment = VerticalAlignment.Center };
        templates.Items.Add(new ComboBoxItem { Content = L.T("tiling.shortcutTemplate"), Tag = "" });
        foreach (var suggestion in suggestions) templates.Items.Add(new ComboBoxItem { Content = suggestion, Tag = suggestion });
        templates.SelectedIndex = 0;
        templates.SelectionChanged += (_, _) =>
        {
            if (templates.SelectedItem is not ComboBoxItem { Tag: string { Length: > 0 } chosen }) return;
            target.Text = chosen;
            target.Focus();
            target.CaretIndex = target.Text.Length;
            templates.SelectedIndex = 0;
        };
        System.Windows.Automation.AutomationProperties.SetName(templates, L.T("tiling.shortcutTemplate"));
        return templates;
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

    /// <summary>
    /// A row per screen: its name, whether it is the main screen, its layout and its workspaces. The
    /// connected screens, and those that still have workspaces from another setup (the docking
    /// station at work), so a laptop on its own keeps them for the next time. Without any workspaces
    /// given yet, the rows show the suggestion: the main screen 1 and 2, the others the rest.
    /// </summary>
    void RenderMonitors()
    {
        _rendering = true;
        Monitors.Children.Clear();
        _monitorRows.Clear();
        MonitorsError.Visibility = Visibility.Collapsed;
        var s = AppModel.Settings;
        var screens = Screens();
        int rows = Math.Clamp(new[] { screens.Count, s.WorkspaceMonitors.Values.DefaultIfEmpty(-1).Max() + 1, s.ScreenLayouts.Count }.Max(), 1, MaxScreens);
        _screenNames = RowNames(screens, rows);
        int main = Math.Clamp(s.MainScreen ?? Math.Max(0, screens.FindIndex(m => m.Primary)), 0, rows - 1);
        bool suggest = s.WorkspaceMonitors.Count == 0 && rows > 1;
        var plan = ScreenPlan.Split(rows, main);
        for (int i = 0; i < rows; i++)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });

            var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(UiKit.Text(_screenNames[i], "H3"));
            name.Children.Add(UiKit.Text(i >= screens.Count ? L.T("tiling.monitorAbsent")
                : Core.Platform.ScreenNames.Position(i, screens.Count) is { } where
                    ? L.T("tiling.monitorSizeAt", ("width", screens[i].Bounds.Width), ("height", screens[i].Bounds.Height), ("position", where))
                    : L.T("tiling.monitorSize", ("width", screens[i].Bounds.Width), ("height", screens[i].Bounds.Height)), "Small"));
            var isMain = new RadioButton
            {
                Content = L.T("tiling.mainScreen"), GroupName = "main-screen", IsChecked = i == main, Tag = i,
                ToolTip = L.T("tiling.mainScreenHint"), Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            };
            isMain.Checked += OnMainScreen;
            name.Children.Add(isMain);
            grid.Children.Add(name);

            // The screen's layout: every workspace shown on it follows.
            var segments = new StackPanel { Orientation = Orientation.Horizontal };
            var buttons = new List<RadioButton>();
            var current = s.ScreenLayoutOf(i);
            foreach (var mode in Modes)
            {
                var b = new RadioButton { Style = UiKit.S("Segment"), GroupName = "screen-layout-" + i, Content = LayoutEngine.Name(mode), IsChecked = mode == current, Tag = mode };
                System.Windows.Automation.AutomationProperties.SetName(b, L.T("tiling.monitorLayout", ("name", _screenNames[i]), ("layout", LayoutEngine.Name(mode))));
                buttons.Add(b);
                segments.Children.Add(b);
            }
            var group = new Border { Style = UiKit.S("SegmentGroup"), Child = segments, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(group, 1);
            grid.Children.Add(group);

            var mine = suggest ? plan[i]
                : s.WorkspaceMonitors.Where(p => p.Value == i).Select(p => int.TryParse(p.Key, out var n) ? n : 0).Where(n => n > 0).OrderBy(n => n).ToList();
            var workspaces = new TextBox { Style = UiKit.S("MonoBox"), Text = Ranges(mine), ToolTip = L.T("tiling.monitorWorkspacesHint"), VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(workspaces, L.T("tiling.monitorWorkspacesOf", ("name", _screenNames[i])));
            Grid.SetColumn(workspaces, 3);
            grid.Children.Add(workspaces);

            Monitors.Children.Add(grid);
            _monitorRows.Add((isMain, workspaces, buttons));
        }
        var hint = UiKit.Text(L.T(suggest ? "tiling.monitorsSuggested" : "tiling.monitorsHint"), "Small");
        hint.TextWrapping = TextWrapping.Wrap;
        Monitors.Children.Add(hint);
        _rendering = false;
    }

    /// <summary>
    /// The rows' names: what the connected screens are called, and for the others the name they had
    /// when they were last connected (remembered in state.json).
    /// </summary>
    static List<string> RowNames(List<System.Windows.Forms.Screen> screens, int rows)
    {
        var connected = Core.Platform.ScreenNames.Of(screens.Select(m => (m.DeviceName, m.Bounds.Height > m.Bounds.Width)).ToList());
        var remembered = AppState.Load().ScreenNames;
        if (connected.Count > 0 && !remembered.Take(connected.Count).SequenceEqual(connected))
            AppState.Update(st =>
            {
                for (int i = 0; i < connected.Count; i++)
                {
                    if (i < st.ScreenNames.Count) st.ScreenNames[i] = connected[i];
                    else st.ScreenNames.Add(connected[i]);
                }
            });
        return Enumerable.Range(0, rows)
            .Select(i => i < connected.Count ? connected[i]
                : i < remembered.Count ? remembered[i] : L.T("tiling.monitorName", ("number", i + 1)))
            .ToList();
    }

    /// <summary>The row ticked as the main screen.</summary>
    int MainRow() => Math.Max(0, _monitorRows.FindIndex(r => r.Main.IsChecked == true));

    /// <summary>A new main screen gets workspaces 1 and 2, the other screens share the rest evenly.</summary>
    void OnMainScreen(object sender, RoutedEventArgs e)
    {
        if (_rendering) return;
        FillWorkspaces(ScreenPlan.Split(_monitorRows.Count, MainRow()));
    }

    void FillWorkspaces(List<List<int>> plan)
    {
        for (int i = 0; i < _monitorRows.Count && i < plan.Count; i++) _monitorRows[i].Workspaces.Text = Ranges(plan[i]);
    }

    /// <summary>Each row's layout, as settings.json writes it.</summary>
    List<string> ReadScreenLayouts() => _monitorRows
        .Select(r => r.Layout.FirstOrDefault(b => b.IsChecked == true)?.Tag is LayoutMode m ? LayoutEngine.Id(m) : LayoutEngine.Id(LayoutMode.Dwindle))
        .ToList();

    // ------------------------------------------------------------------ work modes

    /// <summary>The three work modes: a name and a program per screen, at least three screens' worth.</summary>
    void RenderWorkModes()
    {
        WorkModesPanel.Children.Clear();
        _modeRows.Clear();
        WorkModesError.Visibility = Visibility.Collapsed;
        var s = AppModel.Settings;
        var screens = Screens();
        int slots = Math.Clamp(new[] { 3, screens.Count, s.WorkModes.Select(m => m.Apps.Count).DefaultIfEmpty(0).Max() }.Max(), 1, MaxScreens);
        // Slot k is the k-th screen from the left, as for the workspaces.
        var names = RowNames(screens, slots);
        _slotNames = names;
        var active = AppModel.Engine.CurrentHotkeys().Active;
        for (int n = 1; n <= WorkMode.Count; n++)
        {
            var mode = s.WorkModeOf(n);
            var panel = new StackPanel { Margin = new Thickness(0, n == 1 ? 0 : 14, 0, 0) };
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var keys = ModeKeys(active, n);
            DockPanel.SetDock(keys, Dock.Right);
            head.Children.Add(keys);
            head.Children.Add(UiKit.Text(L.T("workModes.default", ("number", n)), "H3"));
            panel.Children.Add(head);

            var name = new TextBox { Text = mode.Name, ToolTip = L.T("tiling.workModeNameHint") };
            System.Windows.Automation.AutomationProperties.SetName(name, L.T("tiling.workModeName"));
            panel.Children.Add(ModeRow(L.T("tiling.workModeName"), name, null));
            var apps = new List<TextBox>();
            for (int k = 0; k < slots; k++)
            {
                var box = new TextBox { Style = UiKit.S("MonoBox"), Text = k < mode.Apps.Count ? mode.Apps[k] : "", ToolTip = L.T("tiling.workModeAppHint") };
                var screen = k < names.Count ? names[k] : L.T("tiling.monitorName", ("number", k + 1));
                System.Windows.Automation.AutomationProperties.SetName(box, L.T("tiling.workModeApp", ("mode", n), ("name", screen)));
                panel.Children.Add(ModeRow(screen, box, TemplatePicker(box, ModeSuggestions)));
                apps.Add(box);
            }
            WorkModesPanel.Children.Add(panel);
            _modeRows.Add((name, apps));
        }
        var hint = UiKit.Text(L.T("tiling.workModesHint"), "Small", new Thickness(0, 12, 0, 0));
        hint.TextWrapping = TextWrapping.Wrap;
        WorkModesPanel.Children.Add(hint);
    }

    /// <summary>A label, the box, and the drop-down of starting points if there is one.</summary>
    static Grid ModeRow(string label, TextBox box, ComboBox? templates)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var width in new[] { new GridLength(130), new GridLength(1, GridUnitType.Star), new GridLength(8), new GridLength(170) })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        var text = UiKit.Text(label, "Body");
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);
        Grid.SetColumn(box, 1);
        if (templates == null) Grid.SetColumnSpan(box, 3);
        grid.Children.Add(box);
        if (templates != null)
        {
            Grid.SetColumn(templates, 3);
            grid.Children.Add(templates);
        }
        return grid;
    }

    /// <summary>The shortcuts that start mode <paramref name="number"/>, as key caps.</summary>
    static FrameworkElement ModeKeys(IEnumerable<HotkeyBinding> active, int number)
    {
        var bindings = active.Where(b => WorkMode.NumberOf(b.Do) == number).ToList();
        if (bindings.Count == 0) return UiKit.Text(L.T("tiling.workModeNoKeys"), "Small");
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var b in bindings)
        {
            var caps = UiKit.Keycaps(b.Keys);
            caps.Margin = new Thickness(10, 0, 0, 0);
            panel.Children.Add(caps);
        }
        return panel;
    }

    /// <summary>The modes as typed, without empty ones at the end; a program that would not start is named in the error.</summary>
    (List<WorkMode> Modes, string? Error) ReadWorkModes()
    {
        var modes = new List<WorkMode>();
        for (int n = 0; n < _modeRows.Count; n++)
        {
            var (name, boxes) = _modeRows[n];
            var mode = new WorkMode { Name = name.Text.Trim(), Apps = boxes.Select(b => b.Text.Trim()).ToList() };
            for (int k = 0; k < mode.Apps.Count; k++)
            {
                if (mode.Apps[k].Length == 0) continue;
                WorkModePlan.Startable(mode.Apps[k], out var error);
                if (error != null) return (modes, L.T("tiling.workModeInvalid", ("mode", mode.Title(n + 1)), ("name", _slotNames.ElementAtOrDefault(k) ?? L.T("tiling.monitorName", ("number", k + 1))), ("detail", error)));
            }
            while (mode.Apps.Count > 0 && mode.Apps[^1].Length == 0) mode.Apps.RemoveAt(mode.Apps.Count - 1);
            modes.Add(mode);
        }
        while (modes.Count > 0 && modes[^1].Name.Length == 0 && modes[^1].Apps.Count == 0) modes.RemoveAt(modes.Count - 1);
        return (modes, null);
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

    /// <summary>
    /// The main screen 1 and 2, the rest shared out evenly over the other rows, left to right: also
    /// screens that are not connected right now keep their share.
    /// </summary>
    void OnSplitWorkspaces(object sender, RoutedEventArgs e)
    {
        if (_monitorRows.Count > 0) FillWorkspaces(ScreenPlan.Split(_monitorRows.Count, MainRow()));
    }

    /// <summary>The page's choice, or an error text when a workspace is given to two screens.</summary>
    (Dictionary<string, int> Map, string? Error) ReadMonitors()
    {
        var map = new Dictionary<string, int>();
        for (int i = 0; i < _monitorRows.Count; i++)
        {
            var numbers = ParseRanges(_monitorRows[i].Workspaces.Text);
            if (numbers == null) return (map, L.T("tiling.monitorsInvalid", ("name", _screenNames[i])));
            foreach (var n in numbers)
            {
                if (map.TryGetValue(n.ToString(), out var other))
                    return (map, L.T("tiling.monitorsTwice", ("workspace", n), ("a", _screenNames[other]), ("b", _screenNames[i])));
                map[n.ToString()] = i;
            }
        }
        return (map, null);
    }

    static void Select(ComboBox box, int value) =>
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().OrderBy(i => Math.Abs((int)i.Tag - value)).First();

    void OnApply(object sender, RoutedEventArgs e) => _ = ApplyAsync();

    /// <summary>Saves what the page shows and puts it into effect. False if something is mistyped (the error stands next to it).</summary>
    async Task<bool> ApplyAsync()
    {
        var (map, error) = ReadMonitors();
        MonitorsError.Text = error ?? "";
        MonitorsError.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
        var (modes, modeError) = ReadWorkModes();
        WorkModesError.Text = modeError ?? "";
        WorkModesError.Visibility = modeError == null ? Visibility.Collapsed : Visibility.Visible;
        var (shortcuts, shortcutError) = ReadShortcuts();
        _shortcutError.Text = shortcutError ?? "";
        _shortcutError.Visibility = shortcutError == null ? Visibility.Collapsed : Visibility.Visible;
        if (error != null || modeError != null || shortcutError != null) return false;
        var layouts = ReadScreenLayouts();
        AppModel.Update(s =>
        {
            s.WorkspaceMonitors = map;
            s.ScreenLayouts = layouts;
            s.WorkModes = modes;
            s.MainScreen = MainRow();
            s.InnerGap = InnerGap.SelectedItem is ComboBoxItem { Tag: int i } ? i : s.InnerGap;
            s.OuterGap = OuterGap.SelectedItem is ComboBoxItem { Tag: int o } ? o : s.OuterGap;
            s.SquareCorners = _square.IsChecked == true;
            s.HideTitleBars = _titleBars.IsChecked == true;
            s.DimInactive = _dim.IsChecked == true;
            s.FocusFollowsCursor = _focusFollows.IsChecked == true;
            s.NewWindowEndsFullscreen = _fullscreen.IsChecked == true;
            s.HideTaskbar = _taskbar.IsChecked == true;
            s.ShowBar = _bar.IsChecked == true;
            s.BarModules = _barModules.ToDictionary(m => m.Key, m => m.Value.IsChecked == true);
            s.BarShortcuts = shortcuts;
        });
        _shown = Snapshot();
        try
        {
            await (AppModel.Agent?.ApplyTilingAsync() ?? Task.CompletedTask);
            Toasts.Show(ToastKind.Ok, L.T("tiling.applied"));
        }
        catch (Exception ex)
        {
            Toasts.Show(ToastKind.Error, Net.Describe(ex));
        }
        return true;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinMarchy.Core;
using WinMarchy.Core.GlazeWm;
using WinMarchy.Core.Hotkeys;
using WinMarchy.Core.Themes;

namespace WinMarchy.App.Overlays;

/// <summary>
/// Omarchy's menu on Windows: a card in the middle of the screen, typed text filters, arrows move,
/// Enter runs, Escape or Backspace goes back. The same window shows the theme switcher and the list
/// of shortcuts (Enter on a shortcut runs it, as in Omarchy).
/// </summary>
public sealed class MenuWindow : Window
{
    static MenuWindow? _current;

    public static void ShowMenu(string? name) => Open(w => w.ShowEntries(string.IsNullOrWhiteSpace(name) ? MenuModel.Root : name.Trim(), clearHistory: true));

    public static void ShowThemes() => Open(w => w.ShowThemeList());

    public static void ShowKeybindings() => Open(w => w.ShowBindings());

    static void Open(Action<MenuWindow> setup)
    {
        var app = Application.Current;
        if (app == null) return;
        app.Dispatcher.InvokeAsync(() =>
        {
            if (_current == null)
            {
                _current = new MenuWindow();
                _current.Closed += (_, _) => _current = null;
            }
            setup(_current);
            _current.Present();
        });
    }

    enum Mode { Menu, Themes, Bindings }

    readonly OverlayLook _look = OverlayLook.Current();
    readonly Border _card = new();
    readonly TextBox _filter = new();
    readonly TextBlock _title = new();
    readonly StackPanel _rows = new();
    readonly ScrollViewer _scroll = new();
    readonly TextBlock _empty = new();
    readonly Stack<(string Menu, int Index)> _history = new();
    IReadOnlyList<MenuEntry> _all = Array.Empty<MenuEntry>();
    List<MenuEntry> _shown = new();
    int _index;
    string _menu = MenuModel.Root;
    Mode _mode = Mode.Menu;
    bool _closing;

    internal MenuWindow()
    {
        Title = GlazeConfig.OverlayTitle;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = _look.Scrim;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        _title.FontFamily = OverlayLook.Mono;
        _title.FontSize = 16;
        _title.Foreground = _look.Muted;
        _title.IsHitTestVisible = false;
        _title.VerticalAlignment = VerticalAlignment.Center;
        _title.Margin = new Thickness(2, 0, 0, 0);

        _filter.FontFamily = OverlayLook.Mono;
        _filter.FontSize = 16;
        _filter.Foreground = _look.Foreground;
        _filter.CaretBrush = _look.Accent;
        _filter.SelectionBrush = _look.Accent;
        _filter.Background = Brushes.Transparent;
        _filter.BorderThickness = new Thickness(0);
        _filter.Padding = new Thickness(0);
        _filter.Style = null;
        _filter.Template = FilterTemplate();
        _filter.TextChanged += OnFilterChanged;

        var header = new Grid { Height = 40, Margin = new Thickness(18, 10, 18, 6) };
        header.Children.Add(_title);
        header.Children.Add(_filter);

        _empty.FontFamily = OverlayLook.Mono;
        _empty.FontSize = 14;
        _empty.Foreground = _look.Muted;
        _empty.Margin = new Thickness(18, 4, 18, 16);
        _empty.Text = L.T("menu.nothingFound");
        _empty.Visibility = Visibility.Collapsed;

        _scroll.Content = _rows;
        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _scroll.Focusable = false;
        _scroll.Padding = new Thickness(8, 0, 8, 10);

        var divider = new Border { Height = 1, Background = _look.Divider, Margin = new Thickness(18, 0, 18, 8) };
        var body = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(divider, Dock.Top);
        DockPanel.SetDock(_empty, Dock.Top);
        body.Children.Add(header);
        body.Children.Add(divider);
        body.Children.Add(_empty);
        body.Children.Add(_scroll);

        _card.Child = body;
        _card.Background = _look.Background;
        _card.BorderBrush = _look.Accent;
        _card.BorderThickness = new Thickness(2);
        _card.HorizontalAlignment = HorizontalAlignment.Center;
        _card.VerticalAlignment = VerticalAlignment.Center;
        _card.MouseDown += (_, e) => e.Handled = true;

        var root = new Grid { Background = Brushes.Transparent };
        root.Children.Add(_card);
        root.MouseDown += (_, _) => Dismiss();
        Content = root;

        SourceInitialized += (_, _) => Placement.MakeOverlay(this, clickThrough: false);
        Deactivated += (_, _) => Dismiss();
        PreviewKeyDown += OnKey;
        SizeChanged += (_, _) => _scroll.MaxHeight = Math.Max(200, ActualHeight * 0.66);
    }

    static ControlTemplate FilterTemplate()
    {
        // A bare text field: no border, no focus rectangle, just the caret.
        var t = new ControlTemplate(typeof(TextBox));
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        host.SetValue(ScrollViewer.VerticalAlignmentProperty, VerticalAlignment.Center);
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        t.VisualTree = host;
        return t;
    }

    void Present()
    {
        var area = Placement.CursorWorkArea();
        if (!IsVisible) Show();
        Placement.MovePixels(this, area.Left, area.Top, area.Width, area.Height, activate: true);
        Placement.ForceForeground(this);
        _filter.Focus();
        Keyboard.Focus(_filter);
        if (Ui.Motion.Enabled) Ui.Motion.FadeIn(_card, 6);
    }

    void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    // ------------------------------------------------------------------ content

    internal void ShowEntries(string menu, bool clearHistory, int index = 0)
    {
        if (clearHistory) _history.Clear();
        _mode = Mode.Menu;
        _menu = menu;
        _card.Width = 360;
        _all = MenuModel.Entries(menu);
        SetTitle(MenuModel.Title(menu));
        Reset(index);
    }

    internal void ShowThemeList()
    {
        _history.Clear();
        _mode = Mode.Themes;
        _card.Width = 420;
        var current = AppModel.Settings.Theme;
        _all = ThemeCatalog.All.Select(t => new MenuEntry("themes", t.Title)
        {
            Run = () => ApplyTheme(t),
            Current = t.Name == current,
            Detail = t.Name,
        }).ToList();
        SetTitle(L.T("menu.titles.themes"));
        Reset(Math.Max(0, _all.ToList().FindIndex(e => e.Current)));
    }

    internal void ShowBindings()
    {
        _history.Clear();
        _mode = Mode.Bindings;
        _card.Width = 700;
        var layout = KeyboardLayouts.Current;
        _all = AppModel.Engine.CurrentHotkeys().Active
            .Select(b => new MenuEntry("keyboard", b.Describe())
            {
                Keys = b.Combo?.Display(layout) ?? b.Keys,
                Action = b.Do,
                Detail = b.Omarchy,
            }).ToList();
        SetTitle(L.T("menu.titles.keybindings", ("count", _all.Count)));
        Reset(0);
    }

    void SetTitle(string text) => _title.Text = text;

    void Reset(int index)
    {
        _filter.TextChanged -= OnFilterChanged;
        _filter.Text = "";
        _filter.TextChanged += OnFilterChanged;
        _index = index;
        ApplyFilter(keepSelection: true);
    }

    void OnFilterChanged(object? sender, TextChangedEventArgs e) => ApplyFilter(keepSelection: false);

    void ApplyFilter(bool keepSelection)
    {
        var q = _filter.Text.Trim();
        _title.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Hidden;
        _shown = q.Length == 0
            ? _all.ToList()
            : _all.Where(e => e.Label.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                              (e.Keys?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                              (e.Detail?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false)).ToList();
        if (!keepSelection) _index = 0;
        _index = Math.Clamp(_index, 0, Math.Max(0, _shown.Count - 1));
        _empty.Visibility = _shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Render();
    }

    void Render()
    {
        _rows.Children.Clear();
        for (int i = 0; i < _shown.Count; i++) _rows.Children.Add(Row(_shown[i], i));
        BringSelectedIntoView();
    }

    FrameworkElement Row(MenuEntry entry, int i)
    {
        bool selected = i == _index;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (_mode == Mode.Themes && ThemeCatalog.Find(entry.Detail ?? "") is { } theme)
        {
            grid.Children.Add(Swatches(theme.Palette));
            grid.ColumnDefinitions[0].Width = new GridLength(74);
        }
        else
        {
            var icon = new Ui.Icon { Kind = entry.Icon, Size = 15, Foreground = selected ? _look.Accent : _look.Muted, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(icon);
        }

        var label = new TextBlock
        {
            Text = entry.Label,
            FontFamily = OverlayLook.Mono,
            FontSize = 14.5,
            Foreground = _look.Foreground,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        FrameworkElement? right = null;
        if (entry.Keys != null)
            right = new TextBlock { Text = entry.Keys, FontFamily = OverlayLook.Mono, FontSize = 13, Foreground = selected ? _look.Accent : _look.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        else if (entry.Menu != null)
            right = new Ui.Icon { Kind = "chevron", Size = 13, Foreground = _look.Muted, VerticalAlignment = VerticalAlignment.Center };
        else if (entry.Current)
            right = new Ui.Icon { Kind = "check", Size = 14, Foreground = _look.Accent, VerticalAlignment = VerticalAlignment.Center };
        if (right != null)
        {
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
        }

        var row = new Border
        {
            Child = grid,
            Height = _mode == Mode.Bindings ? 34 : 40,
            Padding = new Thickness(10, 0, 12, 0),
            Background = selected ? _look.Selection : Brushes.Transparent,
            BorderBrush = selected ? _look.Accent : Brushes.Transparent,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Cursor = Cursors.Hand,
        };
        row.MouseEnter += (_, _) => Select(i);
        row.MouseLeftButtonUp += (_, _) => Activate(i);
        return row;
    }

    static FrameworkElement Swatches(Palette p)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var hex in new[] { p.Background, p.Accent, p.Red, p.Green, p.Yellow, p.Foreground })
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var b = new SolidColorBrush(c);
            b.Freeze();
            panel.Children.Add(new Border { Width = 9, Height = 16, Background = b, Margin = new Thickness(0, 0, 1, 0) });
        }
        return panel;
    }

    void Select(int i)
    {
        if (i == _index || i < 0 || i >= _shown.Count) return;
        int old = _index;
        _index = i;
        if (old < _rows.Children.Count) _rows.Children[old] = Row(_shown[old], old);
        _rows.Children[i] = Row(_shown[i], i);
        BringSelectedIntoView();
    }

    void BringSelectedIntoView()
    {
        if (_index < _rows.Children.Count && _rows.Children[_index] is FrameworkElement f)
            Dispatcher.InvokeAsync(() => f.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ------------------------------------------------------------------ keys

    void OnKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool filterEmpty = _filter.Text.Length == 0;
        switch (key)
        {
            case Key.Down:
            case Key.Tab when Keyboard.Modifiers != ModifierKeys.Shift:
                Move(1);
                break;
            case Key.Up:
            case Key.Tab:
                Move(-1);
                break;
            case Key.PageDown: Move(8); break;
            case Key.PageUp: Move(-8); break;
            case Key.Home when filterEmpty: Select(0); break;
            case Key.End when filterEmpty: Select(_shown.Count - 1); break;
            case Key.Enter:
                Activate(_index);
                break;
            case Key.Right when filterEmpty && _index < _shown.Count && _shown[_index].Menu != null:
                Activate(_index);
                break;
            case Key.Escape:
                if (!filterEmpty) _filter.Text = "";
                else if (!Back()) Dismiss();
                break;
            case Key.Back when filterEmpty:
            case Key.Left when filterEmpty:
                Back();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    void Move(int step)
    {
        if (_shown.Count == 0) return;
        var next = _index + step;
        if (Math.Abs(step) == 1) next = (next + _shown.Count) % _shown.Count;
        Select(Math.Clamp(next, 0, _shown.Count - 1));
    }

    bool Back()
    {
        if (_mode != Mode.Menu || _history.Count == 0) return false;
        var (menu, index) = _history.Pop();
        ShowEntries(menu, clearHistory: false, index);
        return true;
    }

    void Activate(int i)
    {
        if (i < 0 || i >= _shown.Count) return;
        var entry = _shown[i];
        if (entry.Menu != null)
        {
            _history.Push((_menu, _all.ToList().IndexOf(entry)));
            ShowEntries(entry.Menu, clearHistory: false);
            return;
        }
        Dismiss();
        // The menu closes first, so keys and windows go to the program that was in front.
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (entry.Action != null) AppModel.Agent?.RunAction(entry.Action);
                else entry.Run?.Invoke();
            }
            catch (Exception ex)
            {
                Osd.Show(Net.Describe(ex));
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    static void ApplyTheme(OmarchyTheme theme)
    {
        var steps = AppModel.Engine.ThemeSteps(theme, downloadWallpapers: AppModel.Settings.ThemeTargets.Wallpaper);
        var job = AppModel.Run("theme", "theme", L.T("jobs.theme", ("theme", theme.Title)), steps, j =>
        {
            Osd.Show(j.State == JobState.Done ? L.T("themes.applied", ("theme", theme.Title)) : L.T("menu.themeFailed", ("theme", theme.Title), ("detail", j.Status)));
            return Task.CompletedTask;
        });
        if (job != null) Osd.Show(L.T("menu.themeApplying", ("theme", theme.Title)));
    }
}

/// <summary>The menu card without a window on the desktop, for the self test's screenshots.</summary>
sealed class MenuPreview
{
    readonly MenuWindow _window = new();

    public FrameworkElement Card => (FrameworkElement)((Grid)_window.Content).Children[0];

    public void Root() => Show(w => w.ShowEntries(MenuModel.Root, clearHistory: true));

    public void Sub(string menu) => Show(w => w.ShowEntries(menu, clearHistory: true));

    public void Themes() => Show(w => w.ShowThemeList());

    public void Bindings() => Show(w => w.ShowBindings());

    void Show(Action<MenuWindow> build)
    {
        build(_window);
        if (((Border)Card).Child is DockPanel body && body.Children.OfType<ScrollViewer>().FirstOrDefault() is { } scroll) scroll.MaxHeight = 620;
    }
}

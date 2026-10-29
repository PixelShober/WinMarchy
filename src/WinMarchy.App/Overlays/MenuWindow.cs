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
/// Enter runs, Escape or Backspace goes back. Typing in the main menu also finds programs from the
/// Start menu. The same window shows the theme switcher and the list of shortcuts (Enter on a
/// shortcut runs it, as in Omarchy).
/// </summary>
public sealed class MenuWindow : Window
{
    static MenuWindow? _current;

    public static void ShowMenu(string? name)
    {
        if (name?.Trim() == "wallpaper") ShowWallpapers();
        else Open(w => w.ShowEntries(string.IsNullOrWhiteSpace(name) ? MenuModel.Root : name.Trim(), clearHistory: true));
    }

    public static void ShowThemes() => Open(w => w.ShowThemeList());

    public static void ShowWallpapers() => Open(w => w.ShowWallpaperList());

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

    enum Mode { Menu, Themes, Bindings, Wallpapers }

    /// <summary>Wallpaper thumbnails, decoded once per file.</summary>
    static readonly Dictionary<string, ImageSource?> Thumbnails = new(StringComparer.OrdinalIgnoreCase);

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
    /// <summary>
    /// Where the mouse was when it last chose a row. Rows are rebuilt and scrolled while the arrow
    /// keys move; the row that ends up under a resting mouse must not take the selection back.
    /// </summary>
    System.Drawing.Point _mouse;

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
        _mouse = System.Windows.Forms.Cursor.Position;
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
        if (menu == MenuModel.Root)
        {
            // The program list loads in the background; a search typed meanwhile is shown again with it.
            _ = Core.Platform.StartApps.Refresh().ContinueWith(_ => Dispatcher.InvokeAsync(() =>
            {
                if (_mode == Mode.Menu && _menu == MenuModel.Root && _filter.Text.Trim().Length > 0) ApplyFilter(keepSelection: true);
            }), TaskScheduler.Default);
        }
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
        var active = AppModel.Engine.CurrentHotkeys().Active;
        var favorites = BindingRows(active.Where(b => b.Favorite), layout);
        var optional = BindingRows(active.Where(b => !b.Favorite), layout);
        var list = new List<MenuEntry>(favorites);
        if (optional.Count > 0)
        {
            list.Add(MenuEntry.Section(L.T("menu.keybindingsOptional")));
            list.AddRange(optional);
        }
        _all = list;
        SetTitle(L.T("menu.titles.keybindings", ("count", favorites.Count + optional.Count)));
        Reset(0);
    }

    /// <summary>One row per shortcut; the ten of "Win+1 … 0" become one row.</summary>
    static List<MenuEntry> BindingRows(IEnumerable<HotkeyBinding> bindings, IKeyboardLayout layout)
    {
        var rows = new List<MenuEntry>();
        var seen = new HashSet<string>();
        foreach (var b in bindings)
        {
            if (b.Template != null)
            {
                if (!seen.Add(b.Template)) continue;
                var keys = (b.Combo?.Display(layout) ?? b.Keys);
                var label = b.TemplateLabel?.ToString().Replace("{ws}", "1–10") ?? b.Describe();
                rows.Add(new MenuEntry("keyboard", label) { Keys = keys[..^1] + "1 … 0", Detail = b.Omarchy });
                continue;
            }
            rows.Add(new MenuEntry("keyboard", b.Describe()) { Keys = b.Combo?.Display(layout) ?? b.Keys, Action = b.Do, Detail = b.Omarchy });
        }
        return rows;
    }

    /// <summary>
    /// Omarchy's background picker: the pictures of the current theme with a preview, Enter sets one.
    /// Missing pictures can be loaded from here, and the current one can go to the lock screen.
    /// </summary>
    internal void ShowWallpaperList()
    {
        _history.Clear();
        _mode = Mode.Wallpapers;
        _card.Width = 520;
        var theme = AppModel.CurrentTheme;
        var files = ThemeAssets.Backgrounds(theme);
        var official = ThemeAssets.DownloadedBackgrounds(theme).Count;
        int current = -1;
        if (files.Count > 0)
        {
            var index = AppState.Load().WallpaperIndex.GetValueOrDefault(theme.Name);
            current = ((index % files.Count) + files.Count) % files.Count;
        }
        var list = files.Select((f, i) => new MenuEntry("image", PictureName(f))
        {
            Run = () => SetWallpaper(i),
            Current = i == current,
            Detail = f,
        }).ToList();
        if (official < theme.Backgrounds.Count)
            list.Add(new MenuEntry("download", L.T("menu.wallpaper.load", ("theme", theme.Title), ("count", theme.Backgrounds.Count - official))) { Run = () => LoadWallpapers(theme) });
        list.Add(new MenuEntry("plus", L.T("menu.wallpaper.addOwn")) { Run = () => AddOwnWallpapers(theme) });
        if (files.Count > 0)
            list.Add(new MenuEntry("lock", L.T("menu.wallpaper.lockScreen")) { Run = WallpaperToLockScreen });
        _all = list;
        SetTitle(L.T("menu.titles.wallpaper", ("theme", theme.Title)));
        Reset(Math.Max(0, current));
    }

    /// <summary>"1-totoro.png" → "Totoro".</summary>
    static string PictureName(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var dash = name.IndexOf('-');
        if (dash > 0 && name[..dash].All(char.IsDigit)) name = name[(dash + 1)..];
        name = name.Replace('-', ' ').Replace('_', ' ').Trim();
        return name.Length == 0 ? Path.GetFileName(file) : char.ToUpper(name[0], L.Culture) + name[1..];
    }

    static void SetWallpaper(int index) => _ = Task.Run(() =>
    {
        try
        {
            var file = AppModel.Engine.SetWallpaper(index);
            Osd.Show(file != null ? L.T("agent.wallpaper", ("name", PictureName(file))) : L.T("agent.noWallpapers"));
        }
        catch (Exception ex)
        {
            Osd.Show(Net.Describe(ex));
        }
    });

    static void WallpaperToLockScreen() => _ = Task.Run(() =>
    {
        try
        {
            Osd.Show(AppModel.Engine.WallpaperToLockScreen() ? L.T("menu.wallpaper.lockScreenDone") : L.T("menu.wallpaper.lockScreenFailed"));
        }
        catch (Exception ex)
        {
            Osd.Show(Net.Describe(ex));
        }
    });

    /// <summary>Own pictures for the current theme; the newest one is set right away.</summary>
    static void AddOwnWallpapers(OmarchyTheme theme)
    {
        var d = new Microsoft.Win32.OpenFileDialog { Title = L.T("menu.wallpaper.addOwn"), Filter = L.T("themes.imageFilter"), Multiselect = true };
        if (d.ShowDialog() != true) return;
        try
        {
            var count = ThemeAssets.AddOwn(theme, d.FileNames);
            Osd.Show(L.T("themes.ownAdded", ("count", count), ("theme", theme.Title)));
            var index = ThemeAssets.OwnBackgrounds(theme).ToList().FindIndex(f => Path.GetFileNameWithoutExtension(f).StartsWith(Path.GetFileNameWithoutExtension(d.FileNames[0]), StringComparison.OrdinalIgnoreCase));
            if (count > 0) SetWallpaper(Math.Max(0, index));
        }
        catch (Exception ex)
        {
            Osd.Show(Net.Describe(ex));
        }
    }

    static void LoadWallpapers(OmarchyTheme theme)
    {
        var step = new SetupStep(L.T("steps.themeAssets"), async (st, ct) =>
        {
            using var http = Net.CreateClient(TimeSpan.FromMinutes(10));
            await ThemeAssets.DownloadAsync(http, theme, backgrounds: true, t => st.Report(t), ct);
        });
        var job = AppModel.Run("wallpapers", "wallpapers", L.T("jobs.wallpapers", ("theme", theme.Title)), new List<SetupStep> { step }, j =>
        {
            Osd.Show(j.State == JobState.Done ? L.T("menu.wallpaper.loaded", ("theme", theme.Title)) : j.Status);
            if (j.State == JobState.Done) ShowWallpapers();
            return Task.CompletedTask;
        });
        if (job != null) Osd.Show(L.T("menu.wallpaper.loading", ("theme", theme.Title)));
    }

    static ImageSource? Thumbnail(string file)
    {
        if (Thumbnails.TryGetValue(file, out var cached)) return cached;
        var image = Ui.UiKit.LoadImage(file, 128);
        Thumbnails[file] = image;
        return image;
    }

    void SetTitle(string text) => _title.Text = text;

    /// <summary>For the self test: as if the text had been typed.</summary>
    internal void Type(string text) => _filter.Text = text;

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
        bool Matches(MenuEntry e) => !e.Header && (e.Label.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                              (e.Keys?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                              (_mode != Mode.Wallpapers && (e.Detail?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false)));
        if (q.Length == 0) _shown = _all.ToList();
        else if (_mode == Mode.Menu && _menu == MenuModel.Root) _shown = SearchEverything(q, Matches);
        else _shown = _all.Where(Matches).ToList();
        if (!keepSelection) _index = 0;
        _index = Math.Clamp(_index, 0, Math.Max(0, _shown.Count - 1));
        if (_index < _shown.Count && _shown[_index].Header) _index = NextSelectable(_index, 1);
        _empty.Visibility = _shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Render();
    }

    /// <summary>The main menu's search: programs from the Start menu first, then WinMarchy's own entries.</summary>
    static List<MenuEntry> SearchEverything(string q, Func<MenuEntry, bool> matches)
    {
        var apps = Core.Platform.StartApps.Search(Core.Platform.StartApps.Current, q)
            .Select(a => new MenuEntry("grid", a.Name) { Run = () => Core.Platform.StartApps.Launch(a) }).ToList();
        var own = MenuModel.Searchable().Where(matches).ToList();
        var list = new List<MenuEntry>();
        if (apps.Count > 0)
        {
            list.Add(MenuEntry.Section(L.T("menu.sectionApps")));
            list.AddRange(apps);
        }
        if (own.Count > 0)
        {
            if (apps.Count > 0) list.Add(MenuEntry.Section("WinMarchy"));
            list.AddRange(own);
        }
        return list;
    }

    /// <summary>The next row that can be selected from <paramref name="from"/> on (headers are skipped).</summary>
    int NextSelectable(int from, int step)
    {
        for (int i = from; i >= 0 && i < _shown.Count; i += step)
            if (!_shown[i].Header) return i;
        for (int i = from - step; i >= 0 && i < _shown.Count; i -= step)
            if (!_shown[i].Header) return i;
        return Math.Clamp(from, 0, Math.Max(0, _shown.Count - 1));
    }

    void Render()
    {
        _rows.Children.Clear();
        for (int i = 0; i < _shown.Count; i++) _rows.Children.Add(Row(_shown[i], i));
        BringSelectedIntoView();
    }

    FrameworkElement Row(MenuEntry entry, int i)
    {
        if (entry.Header)
        {
            return new TextBlock
            {
                Text = entry.Label,
                FontFamily = OverlayLook.Mono,
                FontSize = 12.5,
                Foreground = _look.Muted,
                Margin = new Thickness(13, i == 0 ? 2 : 12, 12, 4),
            };
        }
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
        else if (_mode == Mode.Wallpapers && entry.Detail != null)
        {
            grid.Children.Add(new Border
            {
                Width = 64,
                Height = 36,
                BorderThickness = new Thickness(1),
                BorderBrush = selected ? _look.Accent : _look.Divider,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new Image { Source = Thumbnail(entry.Detail), Stretch = Stretch.UniformToFill },
            });
            grid.ColumnDefinitions[0].Width = new GridLength(78);
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
            Height = _mode switch { Mode.Bindings => 34, Mode.Wallpapers when entry.Detail != null => 48, _ => 40 },
            Padding = new Thickness(10, 0, 12, 0),
            Background = selected ? _look.Selection : Brushes.Transparent,
            BorderBrush = selected ? _look.Accent : Brushes.Transparent,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Cursor = Cursors.Hand,
        };
        // Only a mouse that really moved chooses a row (see _mouse).
        row.MouseMove += (_, _) =>
        {
            var at = System.Windows.Forms.Cursor.Position;
            if (Math.Abs(at.X - _mouse.X) + Math.Abs(at.Y - _mouse.Y) < 2) return;
            _mouse = at;
            Select(i);
        };
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
        if (i == _index || i < 0 || i >= _shown.Count || _shown[i].Header) return;
        int old = _index;
        _index = i;
        Replace(old);
        Replace(i);
        BringSelectedIntoView();
    }

    /// <summary>
    /// Swaps one row for a fresh one. A panel slot has to be emptied first: assigning to an occupied
    /// index throws ("Specified index is already in use").
    /// </summary>
    void Replace(int i)
    {
        if (i < 0 || i >= _rows.Children.Count || i >= _shown.Count) return;
        _rows.Children.RemoveAt(i);
        _rows.Children.Insert(i, Row(_shown[i], i));
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
            case Key.Home when filterEmpty: Select(NextSelectable(0, 1)); break;
            case Key.End when filterEmpty: Select(NextSelectable(_shown.Count - 1, -1)); break;
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
        if (_shown.Count == 0 || _shown.All(e => e.Header)) return;
        var next = _index + step;
        if (Math.Abs(step) == 1)
        {
            // Past a section title, and round from the last row to the first.
            next = (next + _shown.Count) % _shown.Count;
            while (_shown[next].Header) next = (next + step + _shown.Count) % _shown.Count;
            Select(next);
            return;
        }
        Select(NextSelectable(Math.Clamp(next, 0, _shown.Count - 1), Math.Sign(step)));
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
        if (i < 0 || i >= _shown.Count || _shown[i].Header) return;
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
        var job = AppModel.Run("theme", "theme", L.T("jobs.theme", ("theme", theme.Title)), steps);
        // A notification with a progress bar that follows the steps and says how it ended.
        if (job != null)
            Osd.Follow(job.Id, L.T("menu.themeApplying", ("theme", theme.Title)),
                L.T("themes.applied", ("theme", theme.Title)), status => L.T("menu.themeFailed", ("theme", theme.Title), ("detail", status)));
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

    public void Wallpapers() => Show(w => w.ShowWallpaperList());

    public void Bindings() => Show(w => w.ShowBindings());

    /// <summary>The main menu with something typed: programs and WinMarchy's own entries.</summary>
    public void Search(string text) => Show(w =>
    {
        w.ShowEntries(MenuModel.Root, clearHistory: true);
        w.Type(text);
    });

    void Show(Action<MenuWindow> build)
    {
        build(_window);
        if (((Border)Card).Child is DockPanel body && body.Children.OfType<ScrollViewer>().FirstOrDefault() is { } scroll) scroll.MaxHeight = 620;
    }
}

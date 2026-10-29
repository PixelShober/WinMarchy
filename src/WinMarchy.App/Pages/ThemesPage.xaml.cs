using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using WinMarchy.App.Ui;
using WinMarchy.Core;
using WinMarchy.Core.Themes;

namespace WinMarchy.App.Pages;

public partial class ThemesPage : UserControl, IPage
{
    OmarchyTheme _selected = AppModel.CurrentTheme;
    readonly Dictionary<string, Border> _tiles = new();

    static readonly (string Id, Func<ThemeTargets, bool> Get, Action<ThemeTargets, bool> Set)[] TargetList =
    {
        ("glazewm", t => t.GlazeWm, (t, v) => t.GlazeWm = v),
        ("zebar", t => t.Zebar, (t, v) => t.Zebar = v),
        ("terminal", t => t.Terminal, (t, v) => t.Terminal = v),
        ("powershell", t => t.PowerShell, (t, v) => t.PowerShell = v),
        ("vscode", t => t.VsCode, (t, v) => t.VsCode = v),
        ("flow", t => t.Flow, (t, v) => t.Flow = v),
        ("files", t => t.Files, (t, v) => t.Files = v),
        ("windows", t => t.Windows, (t, v) => t.Windows = v),
        ("wallpaper", t => t.Wallpaper, (t, v) => t.Wallpaper = v),
        ("lockscreen", t => t.LockScreen, (t, v) => t.LockScreen = v),
    };

    public ThemesPage()
    {
        InitializeComponent();
        Credit.Text = L.T("themes.credit", ("tag", ThemeCatalog.Manifest.Tag));
        foreach (var (id, get, set) in TargetList)
        {
            var box = new CheckBox { Content = L.T("themes.target." + id), IsChecked = get(AppModel.Settings.ThemeTargets), Margin = new Thickness(0, 0, 16, 8) };
            box.Click += (_, _) => AppModel.Update(s => set(s.ThemeTargets, box.IsChecked == true));
            Targets.Children.Add(box);
        }
        BuildGallery();
        if (AppModel.Jobs.Get("theme") != null) Job.Follow("theme");
        else if (AppModel.Jobs.Get("wallpapers") != null) Job.Follow("wallpapers");
    }

    public void OnShown()
    {
        _selected = AppModel.CurrentTheme;
        Select(_selected);
        RefreshPreviewButton();
        foreach (var theme in ThemeCatalog.All) UpdateTile(theme);
    }

    void BuildGallery()
    {
        foreach (var theme in ThemeCatalog.All)
        {
            var image = new Image { Stretch = System.Windows.Media.Stretch.UniformToFill, Height = 104 };
            var palette = new UniformGrid { Rows = 1, Height = 104 };
            var p = theme.Palette;
            foreach (var c in new[] { p.Background, p.LighterBackground, p.Accent, p.Foreground }) palette.Children.Add(UiKit.Swatch(c, 104));
            var frame = new Grid { ClipToBounds = true };
            frame.Children.Add(palette);
            frame.Children.Add(image);
            var label = new DockPanel { Margin = new Thickness(10, 8, 10, 9) };
            var mode = UiKit.Text(theme.IsLight ? L.T("common.light") : L.T("common.dark"), "Small");
            DockPanel.SetDock(mode, Dock.Right);
            label.Children.Add(mode);
            label.Children.Add(UiKit.Text(theme.Title, "H3"));
            var stack = new StackPanel();
            stack.Children.Add(frame);
            stack.Children.Add(label);
            var tile = new Border
            {
                Child = stack,
                Margin = new Thickness(0, 0, 14, 14),
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
                Focusable = true,
                Tag = image,
                FocusVisualStyle = (Style)FindResource("FocusRing"),
            };
            tile.SetResourceReference(Border.BackgroundProperty, "Surface");
            tile.SetResourceReference(Border.BorderBrushProperty, "Border");
            AutomationName(tile, theme.Title);
            tile.MouseLeftButtonUp += (_, _) => Select(theme);
            tile.KeyDown += (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Space) Select(theme);
            };
            _tiles[theme.Name] = tile;
            Gallery.Items.Add(tile);
        }
    }

    static void AutomationName(UIElement e, string name) => System.Windows.Automation.AutomationProperties.SetName(e, name);

    void UpdateTile(OmarchyTheme theme)
    {
        if (!_tiles.TryGetValue(theme.Name, out var tile)) return;
        ((Image)tile.Tag).Source = UiKit.LoadImage(ThemeAssets.PreviewFile(theme), 360);
        // The selected theme gets the 2 px accent frame, as GlazeWM draws it around the focused window.
        tile.SetResourceReference(Border.BorderBrushProperty, theme.Name == _selected.Name ? "Accent" : "Border");
    }

    void Select(OmarchyTheme theme)
    {
        var previous = _selected;
        _selected = theme;
        UpdateTile(previous);
        UpdateTile(theme);
        var p = theme.Palette;
        SelName.Text = theme.Title;
        SelMode.Child = UiKit.Badge(theme.IsLight ? L.T("common.light") : L.T("common.dark"), "muted");
        SelCurrent.Child = theme.Name == AppModel.Settings.Theme ? UiKit.Badge(L.T("themes.active"), "accent") : null;
        HeroImage.Source = UiKit.LoadImage(ThemeAssets.PreviewFile(theme), 1000);
        HeroPalette.Children.Clear();
        foreach (var c in new[] { p.Background, p.LighterBackground, p.Selection, p.Accent, p.Foreground }) HeroPalette.Children.Add(UiKit.Swatch(c, 300));
        SelPalette.Children.Clear();
        foreach (var c in new[] { p.Background, p.Foreground, p.Accent, p.Red, p.Green, p.Yellow, p.Blue, p.Magenta, p.Cyan, p.Muted })
            SelPalette.Children.Add(UiKit.Swatch(c, 18));
        var have = ThemeAssets.DownloadedBackgrounds(theme).Count;
        WallpaperInfo.Text = theme.Backgrounds.Count == 0 ? L.T("themes.noWallpapers")
            : L.T("themes.wallpapers", ("have", have), ("total", theme.Backgrounds.Count), ("size", Net.Mb(theme.Backgrounds.Sum(b => b.Size))));
        ApplyButton.Content = theme.Name == AppModel.Settings.Theme ? L.T("themes.reapply") : L.T("themes.apply");
    }

    void RefreshPreviewButton()
    {
        var missing = ThemeCatalog.All.Where(t => ThemeAssets.PreviewFile(t) == null).ToList();
        LoadPreviews.Visibility = missing.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        LoadPreviews.Content = L.T("themes.loadPreviews", ("size", Net.Mb(missing.Sum(t => t.Preview?.Size ?? 0))));
        var bytes = Wallpapers.AllMissingBytes();
        LoadAllWallpapers.Visibility = bytes == 0 ? Visibility.Collapsed : Visibility.Visible;
        LoadAllWallpapers.Content = L.T("themes.loadAllWallpapers", ("size", Net.Mb(bytes)));
    }

    /// <summary>The wallpapers of all 22 themes at once, so that every theme switch has its pictures.</summary>
    void OnLoadAllWallpapers(object sender, RoutedEventArgs e)
    {
        var job = AppModel.Run("wallpapers", "wallpapers", L.T("jobs.allWallpapers"),
            new List<SetupStep> { AppModel.Engine.DownloadAllWallpapersStep() }, async j =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (j.State == JobState.Done) Toasts.Show(ToastKind.Ok, L.T("themes.allWallpapersLoaded", ("count", Wallpapers.AllCount())));
                    OnShown();
                });
            });
        if (job != null) Job.Follow(job.Id);
    }

    async void OnLoadPreviews(object sender, RoutedEventArgs e)
    {
        LoadPreviews.IsEnabled = false;
        try
        {
            using var http = Net.CreateClient(TimeSpan.FromMinutes(5));
            foreach (var theme in ThemeCatalog.All.Where(t => ThemeAssets.PreviewFile(t) == null))
            {
                await ThemeAssets.DownloadAsync(http, theme, backgrounds: false, null, CancellationToken.None);
                UpdateTile(theme);
            }
            Select(_selected);
            Toasts.Show(ToastKind.Ok, L.T("themes.previewsLoaded"));
        }
        catch (Exception ex)
        {
            Toasts.Show(ToastKind.Error, L.T("themes.previewsFailed", ("detail", Net.Describe(ex))));
        }
        finally
        {
            LoadPreviews.IsEnabled = true;
            RefreshPreviewButton();
        }
    }

    void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = L.T("themes.importTitle") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var n = ThemeAssets.ImportFolder(dialog.FolderName, null);
        Toasts.Show(n > 0 ? ToastKind.Ok : ToastKind.Error, n > 0 ? L.T("themes.importDone", ("count", n)) : L.T("themes.importNone"));
        OnShown();
    }

    void OnApply(object sender, RoutedEventArgs e)
    {
        var theme = _selected;
        var job = AppModel.Run("theme", "theme", L.T("jobs.theme", ("theme", theme.Title)),
            AppModel.Engine.ThemeSteps(theme, WithWallpapers.IsChecked == true), async j =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (j.State == JobState.Done) Toasts.Show(ToastKind.Ok, L.T("themes.applied", ("theme", theme.Title)));
                    OnShown();
                });
            });
        if (job != null) Job.Follow(job.Id);
    }
}

using System.Windows;
using System.Windows.Controls;
using WinMarchy.App.Ui;
using WinMarchy.Core;
using WinMarchy.Core.Themes;
using WinMarchy.Core.Tools;

namespace WinMarchy.App.Pages;

public partial class OverviewPage : UserControl, IPage
{
    public OverviewPage()
    {
        InitializeComponent();
        AppModel.SettingsChanged += () => Dispatcher.InvokeAsync(Refresh);
    }

    public void OnShown() => Refresh();

    void Refresh()
    {
        var state = AppState.Load();
        SetupNote.Visibility = state.SetupDone ? Visibility.Collapsed : Visibility.Visible;
        if (Directory.Exists(Locations.Current.LegacyStateDir) && !state.SetupDone)
            SetupNoteText.Text = L.T("overview.notSetUpLegacy");
        RenderTools();
        RenderTheme();
        RenderWorkspace();
    }

    void RenderTools()
    {
        ToolTable.Children.Clear();
        ToolTable.RowDefinitions.Clear();
        ToolTable.ColumnDefinitions.Clear();
        foreach (var w in new[] { "Auto", "*", "Auto" })
            ToolTable.ColumnDefinitions.Add(new ColumnDefinition { Width = (GridLength)new GridLengthConverter().ConvertFromString(w)! });

        int row = 0;
        foreach (var tool in ToolCatalog.All)
        {
            var status = AppModel.Engine.Components.Status(tool);
            ToolTable.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var name = new StackPanel { Margin = new Thickness(0, 0, 18, 12) };
            name.Children.Add(UiKit.Text(tool.Name, "H3"));
            name.Children.Add(UiKit.Text(L.T("tools.roles." + tool.Id), "Small"));
            Grid.SetRow(name, row);
            ToolTable.Children.Add(name);

            var where = UiKit.Text(status.Installed ? (status.Version != null ? $"{status.Version} · {Shorten(status.Path!)}" : Shorten(status.Path!)) : L.T("tools.notFound"), "Small");
            where.FontFamily = (System.Windows.Media.FontFamily)FindResource("MonoFont");
            where.TextTrimming = TextTrimming.CharacterEllipsis;
            where.ToolTip = status.Path;
            where.Margin = new Thickness(0, 3, 12, 12);
            Grid.SetRow(where, row);
            Grid.SetColumn(where, 1);
            ToolTable.Children.Add(where);

            var badge = status.Running ? UiKit.Badge(L.T("tools.running"), "ok")
                : status.Installed ? UiKit.Badge(L.T("tools.installedBadge"), "muted")
                : tool.Required ? UiKit.Badge(L.T("tools.missing"), "warn")
                : UiKit.Badge(L.T("tools.optional"), "muted");
            badge.Margin = new Thickness(0, 2, 0, 12);
            badge.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetRow(badge, row);
            Grid.SetColumn(badge, 2);
            ToolTable.Children.Add(badge);
            row++;
        }
    }

    static string Shorten(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length > 0 && path.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "%USERPROFILE%" + path[home.Length..] : path;
    }

    void RenderTheme()
    {
        var theme = AppModel.CurrentTheme;
        var p = theme.Palette;
        ThemeName.Text = theme.Title + (theme.IsLight ? " · " + L.T("common.light") : " · " + L.T("common.dark"));
        Preview.Source = UiKit.LoadImage(ThemeAssets.PreviewFile(theme), 640);
        PaletteBig.Children.Clear();
        if (Preview.Source == null)
            foreach (var c in new[] { p.Background, p.LighterBackground, p.Accent, p.Foreground })
                PaletteBig.Children.Add(UiKit.Swatch(c, 150));
        Palette.Children.Clear();
        foreach (var c in new[] { p.Background, p.Foreground, p.Accent, p.Red, p.Green, p.Yellow, p.Blue, p.Magenta, p.Cyan })
            Palette.Children.Add(UiKit.Swatch(c));
    }

    void RenderWorkspace()
    {
        var agent = AppModel.Agent;
        if (agent?.Glaze is { Connected: true })
        {
            WorkspaceText.Text = agent.StatusLine();
            WorkspaceHint.Text = L.T("overview.workspaceHint");
        }
        else
        {
            WorkspaceText.Text = L.T("status.glazeStopped");
            WorkspaceHint.Text = L.T("overview.glazeHint");
        }
    }

    void OnRefresh(object sender, RoutedEventArgs e)
    {
        Refresh();
        Toasts.Show(ToastKind.Ok, L.T("overview.refreshed"));
    }

    void OnSetup(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.Navigate("setup");

    void OnThemes(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.Navigate("themes");
}

using System.Windows;
using System.Windows.Controls;
using WinMarchy.App.Ui;
using WinMarchy.Core;
using WinMarchy.Core.Themes;
using WinMarchy.Core.Tools;

namespace WinMarchy.App.Pages;

public partial class OverviewPage : UserControl, IPage
{
    /// <summary>A lookup of the latest releases is under way.</summary>
    bool _checking;
    /// <summary>The programs with a newer release, for "Update all".</summary>
    List<ToolDef> _newer = new();
    /// <summary>Follows the agent's pause (the agent starts after this page may have been made).</summary>
    bool _followsPause;
    /// <summary>The WinMarchy menu asked for a lookup of new releases now (<see cref="RequestCheck"/>).</summary>
    static bool _checkRequested;
    static event Action? CheckRequested;

    /// <summary>
    /// "Update programs" in the WinMarchy menu: the next time Start shows (or now, if it shows), it
    /// looks for new releases at once, however recent the last lookup was.
    /// </summary>
    public static void RequestCheck()
    {
        _checkRequested = true;
        CheckRequested?.Invoke();
    }

    public OverviewPage()
    {
        InitializeComponent();
        AppModel.SettingsChanged += () => Dispatcher.InvokeAsync(Refresh);
        ToolUpdates.Changed += () => Dispatcher.InvokeAsync(() => { if (IsVisible) RenderTools(); });
        // "Update to …" and "Update all" wait while anything installs.
        InstallLock.OnChange(() => { if (IsVisible) RenderTools(); });
        CheckRequested += () => Dispatcher.InvokeAsync(() =>
        {
            if (!IsVisible) return;
            _checkRequested = false;
            _ = CheckVersionsAsync(TimeSpan.Zero);
        });
    }

    public void OnShown()
    {
        if (!_followsPause && AppModel.Agent is { } agent)
        {
            agent.PausedChanged += () => Dispatcher.InvokeAsync(RenderPause);
            _followsPause = true;
        }
        Refresh();
        var maxAge = _checkRequested ? TimeSpan.Zero : ToolUpdates.MaxAge;
        _checkRequested = false;
        _ = CheckVersionsAsync(maxAge);
    }

    void Refresh()
    {
        var state = AppState.Load();
        SetupNote.Visibility = state.SetupDone ? Visibility.Collapsed : Visibility.Visible;
        if (Directory.Exists(Locations.Current.LegacyStateDir) && !state.SetupDone)
            SetupNoteText.Text = L.T("overview.notSetUpLegacy");
        RenderTools();
        RenderTheme();
        RenderWorkspace();
        RenderPause();
    }

    /// <summary>"Pause WinMarchy" or "Resume WinMarchy", once it is set up and runs in the background.</summary>
    void RenderPause()
    {
        var agent = AppModel.Agent;
        PauseButton.Visibility = agent != null && AppState.Load().SetupDone ? Visibility.Visible : Visibility.Collapsed;
        bool paused = agent?.Paused == true;
        PauseIcon.Kind = paused ? "play" : "pause";
        PauseText.Text = L.T(paused ? "overview.resume" : "overview.pause");
        PauseButton.ToolTip = L.T(paused ? "overview.resumeHint" : "overview.pauseHint");
    }

    void OnPause(object sender, RoutedEventArgs e)
    {
        if (AppModel.Agent is { } agent) _ = agent.TogglePauseAsync(CancellationToken.None);
    }

    void RenderTools()
    {
        ToolTable.Children.Clear();
        ToolTable.RowDefinitions.Clear();
        ToolTable.ColumnDefinitions.Clear();
        foreach (var w in new[] { "Auto", "*", "Auto", "Auto" })
            ToolTable.ColumnDefinitions.Add(new ColumnDefinition { Width = (GridLength)new GridLengthConverter().ConvertFromString(w)! });

        var state = AppState.Load();
        var updatable = ToolUpdates.Updatable(state);
        var newer = new List<ToolDef>();
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

            if (updatable.Contains(tool) && VersionCell(status, state) is { } version)
            {
                if (ToolUpdates.NewerTag(status, state) != null) newer.Add(tool);
                version.Margin = new Thickness(0, 0, 12, 12);
                version.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetRow(version, row);
                Grid.SetColumn(version, 2);
                ToolTable.Children.Add(version);
            }

            var badge = status.Running ? UiKit.Badge(L.T("tools.running"), "ok")
                : status.Installed ? UiKit.Badge(L.T("tools.installedBadge"), "muted")
                : tool.Required ? UiKit.Badge(L.T("tools.missing"), "warn")
                : UiKit.Badge(L.T("tools.optional"), "muted");
            badge.Margin = new Thickness(0, 2, 0, 12);
            badge.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetRow(badge, row);
            Grid.SetColumn(badge, 3);
            ToolTable.Children.Add(badge);
            row++;
        }

        _newer = newer;
        UpdateAll.Content = L.T("overview.updateAll", ("count", newer.Count));
        UpdateAll.Visibility = newer.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
        UpdateAll.IsEnabled = !InstallLock.Busy;
        VersionsChecked.Text = _checking ? L.T("overview.versionsChecking")
            : updatable.Count == 0 ? L.T("overview.versionsNone")
            : ToolUpdates.CheckedUtc(state) is { } checkedUtc ? L.T("overview.versionsChecked", ("date", checkedUtc.ToLocalTime().ToString("g", L.Culture)))
            : L.T("overview.versionsUnknown");
    }

    /// <summary>"Update to 1.2.3" for a newer release, "Current" for the latest one; null while it was never looked up.</summary>
    FrameworkElement? VersionCell(ToolStatus status, AppState state)
    {
        if (ToolUpdates.NewerTag(status, state) is { } tag)
        {
            var button = UiKit.IconTextButton("download", L.T("overview.updateTo", ("version", tag)));
            button.ToolTip = L.T("overview.updateToHint", ("tool", status.Tool.Name));
            button.IsEnabled = !InstallLock.Busy;
            button.Click += (_, _) => RunUpdates(new[] { status.Tool }, L.T("jobs.toolUpdate", ("tool", status.Tool.Name)));
            return button;
        }
        if (!ToolUpdates.IsCurrent(status, state)) return null;
        var current = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        var check = new Icon { Kind = "check", Size = 13, VerticalAlignment = VerticalAlignment.Center };
        check.SetResourceReference(Icon.ForegroundProperty, "Ok");
        current.Children.Add(check);
        current.Children.Add(UiKit.Text(L.T("overview.current"), "Small", new Thickness(5, 0, 0, 0)));
        return current;
    }

    /// <summary>Looks up the programs' latest releases unless that was done within <paramref name="maxAge"/>.</summary>
    async Task CheckVersionsAsync(TimeSpan maxAge)
    {
        if (_checking) return;
        _checking = true;
        RenderTools();
        try
        {
            await ToolUpdates.RefreshAsync(maxAge, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log.Debug(Net.Describe(ex));
        }
        finally
        {
            _checking = false;
            RenderTools();
        }
    }

    void OnUpdateAll(object sender, RoutedEventArgs e) => RunUpdates(_newer, L.T("jobs.toolUpdates"));

    /// <summary>Updates the programs one after the other; running ones are ended for the swap and started again.</summary>
    void RunUpdates(IEnumerable<ToolDef> tools, string title)
    {
        var steps = AppModel.Engine.ToolUpdateSteps(tools);
        if (steps.Count == 0) return;
        var job = AppModel.Run("tool-updates", "setup", title, steps, async j =>
        {
            await Dispatcher.InvokeAsync(() =>
            {
                Toasts.Show(j.State == JobState.Done ? ToastKind.Ok : ToastKind.Error, j.State == JobState.Done ? L.T("overview.updatesDone") : j.Status);
                RenderTools();
            });
        });
        if (job == null) return;
        UpdateJob.Follow(job.Id);
        RenderTools();
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

    async void OnRefresh(object sender, RoutedEventArgs e)
    {
        Refresh();
        await CheckVersionsAsync(TimeSpan.Zero);
        Toasts.Show(ToastKind.Ok, L.T("overview.refreshed"));
    }

    void OnSetup(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.Navigate("setup");

    void OnThemes(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.Navigate("themes");
}

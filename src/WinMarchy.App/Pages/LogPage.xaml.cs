using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WinMarchy.App.Ui;
using WinMarchy.Core;

namespace WinMarchy.App.Pages;

public partial class LogPage : UserControl, IPage
{
    readonly ObservableCollection<LogEntry> _shown = new();
    readonly DispatcherTimer _jobsTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    LogLevel _level = LogLevel.Info;
    string _search = "";
    bool _visible;

    public LogPage()
    {
        InitializeComponent();
        Subtitle.Text = L.T("log.subtitle", ("path", Paths.LogDir));
        Entries.ItemsSource = _shown;
        LevelInfo.IsChecked = true;
        Log.Written += entry => Dispatcher.InvokeAsync(() => Add(entry), DispatcherPriority.Background);
        AppModel.Jobs.Changed += _ => Dispatcher.InvokeAsync(() => { if (_visible) _jobsTimer.Start(); });
        _jobsTimer.Tick += (_, _) =>
        {
            _jobsTimer.Stop();
            RenderJobs();
        };
        IsVisibleChanged += (_, _) => _visible = IsVisible;
    }

    public void OnShown()
    {
        _visible = true;
        Reload();
        RenderJobs();
    }

    bool Matches(LogEntry e) =>
        e.Level >= _level && (_search.Length == 0 || e.Message.Contains(_search, StringComparison.CurrentCultureIgnoreCase));

    void Reload()
    {
        _shown.Clear();
        foreach (var e in Log.Recent(minLevel: _level).Where(Matches)) _shown.Add(e);
        Empty.Visibility = _shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ScrollToEnd();
    }

    void Add(LogEntry entry)
    {
        if (!Matches(entry)) return;
        _shown.Add(entry);
        while (_shown.Count > 5000) _shown.RemoveAt(0);
        Empty.Visibility = Visibility.Collapsed;
        if (_visible) ScrollToEnd();
    }

    void ScrollToEnd()
    {
        if (_shown.Count == 0) return;
        Dispatcher.InvokeAsync(() =>
        {
            if (FindScroller(Entries) is { } sv) sv.ScrollToEnd();
        }, DispatcherPriority.Background);
    }

    static ScrollViewer? FindScroller(DependencyObject d)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
            if (child is ScrollViewer sv) return sv;
            if (FindScroller(child) is { } found) return found;
        }
        return null;
    }

    void RenderJobs()
    {
        var jobs = AppModel.Jobs.List().OrderByDescending(j => j.Started).Take(8).ToList();
        JobsBlock.Visibility = jobs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Jobs.Children.Clear();
        bool first = true;
        foreach (var j in jobs)
        {
            var (label, kind) = j.State switch
            {
                JobState.Running => (L.T("jobs.state.running"), "accent"),
                JobState.Done => (L.T("jobs.state.done"), "ok"),
                JobState.Failed => (L.T("jobs.state.failed"), "error"),
                _ => (L.T("jobs.state.cancelled"), "muted"),
            };
            var row = new DockPanel();
            var show = new Button { Style = UiKit.S("SmallButton"), Content = L.T("log.showJob"), VerticalAlignment = VerticalAlignment.Center };
            var id = j.Id;
            show.Click += (_, _) => Job.Follow(id);
            DockPanel.SetDock(show, Dock.Right);
            row.Children.Add(show);
            var badge = UiKit.Badge(label, kind);
            badge.Margin = new Thickness(0, 0, 12, 0);
            badge.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(badge, Dock.Right);
            row.Children.Add(badge);
            var info = new StackPanel();
            info.Children.Add(UiKit.Text(j.Title, "H3"));
            info.Children.Add(UiKit.Text(j.Started.ToString("g", L.Culture) + (j.Status.Length > 0 ? " · " + j.Status : ""), "Small"));
            row.Children.Add(info);
            var border = new Border { Child = row, Padding = new Thickness(20, 12, 16, 12), BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0) };
            border.SetResourceReference(Border.BorderBrushProperty, "Border");
            Jobs.Children.Add(border);
            first = false;
        }
    }

    void OnLevel(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse<LogLevel>(tag, out var level))
        {
            _level = level;
            if (IsLoaded) Reload();
        }
    }

    void OnSearch(object sender, TextChangedEventArgs e)
    {
        _search = Search.Text.Trim();
        Reload();
    }

    void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, _shown.Select(x => x.ToString())));
            Toasts.Show(ToastKind.Ok, L.T("log.copied", ("count", _shown.Count)));
        }
        catch (Exception ex)
        {
            // The clipboard can be held by another program for a moment.
            Toasts.Show(ToastKind.Error, ex.Message);
        }
    }

    void OnOpenFolder(object sender, RoutedEventArgs e) => UiKit.OpenFolder(File.Exists(Log.CurrentFile) ? Log.CurrentFile : Paths.LogDir);
}

using System.Windows;
using System.Windows.Controls;
using WinMarchy.Core;

namespace WinMarchy.App.Ui;

/// <summary>
/// The steps of a running or finished job: status, title, detail, duration, progress – plus cancel
/// and retry from the failed step, as in Ember.
/// </summary>
public sealed class JobView : Border
{
    readonly StackPanel _steps = new();
    readonly TextBlock _title = new();
    readonly TextBlock _status = new();
    readonly ProgressBar _bar = new() { Margin = new Thickness(0, 10, 0, 14) };
    readonly Button _cancel = new();
    readonly Button _retry = new();
    string? _jobId;

    public JobView()
    {
        Style = (Style)Application.Current.FindResource("Card");
        Visibility = Visibility.Collapsed;
        _title.Style = (Style)Application.Current.FindResource("H2");
        _status.Style = (Style)Application.Current.FindResource("Small");
        _cancel.Content = L.T("jobs.cancel");
        _cancel.Style = (Style)Application.Current.FindResource("SmallButton");
        _retry.Content = L.T("jobs.retry");
        _retry.Style = (Style)Application.Current.FindResource("SmallButton");
        _retry.Margin = new Thickness(8, 0, 0, 0);
        _cancel.Click += (_, _) => { if (_jobId != null) AppModel.Jobs.Cancel(_jobId); };
        _retry.Click += (_, _) => { if (_jobId != null) AppModel.Jobs.Retry(_jobId); };

        var head = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        buttons.Children.Add(_cancel);
        buttons.Children.Add(_retry);
        DockPanel.SetDock(buttons, Dock.Right);
        head.Children.Add(buttons);
        var titles = new StackPanel();
        titles.Children.Add(_title);
        titles.Children.Add(_status);
        head.Children.Add(titles);

        var root = new StackPanel();
        root.Children.Add(head);
        root.Children.Add(_bar);
        root.Children.Add(_steps);
        Child = root;

        AppModel.Jobs.Changed += snapshot => Dispatcher.InvokeAsync(() =>
        {
            if (snapshot.Id == _jobId) Render(snapshot);
        });
    }

    public void Follow(string jobId)
    {
        _jobId = jobId;
        if (AppModel.Jobs.Get(jobId) is { } job) Render(job.Snapshot());
    }

    void Render(JobSnapshot s)
    {
        Visibility = Visibility.Visible;
        _title.Text = s.Title;
        _status.Text = s.Status;
        _bar.Value = s.Progress;
        _bar.IsIndeterminate = false;
        _bar.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource(s.State == JobState.Failed ? "Error" : "Accent");
        _cancel.Visibility = s.State == JobState.Running ? Visibility.Visible : Visibility.Collapsed;
        _retry.Visibility = s.State is JobState.Failed or JobState.Cancelled ? Visibility.Visible : Visibility.Collapsed;

        _steps.Children.Clear();
        foreach (var step in s.Steps) _steps.Children.Add(Row(step));
    }

    static FrameworkElement Row(StepSnapshot step)
    {
        var (kind, brush) = step.Status switch
        {
            StepStatus.Done => ("check", "Ok"),
            StepStatus.Failed => ("x", "Error"),
            StepStatus.Running => ("refresh", "AccentFg"),
            StepStatus.Skipped => ("dash", "TextMuted"),
            _ => ("circle", "TextMuted"),
        };
        var icon = new Icon { Kind = kind, Size = 14, Margin = new Thickness(0, 3, 10, 0), VerticalAlignment = VerticalAlignment.Top };
        icon.SetResourceReference(Icon.ForegroundProperty, brush);

        var duration = new TextBlock { Text = step.Duration, Style = (Style)Application.Current.FindResource("Small"), Margin = new Thickness(12, 1, 0, 0) };
        var text = new StackPanel();
        var title = new TextBlock { Text = step.Title, Style = (Style)Application.Current.FindResource("Body") };
        if (step.Status == StepStatus.Pending) title.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        text.Children.Add(title);
        if (step.Detail.Length > 0)
        {
            var detail = new TextBlock { Text = step.Detail, Style = (Style)Application.Current.FindResource("Small") };
            if (step.Status == StepStatus.Failed) detail.SetResourceReference(TextBlock.ForegroundProperty, "Error");
            text.Children.Add(detail);
        }
        if (step.ProgressVisible)
            text.Children.Add(new ProgressBar { Margin = new Thickness(0, 6, 0, 2), Value = step.Progress ?? 0, IsIndeterminate = step.Progress == null, Maximum = 100 });

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(duration, Dock.Right);
        row.Children.Add(icon);
        row.Children.Add(duration);
        row.Children.Add(text);
        return row;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WinMarchy.Core;

namespace WinMarchy.App.Ui;

/// <summary>
/// One installation at a time. Setup, extras, program updates and the GlazeWM build all run as jobs
/// of the kind "setup"; while one runs, every button that would start another is disabled (with a
/// tooltip that says why), and <see cref="InstallProgress"/> shows how far it is.
/// </summary>
public static class InstallLock
{
    /// <summary>The job kind every installation runs as.</summary>
    public const string Kind = "setup";

    static bool _busy;
    static bool _following;

    /// <summary>Whether an installation runs now.</summary>
    public static bool Busy => AppModel.Jobs.IsRunning(Kind);

    /// <summary>On the UI thread, whenever an installation starts or ends.</summary>
    public static event Action? Changed;

    /// <summary>The running installation, if any.</summary>
    public static JobSnapshot? Running =>
        AppModel.Jobs.List().LastOrDefault(j => j.Kind == Kind && j.State == JobState.Running);

    static void Follow()
    {
        if (_following) return;
        _following = true;
        AppModel.Jobs.Changed += snapshot =>
        {
            if (snapshot.Kind != Kind) return;
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                bool busy = Busy;
                if (busy == _busy) return;
                _busy = busy;
                Changed?.Invoke();
            });
        };
    }

    /// <summary>
    /// Keeps the button disabled while an installation runs; otherwise <paramref name="allowed"/>
    /// decides (null: always enabled). Returns the check, for when <paramref name="allowed"/> changes.
    /// </summary>
    public static Action Bind(ButtonBase button, Func<bool>? allowed = null)
    {
        Follow();
        var hint = button.ToolTip;
        ToolTipService.SetShowOnDisabled(button, true);
        void Apply()
        {
            bool busy = Busy;
            button.IsEnabled = !busy && (allowed?.Invoke() ?? true);
            button.ToolTip = busy ? L.T("jobs.installRunning") : hint;
        }
        Changed += Apply;
        Apply();
        return Apply;
    }

    /// <summary>Calls <paramref name="refresh"/> whenever an installation starts or ends (pages that draw their buttons themselves).</summary>
    public static void OnChange(Action refresh)
    {
        Follow();
        Changed += refresh;
    }
}

/// <summary>
/// A slim line under install buttons: the running installation's title, its current step and a
/// progress bar that moves with the download. Hidden while nothing is installed.
/// </summary>
public sealed class InstallProgress : StackPanel
{
    readonly TextBlock _title = new();
    readonly TextBlock _step = new();
    readonly ProgressBar _bar = new() { Maximum = 100, Height = 6, Margin = new Thickness(0, 6, 0, 0) };

    public InstallProgress()
    {
        Visibility = Visibility.Collapsed;
        Margin = new Thickness(0, 0, 0, 14);
        _title.Style = (Style)Application.Current.FindResource("Body");
        _step.Style = (Style)Application.Current.FindResource("Small");
        _step.TextWrapping = TextWrapping.Wrap;
        Children.Add(_title);
        Children.Add(_step);
        Children.Add(_bar);
        AppModel.Jobs.Changed += snapshot =>
        {
            if (snapshot.Kind == InstallLock.Kind) Dispatcher.InvokeAsync(() => Render(snapshot));
        };
        Loaded += (_, _) => { if (InstallLock.Running is { } running) Render(running); };
    }

    void Render(JobSnapshot s)
    {
        if (s.State != JobState.Running)
        {
            // Another installation may have started meanwhile; only the last one counts.
            if (InstallLock.Running is { } other && other.Id != s.Id) Render(other);
            else Visibility = Visibility.Collapsed;
            return;
        }
        Visibility = Visibility.Visible;
        _title.Text = s.Title;
        var current = s.Steps.FirstOrDefault(step => step.Status == StepStatus.Running);
        _step.Text = current == null ? s.Status
            : current.Detail.Length > 0 ? current.Title + " · " + current.Detail
            : current.Title;
        double percent = Percent(s);
        _bar.IsIndeterminate = current is { ProgressVisible: true, Progress: null };
        _bar.Value = percent;
    }

    /// <summary>Finished steps plus the share of the running one, so a download moves the bar too.</summary>
    internal static double Percent(JobSnapshot s)
    {
        if (s.Steps.Count == 0) return 0;
        double done = s.Steps.Count(step => step.Status is StepStatus.Done or StepStatus.Skipped);
        var running = s.Steps.FirstOrDefault(step => step.Status == StepStatus.Running);
        if (running is { Progress: { } p }) done += Math.Clamp(p, 0, 100) / 100;
        return 100 * done / s.Steps.Count;
    }
}

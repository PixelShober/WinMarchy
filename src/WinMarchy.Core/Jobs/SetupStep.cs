using System.Diagnostics;

namespace WinMarchy.Core;

public enum StepStatus { Pending, Running, Done, Failed, Skipped }

/// <summary>A single step that is visible in the UI (status, detail text, progress, duration).</summary>
public sealed class SetupStep
{
    readonly Func<SetupStep, CancellationToken, Task> _action;
    readonly Func<string?>? _skipReason;

    public SetupStep(string title, Func<SetupStep, CancellationToken, Task> action, Func<string?>? skipReason = null)
    {
        Title = title;
        _action = action;
        _skipReason = skipReason;
    }

    public string Title { get; }
    public StepStatus Status { get; private set; }
    public string Detail { get; private set; } = "";
    public string Duration { get; private set; } = "";
    /// <summary>0–100, or null for "indeterminate".</summary>
    public double? Progress { get; private set; }
    public bool ProgressVisible { get; private set; }

    /// <summary>Raised on every visible change; the job manager throttles it for the UI.</summary>
    public event Action? Changed;

    public void Reset()
    {
        Status = StepStatus.Pending;
        Detail = "";
        Duration = "";
        ProgressVisible = false;
        Progress = null;
        Changed?.Invoke();
    }

    /// <summary>Shows a text below the step (and optionally writes it to the log).</summary>
    public void Report(string text, bool log = true)
    {
        Detail = text;
        if (log) Log.Info("   " + text);
        Changed?.Invoke();
    }

    /// <summary>percent == null with visible == true: indeterminate progress.</summary>
    public void SetProgress(double? percent, bool visible = true)
    {
        ProgressVisible = visible;
        Progress = percent;
        Changed?.Invoke();
    }

    /// <summary>Runs the step. false = failed; OperationCanceledException = cancelled.</summary>
    public async Task<bool> RunAsync(CancellationToken ct)
    {
        Reset();
        var skip = _skipReason?.Invoke();
        if (skip != null)
        {
            Status = StepStatus.Skipped;
            Detail = skip;
            Log.Info($"⏭ {Title}: {L.T("jobs.skipped")} ({skip})");
            Changed?.Invoke();
            return true;
        }

        Status = StepStatus.Running;
        Changed?.Invoke();
        Log.Info($"▶ {Title}");
        var sw = Stopwatch.StartNew();
        try
        {
            await _action(this, ct);
            Status = StepStatus.Done;
            Log.Info($"✔ {Title} ({sw.Elapsed.TotalSeconds:0.0} s)");
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Status = StepStatus.Failed;
            Detail = L.T("jobs.cancelled");
            Log.Warn($"■ {Title}: {Detail}");
            throw;
        }
        catch (Exception ex)
        {
            Status = StepStatus.Failed;
            Detail = Net.Describe(ex);
            Log.Error($"✖ {Title}: {Detail}");
            Log.Debug(ex.ToString());
            return false;
        }
        finally
        {
            ProgressVisible = false;
            Progress = null;
            var s = sw.Elapsed.TotalSeconds;
            Duration = s < 10 ? $"{s.ToString("0.0", L.Culture)} s" : $"{s:0} s";
            Changed?.Invoke();
        }
    }
}

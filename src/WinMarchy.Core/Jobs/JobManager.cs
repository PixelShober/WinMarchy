using System.Collections.Concurrent;

namespace WinMarchy.Core;

public enum JobState { Running, Done, Failed, Cancelled }

public sealed record StepSnapshot(string Title, StepStatus Status, string Detail, string Duration, double? Progress, bool ProgressVisible);

public sealed record JobSnapshot(
    string Id, string Kind, string Title, JobState State, int FailedIndex, double Progress,
    string Status, DateTime Started, IReadOnlyList<StepSnapshot> Steps);

/// <summary>A sequence of steps that runs in the background (setup, theme, downloads, …).</summary>
public sealed class Job
{
    internal Job(string id, string kind, string title, List<SetupStep> steps)
    {
        Id = id;
        Kind = kind;
        Title = title;
        Steps = steps;
    }

    public string Id { get; }
    public string Kind { get; }
    public string Title { get; }
    public List<SetupStep> Steps { get; }
    public JobState State { get; internal set; } = JobState.Running;
    public int FailedIndex { get; internal set; } = -1;
    public string Status { get; internal set; } = "";
    public DateTime Started { get; } = DateTime.Now;
    internal CancellationTokenSource Cts { get; set; } = new();
    internal Func<Job, Task>? OnFinished { get; init; }
    internal int EmitPending;

    public JobSnapshot Snapshot() => new(
        Id, Kind, Title, State, FailedIndex,
        Steps.Count == 0 ? 0 : 100.0 * Steps.Count(s => s.Status is StepStatus.Done or StepStatus.Skipped) / Steps.Count,
        Status, Started,
        Steps.Select(s => new StepSnapshot(s.Title, s.Status, s.Detail, s.Duration, s.Progress, s.ProgressVisible)).ToList());
}

/// <summary>Starts, cancels and retries jobs and reports their state (throttled) to the UI.</summary>
public sealed class JobManager
{
    readonly ConcurrentDictionary<string, Job> _jobs = new();

    public event Action<JobSnapshot>? Changed;

    public IReadOnlyList<JobSnapshot> List() => _jobs.Values.OrderBy(j => j.Started).Select(j => j.Snapshot()).ToList();

    public Job? Get(string id) => _jobs.TryGetValue(id, out var job) ? job : null;

    public bool IsRunning(string kind) => _jobs.Values.Any(j => j.Kind == kind && j.State == JobState.Running);

    public bool AnyRunning => _jobs.Values.Any(j => j.State == JobState.Running);

    /// <summary>Starts a job. A job with the same id must not be running.</summary>
    public Job Start(string id, string kind, string title, List<SetupStep> steps, Func<Job, Task>? onFinished = null)
    {
        if (_jobs.TryGetValue(id, out var existing) && existing.State == JobState.Running)
            throw new WinMarchyException("jobRunning", ("title", existing.Title));

        var job = new Job(id, kind, title, steps) { OnFinished = onFinished };
        foreach (var step in steps) step.Changed += () => Emit(job, immediate: false);
        _jobs[id] = job;
        // Off the caller's thread: a theme change must not hold up the window, the menus or the
        // taskbar watch while it writes files and waits for programs.
        _ = Task.Run(() => RunAsync(job, 0));
        return job;
    }

    public void Cancel(string id) => Get(id)?.Cts.Cancel();

    public void Retry(string id)
    {
        var job = Get(id) ?? throw new WinMarchyException("jobUnknown");
        if (job.State == JobState.Running || job.FailedIndex < 0) return;
        job.Cts.Dispose();
        job.Cts = new CancellationTokenSource();
        var from = job.FailedIndex;
        job.State = JobState.Running;
        _ = Task.Run(() => RunAsync(job, from));
    }

    public void Remove(string id)
    {
        if (_jobs.TryGetValue(id, out var job) && job.State != JobState.Running) _jobs.TryRemove(id, out _);
    }

    async Task RunAsync(Job job, int startIndex)
    {
        for (int i = startIndex; i < job.Steps.Count; i++) job.Steps[i].Reset();
        job.State = JobState.Running;
        job.FailedIndex = -1;
        Log.Info(startIndex == 0
            ? $"===== {job.Title}: {L.T("jobs.started")} ====="
            : $"===== {job.Title}: {L.T("jobs.retryFrom", ("step", job.Steps[startIndex].Title))} =====");
        Emit(job, immediate: true);

        int index = startIndex;
        try
        {
            for (; index < job.Steps.Count; index++)
            {
                job.Status = job.Steps[index].Title + " …";
                if (!await job.Steps[index].RunAsync(job.Cts.Token))
                {
                    job.FailedIndex = index;
                    job.State = JobState.Failed;
                    job.Status = L.T("jobs.failedAt", ("step", job.Steps[index].Title));
                    return;
                }
            }
            job.State = JobState.Done;
            job.Status = L.T("jobs.done", ("title", job.Title));
            Log.Info($"===== {job.Title}: {L.T("jobs.finished")} =====");
        }
        catch (OperationCanceledException)
        {
            job.FailedIndex = index;
            job.State = JobState.Cancelled;
            job.Status = L.T("jobs.cancelled");
        }
        catch (Exception ex)
        {
            job.FailedIndex = index;
            job.State = JobState.Failed;
            job.Status = Net.Describe(ex);
            Log.Error(job.Status);
        }
        finally
        {
            if (job.OnFinished != null)
            {
                try { await job.OnFinished(job); }
                catch (Exception ex) { Log.Debug("job finish handler: " + ex.Message); }
            }
            Emit(job, immediate: true);
        }
    }

    void Emit(Job job, bool immediate)
    {
        if (immediate)
        {
            Changed?.Invoke(job.Snapshot());
            return;
        }
        // Progress updates arrive many times per second; the UI needs at most ~8 per second.
        if (Interlocked.Exchange(ref job.EmitPending, 1) == 1) return;
        _ = Task.Delay(120).ContinueWith(_ =>
        {
            Interlocked.Exchange(ref job.EmitPending, 0);
            Changed?.Invoke(job.Snapshot());
        }, TaskScheduler.Default);
    }
}

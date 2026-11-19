namespace WinMarchy.App.Startup;

/// <summary>
/// One WinMarchy per Windows session. The first one owns a named mutex; later starts hand their job
/// to it through the agent window (see <see cref="AgentClient"/>).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    const string MutexName = @"Local\WinMarchy.SingleInstance";

    readonly Mutex _mutex;

    SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>null when another instance owns it. After an update the old one may still be closing, so wait a moment.</summary>
    public static SingleInstance? Acquire(int waitSeconds)
    {
        var mutex = new Mutex(false, MutexName);
        bool owned;
        try
        {
            owned = mutex.WaitOne(TimeSpan.FromSeconds(waitSeconds));
        }
        catch (AbandonedMutexException)
        {
            owned = true;   // the previous instance crashed
        }
        if (owned) return new SingleInstance(mutex);
        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        try { _mutex.ReleaseMutex(); } catch { }
        _mutex.Dispose();
    }
}

using System.Text;

namespace WinMarchy.Core;

public enum LogLevel { Debug, Info, Warning, Error }

public sealed record LogEntry(DateTime Time, LogLevel Level, string Source, string Message)
{
    public override string ToString() => $"{Time:yyyy-MM-dd HH:mm:ss.fff} [{Level}] {Source}: {Message}";
}

/// <summary>
/// Central log. Every entry goes to the UI (via <see cref="Written"/>) and to
/// C:\WinMarchy\Logs\winmarchy-YYYY-MM-DD.log (UTF-8 with BOM, so Notepad shows umlauts correctly).
/// </summary>
public static class Log
{
    const int MaxEntries = 5000;
    static readonly object Sync = new();
    static readonly Queue<LogEntry> Buffer = new();
    static readonly UTF8Encoding Utf8NoBom = new(false);

    public static event Action<LogEntry>? Written;

    public static string CurrentFile => Path.Combine(Paths.LogDir, $"winmarchy-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Debug(string message, string source = "winmarchy") => Write(LogLevel.Debug, message, source);
    public static void Info(string message, string source = "winmarchy") => Write(LogLevel.Info, message, source);
    public static void Warn(string message, string source = "winmarchy") => Write(LogLevel.Warning, message, source);
    public static void Error(string message, string source = "winmarchy") => Write(LogLevel.Error, message, source);

    public static void Write(LogLevel level, string message, string source = "winmarchy")
    {
        var entry = new LogEntry(DateTime.Now, level, source, message);
        lock (Sync)
        {
            Buffer.Enqueue(entry);
            while (Buffer.Count > MaxEntries) Buffer.Dequeue();
            try
            {
                var file = CurrentFile;
                if (!File.Exists(file))
                {
                    Directory.CreateDirectory(Paths.LogDir);
                    File.WriteAllBytes(file, Encoding.UTF8.GetPreamble());
                }
                File.AppendAllText(file, entry + Environment.NewLine, Utf8NoBom);
            }
            catch
            {
                // The log file is a bonus; the UI log keeps working without it.
            }
        }
        Written?.Invoke(entry);
    }

    public static IReadOnlyList<LogEntry> Recent(int max = MaxEntries, LogLevel minLevel = LogLevel.Debug)
    {
        lock (Sync) return Buffer.Where(e => e.Level >= minLevel).TakeLast(max).ToList();
    }

    public static void Clear()
    {
        lock (Sync) Buffer.Clear();
    }

    /// <summary>Removes log files older than <paramref name="days"/> days.</summary>
    public static void Cleanup(int days = 30)
    {
        try
        {
            if (!Directory.Exists(Paths.LogDir)) return;
            foreach (var file in Directory.EnumerateFiles(Paths.LogDir, "winmarchy-*.log"))
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-days)) File.Delete(file);
        }
        catch
        {
            // not important
        }
    }
}

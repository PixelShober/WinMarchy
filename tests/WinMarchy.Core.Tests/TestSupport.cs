namespace WinMarchy.Core.Tests;

/// <summary>A fresh folder per test, deleted afterwards.</summary>
sealed class TempDir : IDisposable
{
    public TempDir() => Directory.CreateDirectory(Path);

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "winmarchy-tests", Guid.NewGuid().ToString("N"));

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { }
    }
}

/// <summary>Tests that switch the UI language run alone; the language is global.</summary>
[CollectionDefinition("Language", DisableParallelization = true)]
public sealed class LanguageCollection;

/// <summary>The repository root, for tests that look at source files.</summary>
static class Repo
{
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "WinMarchy.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("WinMarchy.sln not found above " + AppContext.BaseDirectory);
        }
    }
}

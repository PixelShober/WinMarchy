using System.IO.Compression;
using System.Text;

namespace WinMarchy.Core;

/// <summary>Zip extraction, atomic writes and shortcut files.</summary>
public static class FileSafety
{
    static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>Extracts a zip, refusing entries that would land outside the target folder.</summary>
    public static int ExtractZip(string zip, string dir, CancellationToken ct)
    {
        Directory.CreateDirectory(dir);
        var root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        int count = 0;
        using var archive = ZipFile.OpenRead(zip);
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var dest = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new WinMarchyException("zipInvalidPath", ("entry", entry.FullName));
            if (entry.Name.Length == 0) { Directory.CreateDirectory(dest); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
            count++;
        }
        return count;
    }

    /// <summary>Safety net: never delete a drive root or a profile/system folder.</summary>
    public static bool IsDangerousDirectory(string dir)
    {
        var full = Path.GetFullPath(dir).TrimEnd('\\', '/');
        var forbidden = new[]
        {
            Path.GetPathRoot(full),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Paths.InstallRoot,
            Paths.Root,
        };
        return forbidden.Any(f => !string.IsNullOrEmpty(f) &&
                                  string.Equals(Path.GetFullPath(f).TrimEnd('\\', '/'), full, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Writes text as UTF-8 without BOM through a temporary file and a rename, so a crash never leaves
    /// half a config file behind.
    /// </summary>
    public static void WriteText(string path, string text, bool bom = false)
    {
        var encoding = bom ? new UTF8Encoding(true) : Utf8NoBom;
        WriteBytes(path, [.. encoding.GetPreamble(), .. encoding.GetBytes(text)]);
    }

    /// <summary>
    /// Through a temporary file of its own (two writers never share one) and a rename. A virus scanner,
    /// the search indexer or a program reading the file can hold it for a moment; that is waited out
    /// a few times before the error goes up.
    /// </summary>
    public static void WriteBytes(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = $"{path}.{Guid.NewGuid():N}.winmarchy-tmp";
        try
        {
            File.WriteAllBytes(tmp, data);
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
                        File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
                    File.Move(tmp, path, overwrite: true);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 5)
                {
                    Thread.Sleep(80 * attempt);
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException($"{ex.Message} ({path})", ex);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    /// <summary>Reads a file another program may have open (Flow Launcher, Terminal).</summary>
    public static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>Creates a .lnk through WScript.Shell (late-bound, no interop assembly).</summary>
    public static void CreateShortcut(string link, string target, string arguments, string workingDir, string description)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(link);
        shortcut.TargetPath = target;
        shortcut.Arguments = arguments;
        shortcut.WorkingDirectory = workingDir;
        shortcut.Description = description;
        shortcut.Save();
    }

    /// <summary>Target and arguments of a .lnk, or null if it cannot be read.</summary>
    public static (string Target, string Arguments)? ReadShortcut(string link)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(link);
            return ((string)shortcut.TargetPath, (string)shortcut.Arguments);
        }
        catch
        {
            return null;
        }
    }
}

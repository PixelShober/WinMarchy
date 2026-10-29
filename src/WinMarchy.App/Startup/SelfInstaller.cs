using System.Diagnostics;
using System.Security.Cryptography;
using WinMarchy.Core;

namespace WinMarchy.App.Startup;

/// <summary>
/// Copies WinMarchy to C:\WinMarchy\App on start and restarts it from there, as Ember does. The program
/// then lives at one fixed path: GlazeWM, the bar, AutoHotkey and the autostart entry all point to it,
/// and IT can allow exactly that path. Test builds from pull requests do the same, so what the user
/// starts is what runs everywhere. A WinMarchy that is already running in another version is asked to
/// quit (or ended) first, because Windows does not let anyone overwrite a running exe.
/// </summary>
public static class SelfInstaller
{
    public enum Result { RunHere, Relaunched }

    /// <param name="copy">false for actions and the start after an update: only find out where WinMarchy runs.</param>
    public static Result EnsureInstalled(string[] args, bool copy)
    {
        var currentDir = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\');
        if (currentDir.Equals(Paths.AppDir, StringComparison.OrdinalIgnoreCase))
        {
            AppInfo.Mode = "installed";
            return Result.RunHere;
        }
#if DEBUG
        AppInfo.Mode = "dev";
        Log.Debug("Debug build: no copy to " + Paths.AppDir);
        return Result.RunHere;
#else
        AppInfo.Mode = "portable";
        if (!copy) return Result.RunHere;
        if (args.Contains(StartArgs.PortableSwitch, StringComparer.OrdinalIgnoreCase))
        {
            Log.Info(L.T("install.portable", ("path", Paths.AppDir)));
            return Result.RunHere;
        }

        // Only the single-file release (one WinMarchy.exe) is copied; a multi-file build would be incomplete.
        var exe = Environment.ProcessPath;
        if (exe == null || File.Exists(Path.Combine(currentDir, "WinMarchy.dll")))
        {
            Log.Info(L.T("install.notSingleFile"));
            return Result.RunHere;
        }

        try
        {
            // An old release in the downloads must not replace one that has updated itself since. A test
            // build always wins: the user starts it to try exactly that.
            var installedVersion = ExeVersion(Paths.InstalledExe);
            if (!AppInfo.IsPrerelease && installedVersion != null && AppInfo.CompareVersions(installedVersion, AppInfo.Version) > 0)
            {
                Log.Info(L.T("install.newerInstalled", ("version", installedVersion)));
                StopOthers(Paths.InstalledExe);
                return Launch(args);
            }
            StopOthers(exe);
            if (!SameFile(exe, Paths.InstalledExe))
            {
                Directory.CreateDirectory(Paths.AppDir);
                CopyWhenFree(exe, Paths.InstalledExe);
                Log.Info(L.T("install.copied", ("version", AppInfo.Version), ("path", Paths.AppDir)));
                CreateStartMenuShortcut();
            }
            return Launch(args);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Warn(L.T("install.noRights", ("path", Paths.AppDir), ("detail", ex.Message)));
        }
        catch (IOException ex)
        {
            // The old WinMarchy did not let go of the file.
            Log.Warn(L.T("install.locked", ("path", Paths.AppDir), ("detail", ex.Message)));
        }
        catch (Exception ex)
        {
            Log.Warn(L.T("install.failed", ("detail", Net.Describe(ex))));
        }
        return Result.RunHere;
#endif
    }

    /// <summary>
    /// Ends every other running WinMarchy that is not the file <paramref name="keep"/>: first politely
    /// (it shuts down like from the tray, taskbar and all), by force if it does not answer or is not
    /// gone after a few seconds. Older versions do not know the polite message and are ended.
    /// </summary>
    static void StopOthers(string keep)
    {
        foreach (var p in Others())
        {
            using (p)
            {
                string? path = null;
                try { path = p.MainModule?.FileName; } catch { }
                if (path != null && SameFile(keep, path)) continue;
                var version = path != null ? ExeVersion(path) : null;
                try
                {
                    // Asked, it shuts down like from the tray; an older version does not answer and is ended.
                    if (!AgentClient.AskToQuit(p.Id) || !p.WaitForExit(8000))
                    {
                        if (!p.HasExited) p.Kill();
                        p.WaitForExit(3000);
                    }
                    Log.Info(L.T("install.stoppedOther", ("version", version ?? "?"), ("path", path ?? "?")));
                }
                catch (Exception ex)
                {
                    Log.Warn(L.T("install.stopFailed", ("version", version ?? "?"), ("detail", Net.Describe(ex))));
                }
            }
        }
    }

    /// <summary>Other WinMarchy processes: by name, and whoever owns the agent window (a renamed exe).</summary>
    static List<Process> Others()
    {
        var me = Environment.ProcessId;
        var list = Process.GetProcessesByName("WinMarchy").Where(p => p.Id != me).ToList();
        if (AgentClient.AgentProcessId() is { } pid && pid != me && list.All(p => p.Id != pid))
        {
            try { list.Add(Process.GetProcessById(pid)); } catch { }
        }
        return list;
    }

    /// <summary>A file of a process that just ended stays locked for a moment.</summary>
    static void CopyWhenFree(string from, string to)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(from, to, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(250);
            }
        }
    }

    /// <summary>"1.4.0" from the file's product version, without the "+commit" part.</summary>
    public static string? ExeVersion(string path)
    {
        if (!File.Exists(path)) return null;
        var info = FileVersionInfo.GetVersionInfo(path);
        var v = info.ProductVersion ?? info.FileVersion;
        if (v == null) return null;
        var plus = v.IndexOf('+');
        return plus > 0 ? v[..plus] : v;
    }

    static bool SameFile(string a, string b)
    {
        if (!File.Exists(b)) return false;
        if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
        using var fa = File.OpenRead(a);
        using var fb = File.OpenRead(b);
        return SHA256.HashData(fa).AsSpan().SequenceEqual(SHA256.HashData(fb));
    }

    static Result Launch(string[] args)
    {
        var psi = new ProcessStartInfo(Paths.InstalledExe) { UseShellExecute = false, WorkingDirectory = Paths.AppDir };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process.Start(psi)?.Dispose();
        return Result.Relaunched;
    }

    /// <summary>Start menu entry "WinMarchy" (per user, no admin rights needed).</summary>
    public static void CreateStartMenuShortcut()
    {
        try
        {
            var link = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "WinMarchy.lnk");
            FileSafety.CreateShortcut(link, Paths.InstalledExe, "", Paths.AppDir, L.T("app.shortcutDescription"));
            Log.Info(L.T("install.shortcut", ("path", link)));
        }
        catch (Exception ex)
        {
            // e.g. when Windows Script Host is disabled by policy
            Log.Warn(L.T("install.shortcutFailed", ("detail", ex.Message)));
        }
    }
}

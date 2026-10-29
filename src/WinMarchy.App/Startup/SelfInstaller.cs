using System.Diagnostics;
using System.Security.Cryptography;
using WinMarchy.Core;

namespace WinMarchy.App.Startup;

/// <summary>
/// Copies WinMarchy to C:\WinMarchy\App on start and restarts it from there, as Ember does. The program
/// then lives at one fixed path: GlazeWM, the bar, AutoHotkey and the autostart entry all point to it,
/// and IT can allow exactly that path.
/// </summary>
public static class SelfInstaller
{
    public enum Result { RunHere, Relaunched }

    public static Result EnsureInstalled(string[] args)
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
        if (AppInfo.IsPrerelease)
        {
            Log.Info(L.T("install.prerelease", ("version", AppInfo.Version)));
            return Result.RunHere;
        }
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
            var installedVersion = ExeVersion(Paths.InstalledExe);
            if (installedVersion != null && AppInfo.CompareVersions(installedVersion, AppInfo.Version) > 0)
            {
                Log.Info(L.T("install.newerInstalled", ("version", installedVersion)));
                return Launch(args);
            }
            if (!SameFile(exe, Paths.InstalledExe))
            {
                Directory.CreateDirectory(Paths.AppDir);
                File.Copy(exe, Paths.InstalledExe, overwrite: true);
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
            // Typically: WinMarchy already runs from C:\WinMarchy\App and the file is locked.
            Log.Warn(L.T("install.locked", ("path", Paths.AppDir), ("detail", ex.Message)));
        }
        catch (Exception ex)
        {
            Log.Warn(L.T("install.failed", ("detail", Net.Describe(ex))));
        }
        return Result.RunHere;
#endif
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

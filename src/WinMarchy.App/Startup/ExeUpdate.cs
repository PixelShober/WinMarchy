using System.Diagnostics;
using WinMarchy.Core;
using WinMarchy.Core.Updates;

namespace WinMarchy.App.Startup;

/// <summary>
/// Replaces the running exe. Windows does not let a running exe be deleted, but it can be renamed:
/// WinMarchy.exe → WinMarchy.exe.old, WinMarchy.exe.new → WinMarchy.exe, start the new one.
/// The .new file only exists after its SHA-256 matched latest.json (see <see cref="Updater"/>).
/// </summary>
public static class ExeUpdate
{
    static string OldPath => Paths.InstalledExe + ".old";

    /// <summary>A downloaded update from an earlier session that is newer than this exe.</summary>
    public static bool Pending()
    {
        if (AppInfo.Mode != "installed" || !File.Exists(Updater.ExeNewPath)) return false;
        var next = SelfInstaller.ExeVersion(Updater.ExeNewPath);
        if (next != null && AppInfo.CompareVersions(next, AppInfo.Version) > 0) return true;
        // Older or unreadable (WinMarchy was updated another way meanwhile): not worth keeping.
        try { File.Delete(Updater.ExeNewPath); } catch { }
        return false;
    }

    public static void Apply(IEnumerable<string> args)
    {
        var exe = Paths.InstalledExe;
        var next = Updater.ExeNewPath;
        if (AppInfo.Mode != "installed" || !File.Exists(next)) throw new WinMarchyException("exeUpdateMissing");

        try { if (File.Exists(OldPath)) File.Delete(OldPath); } catch { }
        File.Move(exe, OldPath);
        try
        {
            File.Move(next, exe);
        }
        catch
        {
            File.Move(OldPath, exe);   // roll back
            throw;
        }
        Log.Info(L.T("update.restarting"));
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Paths.AppDir };
        psi.ArgumentList.Add(StartArgs.AfterUpdateSwitch);
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process.Start(psi)?.Dispose();
    }

    /// <summary>Deletes WinMarchy.exe.old once the old process is gone.</summary>
    public static void CleanupOld()
    {
        for (int i = 0; i < 10 && File.Exists(OldPath); i++)
        {
            try
            {
                File.Delete(OldPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
        }
    }
}

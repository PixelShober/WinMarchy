using System.Diagnostics;

namespace WinMarchy.Core.Tools;

/// <summary>
/// Installs a program straight from its vendor's GitHub release: the asset is checked against the
/// SHA-256 that GitHub publishes for it, and without a published checksum nothing is installed.
/// Everything lands in C:\WinMarchy\Tools and runs with user rights.
/// </summary>
public static class ToolInstaller
{
    /// <param name="release">The release to install, if the caller looked it up already (an update).</param>
    public static async Task<string> InstallAsync(ToolDef tool, SetupStep step, CancellationToken ct, ReleaseInfo? release = null)
    {
        if (tool.Kind == InstallKind.None || tool.Repo == null || tool.Asset == null)
            throw new WinMarchyException("toolNotInstallable", ("tool", tool.Name));

        using var http = Net.CreateClient(TimeSpan.FromMinutes(15));
        if (release == null)
        {
            step.Report(L.T("tools.lookup", ("tool", tool.Name)));
            release = tool.PreRelease ? await Net.GetNewestReleaseAsync(http, tool.Repo, ct) : await Net.GetLatestReleaseAsync(http, tool.Repo, ct);
        }
        var asset = release.Assets.Keys.FirstOrDefault(n => tool.Asset.IsMatch(n))
            ?? throw new WinMarchyException("toolAssetMissing", ("tool", tool.Name), ("tag", release.Tag));
        if (!release.Sha256.TryGetValue(asset, out var sha))
            throw new WinMarchyException("toolNoChecksum", ("tool", tool.Name), ("file", asset));

        var file = Path.Combine(Paths.Ensure(Paths.DownloadDir), asset);
        step.Report(L.T("tools.downloading", ("tool", tool.Name), ("version", release.Tag)));
        await Net.DownloadVerifiedAsync(http, release.Assets[asset], file, sha,
            (done, total) => step.SetProgress(total is > 0 ? 100.0 * done / total.Value : null), ct);
        step.SetProgress(null, visible: false);
        // The check belongs in the log, not on screen.
        Log.Info(L.T("tools.verified", ("file", asset)));
        if (OperatingSystem.IsWindows() && Authenticode.Signer(file) is { } signer)
            Log.Info(L.T("tools.signedBy", ("file", asset), ("signer", signer)));

        // A running GlazeWM keeps its folder locked, and a second one (e.g. the official one when
        // switching builds) would fight the new one over the windows.
        if (tool.Id == ToolCatalog.GlazeWm.Id) await StopGlazeAsync(step, ct);

        string exe;
        switch (tool.Kind)
        {
            case InstallKind.Zip:
                exe = Extract(tool, file, FileSafety.ExtractZip, ct);
                break;
            case InstallKind.SevenZip:
                exe = Extract(tool, file, FileSafety.Extract7z, ct);
                break;
            case InstallKind.MsiExtract:
                exe = await ExtractMsiAsync(tool, file, ct);
                break;
            default:
                throw new WinMarchyException("toolNotInstallable", ("tool", tool.Name));
        }
        try { File.Delete(file); } catch { }
        var tag = release.Tag;
        AppState.Update(s =>
        {
            if (!s.InstalledTools.Contains(tool.Id)) s.InstalledTools.Add(tool.Id);
            s.ToolTags[tool.Id] = tag;
        });
        step.Report(L.T("tools.installed", ("tool", tool.Name), ("path", exe)));
        return exe;
    }

    /// <summary>
    /// Ends GlazeWM the way its tray menu does (wm-exit), so every window it had hidden on other
    /// workspaces shows again; whatever is still running after a few seconds is ended.
    /// </summary>
    static async Task StopGlazeAsync(SetupStep step, CancellationToken ct)
    {
        if (Process.GetProcessesByName("glazewm").Length == 0) return;
        step.Report(L.T("tools.glazeStopping"));
        try
        {
            await using var ipc = new GlazeWm.GlazeIpc();
            if (await ipc.ConnectAsync(ct)) await ipc.CommandAsync("wm-exit", ct);
        }
        catch (WinMarchyException ex)
        {
            Log.Debug("wm-exit: " + ex.Message);
        }
        foreach (var name in new[] { "glazewm", "glazewm-watcher" })
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    try
                    {
                        if (!p.WaitForExit(5000)) p.Kill();
                        p.WaitForExit(3000);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        Log.Debug($"{name}: {ex.Message}");
                    }
                }
            }
        }
    }

    /// <summary>Unpacks the archive next to the program's folder, then swaps it in.</summary>
    static string Extract(ToolDef tool, string archive, Func<string, string, CancellationToken, int> unpack, CancellationToken ct)
    {
        var staging = tool.ToolDir + ".new";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        unpack(archive, staging, ct);
        return Swap(tool, staging);
    }

    /// <summary>
    /// "msiexec /a" unpacks the files of an MSI into a folder without registering anything, which works
    /// with normal user rights. GlazeWM runs from there.
    /// </summary>
    static async Task<string> ExtractMsiAsync(ToolDef tool, string msi, CancellationToken ct)
    {
        var staging = tool.ToolDir + ".new";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        var log = Path.Combine(Paths.Ensure(Paths.LogDir), $"msiexec-{tool.Id}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var psi = new ProcessStartInfo(ProcessRunner.SystemExe("msiexec.exe"))
        {
            // msiexec parses its own command line: the property value keeps its quotes.
            Arguments = $"/a \"{msi}\" /qn TARGETDIR=\"{staging}\" /l*v \"{log}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new WinMarchyException("processStart", ("file", "msiexec.exe"));
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            cts.CancelAfter(TimeSpan.FromMinutes(5));
            await p.WaitForExitAsync(cts.Token);
        }
        if (p.ExitCode != 0) throw new WinMarchyException("msiExtractFailed", ("tool", tool.Name), ("code", p.ExitCode), ("log", log));
        return Swap(tool, staging);
    }

    /// <summary>Replaces C:\WinMarchy\Tools\&lt;id&gt; with the freshly unpacked folder.</summary>
    static string Swap(ToolDef tool, string staging)
    {
        var exe = ToolCatalog.FindIn(staging, tool.Exe) ?? throw new WinMarchyException("toolExeMissing", ("tool", tool.Name), ("exe", tool.Exe));
        var relative = Path.GetRelativePath(staging, exe);
        if (Directory.Exists(tool.ToolDir))
        {
            // The user's data inside the program's folder (a portable program's settings) moves into the new one.
            foreach (var keep in tool.KeepFolders ?? Array.Empty<string>())
            {
                var from = Path.Combine(tool.ToolDir, keep);
                var to = Path.Combine(staging, keep);
                if (!Directory.Exists(from)) continue;
                if (Directory.Exists(to)) Directory.Delete(to, true);
                Directory.Move(from, to);
            }
            var old = tool.ToolDir + ".old";
            if (Directory.Exists(old)) Directory.Delete(old, true);
            Directory.Move(tool.ToolDir, old);
            try { Directory.Delete(old, true); } catch { }   // a running copy may keep files locked until it exits
        }
        Directory.Move(staging, tool.ToolDir);
        return Path.Combine(tool.ToolDir, relative);
    }

    /// <summary>
    /// The vendor's latest release if it is not the one installed, else null. The installed one is
    /// known by the tag WinMarchy noted when it installed it; for older installs by the program's
    /// version.
    /// </summary>
    public static async Task<ReleaseInfo?> NewerReleaseAsync(ToolDef tool, string? installedVersion, CancellationToken ct)
    {
        if (await LatestReleaseAsync(tool, ct) is not { } release) return null;
        ToolUpdates.Remember(tool, release.Tag);
        return IsCurrent(release.Tag, AppState.Load().ToolTags.GetValueOrDefault(tool.Id), installedVersion) ? null : release;
    }

    /// <summary>
    /// The vendor's latest release (for a program on nightly builds: the newest one, pre-releases too),
    /// or null for a program WinMarchy does not download.
    /// </summary>
    public static async Task<ReleaseInfo?> LatestReleaseAsync(ToolDef tool, CancellationToken ct)
    {
        if (tool.Repo == null) return null;
        using var http = Net.CreateClient(TimeSpan.FromMinutes(2));
        return tool.PreRelease ? await Net.GetNewestReleaseAsync(http, tool.Repo, ct) : await Net.GetLatestReleaseAsync(http, tool.Repo, ct);
    }

    /// <summary>
    /// Whether the release <paramref name="latestTag"/> is the one installed: by the tag WinMarchy noted
    /// at the install, else by the program's version. Versions count as equal when only trailing zeros
    /// differ ("2.1.0.0" in the file, "2.1.0" as the tag).
    /// </summary>
    internal static bool IsCurrent(string latestTag, string? installedTag, string? installedVersion)
    {
        if (installedTag != null && SameVersion(installedTag, latestTag)) return true;
        return installedVersion != null && SameVersion(installedVersion, latestTag);
    }

    /// <summary>"v2.1.0" and "2.1.0.0" are the same; anything that is no plain version only as the same text.</summary>
    internal static bool SameVersion(string a, string b)
    {
        static string Plain(string v) => v.Trim().TrimStart('v', 'V');
        if (string.Equals(Plain(a), Plain(b), StringComparison.OrdinalIgnoreCase)) return true;
        if (!Version.TryParse(Plain(a), out var x) || !Version.TryParse(Plain(b), out var y)) return false;
        static int[] Parts(Version v) => new[] { v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision) };
        return Parts(x).SequenceEqual(Parts(y));
    }

    /// <summary>Deletes what <see cref="Remove"/> could not delete while it was in use.</summary>
    public static void CleanupLeftovers()
    {
        var left = AppState.Load().LeftoverDirs;
        if (left.Count == 0) return;
        var gone = new List<string>();
        foreach (var dir in left)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                gone.Add(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Debug($"{dir}: {ex.Message}");
            }
        }
        AppState.Update(s => s.LeftoverDirs.RemoveAll(gone.Contains));
    }

    /// <summary>Removes a program WinMarchy installed (only its own folder).</summary>
    public static void Remove(ToolDef tool, Locations loc) => Remove(tool.Id, tool.Name, tool.Exe);

    /// <summary>
    /// Removes a program WinMarchy installed and no longer offers (Pi): its process ends, its folder
    /// under C:\WinMarchy\Tools goes, and state.json forgets it. A copy installed by someone else
    /// stays. Returns whether there was anything to remove.
    /// </summary>
    public static bool RemoveRetired(string id, string name, string exe)
    {
        bool there = AppState.Load().InstalledTools.Contains(id) || Directory.Exists(Path.Combine(Paths.ToolsDir, id));
        if (there) Remove(id, name, exe);
        return there;
    }

    static void Remove(string id, string name, string exe)
    {
        var dir = Path.Combine(Paths.ToolsDir, id);
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
        {
            try
            {
                // Only its own copy: one installed elsewhere keeps running.
                if (p.MainModule?.FileName is { } file && file.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(3000);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            finally { p.Dispose(); }
        }
        if (Directory.Exists(dir))
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // ExplorerBlurMica's DLL stays loaded in Explorer until the next sign-in.
                Log.Info(L.T("tools.removeLater", ("tool", name), ("path", dir)));
                AppState.Update(s => { if (!s.LeftoverDirs.Contains(dir)) s.LeftoverDirs.Add(dir); });
            }
        }
        AppState.Update(s => s.InstalledTools.Remove(id));
    }
}

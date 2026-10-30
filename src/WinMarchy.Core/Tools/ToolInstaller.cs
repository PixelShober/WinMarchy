using System.Diagnostics;

namespace WinMarchy.Core.Tools;

/// <summary>
/// Installs a program straight from its vendor's GitHub release: the asset is checked against the
/// SHA-256 that GitHub publishes for it, and without a published checksum nothing is installed.
/// Everything lands in C:\WinMarchy\Tools and runs with user rights.
/// </summary>
public static class ToolInstaller
{
    public static async Task<string> InstallAsync(ToolDef tool, SetupStep step, CancellationToken ct)
    {
        if (tool.Kind == InstallKind.None || tool.Repo == null || tool.Asset == null)
            throw new WinMarchyException("toolNotInstallable", ("tool", tool.Name));

        using var http = Net.CreateClient(TimeSpan.FromMinutes(15));
        step.Report(L.T("tools.lookup", ("tool", tool.Name)));
        var release = await Net.GetLatestReleaseAsync(http, tool.Repo, ct);
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
                exe = ExtractZip(tool, file, ct);
                break;
            case InstallKind.MsiExtract:
                exe = await ExtractMsiAsync(tool, file, ct);
                break;
            default:
                exe = await RunInstallerAsync(tool, file, ct);
                break;
        }
        try { File.Delete(file); } catch { }
        AppState.Update(s => { if (!s.InstalledTools.Contains(tool.Id)) s.InstalledTools.Add(tool.Id); });
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

    static string ExtractZip(ToolDef tool, string zip, CancellationToken ct)
    {
        var staging = tool.ToolDir + ".new";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        FileSafety.ExtractZip(zip, staging, ct);
        return Swap(tool, staging);
    }

    /// <summary>
    /// "msiexec /a" unpacks the files of an MSI into a folder without registering anything, which works
    /// with normal user rights. GlazeWM and Zebar run from there.
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

    static async Task<string> RunInstallerAsync(ToolDef tool, string installer, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(installer, tool.InstallerArgs ?? "") { UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi) ?? throw new WinMarchyException("processStart", ("file", installer));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(10));
        await p.WaitForExitAsync(cts.Token);
        if (p.ExitCode != 0) throw new WinMarchyException("installerFailed", ("tool", tool.Name), ("code", p.ExitCode));
        // Squirrel installers start the app at the end; give it a moment to write its folder.
        for (int i = 0; i < 20; i++)
        {
            var found = ToolCatalog.Detect(tool, Locations.Current, UserSettings.Load(), AppState.Load()).Path;
            if (found != null) return found;
            await Task.Delay(500, ct);
        }
        throw new WinMarchyException("toolNotFoundAfterInstall", ("tool", tool.Name));
    }

    /// <summary>Replaces C:\WinMarchy\Tools\&lt;id&gt; with the freshly unpacked folder.</summary>
    static string Swap(ToolDef tool, string staging)
    {
        var exe = ToolCatalog.FindIn(staging, tool.Exe) ?? throw new WinMarchyException("toolExeMissing", ("tool", tool.Name), ("exe", tool.Exe));
        var relative = Path.GetRelativePath(staging, exe);
        if (Directory.Exists(tool.ToolDir))
        {
            var old = tool.ToolDir + ".old";
            if (Directory.Exists(old)) Directory.Delete(old, true);
            Directory.Move(tool.ToolDir, old);
            try { Directory.Delete(old, true); } catch { }   // a running copy may keep files locked until it exits
        }
        Directory.Move(staging, tool.ToolDir);
        return Path.Combine(tool.ToolDir, relative);
    }

    /// <summary>Removes a program WinMarchy installed (only its own folder or Flow's per-user install).</summary>
    public static void Remove(ToolDef tool, Locations loc)
    {
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(tool.Exe)))
        {
            try { p.Kill(entireProcessTree: true); p.WaitForExit(3000); } catch { }
        }
        if (Directory.Exists(tool.ToolDir)) Directory.Delete(tool.ToolDir, true);
        if (tool.Kind == InstallKind.Installer && tool.Id == "flow")
        {
            var update = Path.Combine(loc.LocalAppData, "FlowLauncher", "Update.exe");
            if (File.Exists(update)) Process.Start(new ProcessStartInfo(update, "--uninstall -s") { UseShellExecute = false })?.WaitForExit(60000);
        }
        AppState.Update(s => s.InstalledTools.Remove(tool.Id));
    }
}

using System.Diagnostics;
using System.Text;

namespace WinMarchy.Core;

/// <summary>How the output of a child process is decoded.</summary>
public enum OutputKind
{
    /// <summary>git, AutoHotkey, GlazeWM and other UTF-8 tools.</summary>
    Utf8,

    /// <summary>Classic console tools (powercfg, gpupdate, whoami, cmd).</summary>
    Oem,
}

public sealed record ProcResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;

    public string Output => (StdOut + Environment.NewLine + StdErr).Trim();

    /// <summary>Last non-empty line of stdout (e.g. for "npm config get").</summary>
    public string LastLine =>
        StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
}

public static class ProcessRunner
{
    public static Task<ProcResult> RunAsync(string file, IEnumerable<string> args, CancellationToken ct,
                                            int timeoutSec = 60, OutputKind output = OutputKind.Utf8,
                                            string? displayArgs = null, bool logOutput = true)
    {
        var psi = new ProcessStartInfo(file);
        foreach (var a in args) psi.ArgumentList.Add(a);
        return RunAsync(psi, ct, timeoutSec, output, displayArgs, logOutput);
    }

    /// <summary>
    /// For .cmd files like npm.cmd that must run through cmd.exe.
    /// Only pass ASCII arguments here: cmd.exe converts everything else through the console code page.
    /// </summary>
    public static Task<ProcResult> RunCmdAsync(string commandLine, CancellationToken ct, int timeoutSec = 120, bool logOutput = true)
    {
        var psi = new ProcessStartInfo(SystemExe("cmd.exe"))
        {
            Arguments = "/d /s /c \"" + commandLine + "\""
        };
        return RunAsync(psi, ct, timeoutSec, OutputKind.Utf8, null, logOutput);
    }

    /// <summary>Full path of a tool in System32, so PATH cannot redirect us to a different binary.</summary>
    public static string SystemExe(string name) => Path.Combine(Environment.SystemDirectory, name);

    static async Task<ProcResult> RunAsync(ProcessStartInfo psi, CancellationToken ct, int timeoutSec,
                                           OutputKind output, string? displayArgs, bool logOutput)
    {
        var encoding = output == OutputKind.Oem ? ConsoleEncoding.Oem : new UTF8Encoding(false);
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = encoding;
        psi.StandardErrorEncoding = encoding;

        var shownArgs = displayArgs ?? (psi.ArgumentList.Count > 0 ? string.Join(" ", psi.ArgumentList) : psi.Arguments);
        Log.Debug($"> {Path.GetFileName(psi.FileName)} {shownArgs}");

        using var process = Process.Start(psi)
            ?? throw new WinMarchyException("processStart", ("file", psi.FileName));
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            ct.ThrowIfCancellationRequested();
            throw new WinMarchyException("processTimeout", ("file", Path.GetFileName(psi.FileName)), ("seconds", timeoutSec));
        }

        var result = new ProcResult(process.ExitCode, (await stdout).Trim(), (await stderr).Trim());
        if (logOutput && result.Output.Length > 0) Log.Debug(result.Output);
        Log.Debug($"  exit code {process.ExitCode}");
        return result;
    }
}

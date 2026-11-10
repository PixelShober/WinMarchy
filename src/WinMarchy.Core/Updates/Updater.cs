using System.Text.Json;

namespace WinMarchy.Core.Updates;

/// <summary>latest.json, attached to every GitHub release.</summary>
public sealed class UpdateManifest
{
    public string Version { get; set; } = "0.0.0";
    public DateTime? Published { get; set; }
    public ExeAsset Exe { get; set; } = new();
    public string? Notes { get; set; }
}

public sealed class ExeAsset
{
    public string Name { get; set; } = "WinMarchy.exe";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
}

public sealed record UpdateStatus(string Current, string? Latest, bool Available, bool Ready, bool Checking, DateTime? LastCheck, string? Error, string? Notes);

/// <summary>
/// Checks GitHub for a new release and downloads it in the background. The exe is only replaced
/// after the SHA-256 from latest.json matches; the switch itself happens on the next start
/// (see ExeUpdate in the app). Test builds (version with "-") never update themselves.
/// </summary>
public sealed class Updater
{
    int _checking;

    public UpdateStatus Status { get; private set; } = new(AppInfo.Version, null, false, false, false, null, null, null);

    public event Action<UpdateStatus>? Changed;

    public static string ExeNewPath => Paths.InstalledExe + ".new";

    void Publish(UpdateStatus s)
    {
        Status = s;
        Changed?.Invoke(s);
    }

    public async Task CheckAsync(bool download, CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _checking, 1) == 1) return;
        Publish(Status with { Checking = true, Error = null });
        try
        {
            using var http = Net.CreateClient(TimeSpan.FromMinutes(10));
            using var response = await http.GetAsync($"https://github.com/{AppInfo.GitHubRepo}/releases/latest/download/latest.json", ct);
            Net.EnsureOk(response);
            var manifest = Json.Deserialize<UpdateManifest>(await response.Content.ReadAsStringAsync(ct)) ?? throw new JsonException("latest.json is empty");
            var available = AppInfo.CompareVersions(manifest.Version, AppInfo.Version) > 0;
            Publish(Status with { Latest = manifest.Version, Available = available, LastCheck = DateTime.Now, Notes = manifest.Notes });
            Log.Info(L.T("update.checked", ("latest", manifest.Version), ("current", AppInfo.Version)));

            if (download && available && !AppInfo.IsPrerelease && AppInfo.Mode == "installed")
            {
                await Net.DownloadVerifiedAsync(http, $"https://github.com/{AppInfo.GitHubRepo}/releases/download/v{manifest.Version}/{manifest.Exe.Name}",
                    ExeNewPath, manifest.Exe.Sha256, null, ct);
                Log.Info(L.T("update.ready", ("version", manifest.Version)));
                Publish(Status with { Ready = true });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Log.Warn(L.T("update.failed", ("detail", Net.Describe(ex))));
            Publish(Status with { Error = Net.Describe(ex), LastCheck = DateTime.Now });
        }
        finally
        {
            Publish(Status with { Checking = false });
            Interlocked.Exchange(ref _checking, 0);
        }
    }
}

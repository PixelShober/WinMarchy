namespace WinMarchy.Core.Tools;

/// <summary>The vendor's latest release of a program, as WinMarchy last looked it up.</summary>
public sealed record LatestRelease(string Tag, DateTime CheckedUtc);

/// <summary>
/// Which programs have a newer release than the one installed. Only programs WinMarchy installed
/// itself from a zip or MSI count: it can swap those for the new release (Flow Launcher updates
/// itself, and a program installed some other way belongs to whoever installed it). The latest tags
/// are kept in state.json, so the Start page shows them at once and GitHub is asked at most every
/// few hours.
/// </summary>
public static class ToolUpdates
{
    /// <summary>How long a lookup counts as fresh.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);

    static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>The lookup has new results.</summary>
    public static event Action? Changed;

    /// <summary>Programs WinMarchy can update: installed by it, from a zip or an MSI.</summary>
    public static IReadOnlyList<ToolDef> Updatable(AppState state) => ToolCatalog.All
        .Where(t => state.InstalledTools.Contains(t.Id) && t.Kind is InstallKind.Zip or InstallKind.MsiExtract)
        .ToList();

    /// <summary>The latest release's tag if it is not the one installed, else null (also when it was never looked up).</summary>
    public static string? NewerTag(ToolStatus status, AppState state)
    {
        if (!status.Installed || !state.LatestReleases.TryGetValue(status.Tool.Id, out var latest)) return null;
        return ToolInstaller.IsCurrent(latest.Tag, state.ToolTags.GetValueOrDefault(status.Tool.Id), status.Version) ? null : latest.Tag;
    }

    /// <summary>Whether the latest release of the program was looked up and is the one installed.</summary>
    public static bool IsCurrent(ToolStatus status, AppState state) =>
        status.Installed && state.LatestReleases.ContainsKey(status.Tool.Id) && NewerTag(status, state) == null;

    /// <summary>When the oldest of the updatable programs was looked up, null if one never was.</summary>
    public static DateTime? CheckedUtc(AppState state)
    {
        var tools = Updatable(state);
        if (tools.Count == 0 || tools.Any(t => !state.LatestReleases.ContainsKey(t.Id))) return null;
        return tools.Min(t => state.LatestReleases[t.Id].CheckedUtc);
    }

    /// <summary>
    /// Looks up the latest release of every updatable program whose last lookup is older than
    /// <paramref name="maxAge"/>. A program that cannot be looked up (no network, GitHub refuses)
    /// keeps what was known; the others are not held up by it. Returns whether anything was looked up.
    /// </summary>
    public static async Task<bool> RefreshAsync(TimeSpan maxAge, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var state = AppState.Load();
            var due = Updatable(state)
                .Where(t => !state.LatestReleases.TryGetValue(t.Id, out var l) || DateTime.UtcNow - l.CheckedUtc >= maxAge)
                .ToList();
            if (due.Count == 0) return false;

            var found = new Dictionary<string, LatestRelease>(StringComparer.OrdinalIgnoreCase);
            foreach (var tool in due)
            {
                try
                {
                    if (await ToolInstaller.LatestReleaseAsync(tool, ct) is { } release)
                        found[tool.Id] = new LatestRelease(release.Tag, DateTime.UtcNow);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Debug(L.T("tools.lookupFailed", ("tool", tool.Name), ("detail", Net.Describe(ex))));
                }
            }
            if (found.Count == 0) return false;
            AppState.Update(s =>
            {
                foreach (var (id, latest) in found) s.LatestReleases[id] = latest;
            });
            Changed?.Invoke();
            return true;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Notes a release that was just looked up elsewhere (e.g. by an update), so the Start page agrees with it.</summary>
    public static void Remember(ToolDef tool, string tag)
    {
        AppState.Update(s => s.LatestReleases[tool.Id] = new LatestRelease(tag, DateTime.UtcNow));
        Changed?.Invoke();
    }
}

namespace WinMarchy.Core.GlazeWm;

/// <summary>
/// The workspace-to-screen assignment from the Tiling page, fitted to the screens that are there
/// now. It was chosen for one desk; at another one there may be more screens or fewer. Every
/// screen needs a workspace of its own, or GlazeWM shows an internal one there (the scratchpad
/// came up on the main screen of a fourth monitor), and workspaces of a screen that is gone need
/// a place.
/// </summary>
public static class WorkspacePins
{
    /// <summary>
    /// The assignment for <paramref name="screens"/> screens. Workspaces of screens that are not
    /// there go first to screens without one, then to the screen with the fewest. A screen still
    /// without one gets the highest-numbered workspace of the screen with the most (as long as that
    /// keeps at least one). An empty assignment stays empty: GlazeWM places workspaces itself then.
    /// </summary>
    public static Dictionary<string, int> Effective(IReadOnlyDictionary<string, int> pinned, int screens)
    {
        var result = pinned.Where(p => int.TryParse(p.Key, out var n) && n >= 1 && n <= GlazeConfig.WorkspaceCount)
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        if (result.Count == 0 || screens < 1) return result;

        List<int> On(int screen) => result.Where(p => p.Value == screen).Select(p => int.Parse(p.Key)).OrderBy(n => n).ToList();
        int Fewest() => Enumerable.Range(0, screens).OrderBy(s => On(s).Count).ThenBy(s => s).First();

        // Workspaces of a screen that is gone.
        foreach (var name in result.Where(p => p.Value < 0 || p.Value >= screens).Select(p => p.Key).OrderBy(k => int.Parse(k)).ToList())
            result[name] = Fewest();

        // Screens without a workspace take one from the screen with the most.
        for (int screen = 0; screen < screens; screen++)
        {
            if (On(screen).Count > 0) continue;
            var donor = Enumerable.Range(0, screens).Where(s => On(s).Count > 1).OrderByDescending(s => On(s).Count).ThenByDescending(s => s).FirstOrDefault(-1);
            if (donor < 0) break;
            result[On(donor).Max().ToString()] = screen;
        }
        return result;
    }
}

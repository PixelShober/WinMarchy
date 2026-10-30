using System.Text.Json;
using WinMarchy.Core.Hotkeys;

namespace WinMarchy.Core.GlazeWm;

/// <summary>A monitor as the work modes see it: the regular workspace it shows (null: none) and whether that one has no windows.</summary>
public sealed record ScreenState(int Index, string? Shown, bool ShownEmpty, bool Focused);

/// <summary>A window and where it is: its workspace and that workspace's monitor.</summary>
public sealed record PlacedWindow(string Id, long Handle, string Process, string Workspace, int Monitor);

/// <summary>Every monitor, every open workspace and every window, from "query monitors".</summary>
public sealed record DesktopState(IReadOnlyList<ScreenState> Screens, IReadOnlySet<string> Active, IReadOnlyList<PlacedWindow> Windows)
{
    public static DesktopState From(JsonElement monitorsData)
    {
        var screens = new List<ScreenState>();
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var windows = new List<PlacedWindow>();
        var monitors = GlazeTree.List(monitorsData, "monitors").ToList();
        for (int i = 0; i < monitors.Count; i++)
        {
            string? shown = null;
            bool empty = false;
            foreach (var ws in GlazeTree.Children(monitors[i]).Where(c => GlazeTree.Type(c) == "workspace"))
            {
                var name = GlazeTree.Str(ws, "name") ?? "";
                active.Add(name);
                var list = GlazeTree.Windows(ws).ToList();
                if (GlazeTree.Bool(ws, "isDisplayed") && GlazeTree.IsRegularWorkspace(ws))
                {
                    shown = name;
                    empty = list.Count == 0;
                }
                foreach (var w in list)
                    if (GlazeTree.Str(w, "id") is { } id)
                        windows.Add(new PlacedWindow(id, GlazeTree.Handle(w), GlazeTree.Str(w, "processName") ?? "", name, i));
            }
            screens.Add(new ScreenState(i, shown, empty, GlazeTree.Bool(monitors[i], "hasFocus")));
        }
        return new DesktopState(screens, active, windows);
    }
}

public enum ScreenGoal { Keep, Fresh, Show }

/// <summary>What a monitor should show: what it shows now, a fresh workspace, or a given one.</summary>
public readonly record struct ScreenTarget(ScreenGoal Goal, string? Workspace = null)
{
    public static ScreenTarget Keep => new(ScreenGoal.Keep);
    public static ScreenTarget Fresh => new(ScreenGoal.Fresh);
    public static ScreenTarget Show(string workspace) => new(ScreenGoal.Show, workspace);

    public override string ToString() => Goal == ScreenGoal.Show ? Workspace ?? "" : Goal.ToString().ToLowerInvariant();
}

/// <summary>A window a work mode started: found again by handle and program, as long as the slot still starts the same action.</summary>
public sealed record TrackedWindow(long Handle, string Process, string Action);

/// <summary>
/// C:\WinMarchy\Config\workmodes.json: the windows each work mode started (by handle, GlazeWM's ids
/// change when it restarts) and what the screens showed before the last switch.
/// </summary>
public sealed class WorkModeStore
{
    /// <summary>Mode number ("1" to "3") → one entry per program slot; null where nothing runs.</summary>
    public Dictionary<string, List<TrackedWindow?>> Windows { get; set; } = new();

    /// <summary>The workspace each monitor showed before the last switch; null: nothing to go back to.</summary>
    public List<string?>? Previous { get; set; }

    public static WorkModeStore Load()
    {
        try
        {
            if (File.Exists(Paths.WorkModesFile)) return Json.Deserialize<WorkModeStore>(File.ReadAllText(Paths.WorkModesFile)) ?? new WorkModeStore();
        }
        catch (Exception ex)
        {
            Log.Warn("workmodes.json: " + ex.Message);
        }
        return new WorkModeStore();
    }

    public void Save()
    {
        try { FileSafety.WriteText(Paths.WorkModesFile, Json.Serialize(this, indented: true)); }
        catch (Exception ex) { Log.Warn("workmodes.json: " + Net.Describe(ex)); }
    }
}

/// <summary>
/// What pressing a work mode's key does, worked out from what the screens show now. Programs go to
/// the screen of their slot; a screen that is not connected sends its program to the last one, so
/// on a single screen all of them share it.
/// </summary>
public sealed record WorkModePlan(IReadOnlyList<ScreenTarget> Targets, IReadOnlyList<(int Slot, int Screen)> Start, bool Back)
{
    /// <summary>Nothing to do: the mode is on the screens and there is nothing to go back to.</summary>
    public bool Nothing => !Back && Start.Count == 0 && Targets.All(t => t.Goal == ScreenGoal.Keep);

    /// <summary>The screen a program slot goes to.</summary>
    public static int ScreenFor(int slot, int screens) => Math.Clamp(slot, 0, Math.Max(0, screens - 1));

    /// <summary>
    /// The plan for one press.
    /// <list type="bullet">
    /// <item>The mode's windows are still open: their workspaces come back, and only the programs whose window was closed start again.</item>
    /// <item>Not open: every screen gets a fresh workspace (the windows there stay behind on theirs), and all programs start.</item>
    /// <item>The mode is on the screens already: back to what they showed before.</item>
    /// </list>
    /// </summary>
    /// <param name="apps">Per slot the action to start, null for none.</param>
    /// <param name="alive">Slots whose window is still open, and where it is.</param>
    /// <param name="previous">What each screen showed before the last switch.</param>
    public static WorkModePlan Make(DesktopState state, IReadOnlyList<string?> apps, IReadOnlyDictionary<int, PlacedWindow> alive, IReadOnlyList<string?>? previous)
    {
        int count = state.Screens.Count;
        var targets = new List<ScreenTarget>();
        for (int m = 0; m < count; m++)
        {
            // A window put into the scratchpad or parked off a scrolling row keeps its slot, but shows no workspace of the mode.
            var here = alive.Where(a => a.Value.Monitor == m && GlazeConfig.IsRegular(a.Value.Workspace)).OrderBy(a => a.Key).Select(a => a.Value.Workspace).FirstOrDefault();
            targets.Add(here != null ? ScreenTarget.Show(here) : ScreenTarget.Fresh);
        }
        var start = Enumerable.Range(0, apps.Count).Where(k => apps[k] != null && !alive.ContainsKey(k)).Select(k => (k, ScreenFor(k, count))).ToList();

        bool onScreen = start.Count == 0 && targets.Select((t, m) => t.Goal == ScreenGoal.Show
            ? state.Screens[m].Shown == t.Workspace
            : state.Screens[m].Shown != null && state.Screens[m].ShownEmpty).All(x => x);
        if (!onScreen) return new WorkModePlan(targets, start, Back: false);

        var back = new List<ScreenTarget>();
        for (int m = 0; m < count; m++)
        {
            var before = previous != null && m < previous.Count ? previous[m] : null;
            back.Add(before == null || before == state.Screens[m].Shown ? ScreenTarget.Keep
                : state.Active.Contains(before) ? ScreenTarget.Show(before) : ScreenTarget.Fresh);
        }
        return back.Any(t => t.Goal != ScreenGoal.Keep)
            ? new WorkModePlan(back, Array.Empty<(int, int)>(), Back: true)
            : new WorkModePlan(Enumerable.Repeat(ScreenTarget.Keep, count).ToList(), Array.Empty<(int, int)>(), Back: false);
    }

    /// <summary>
    /// A workspace for a fresh screen: the first free one pinned to this screen; else the first free
    /// one pinned to no screen or to one that is not connected (GlazeWM opens those on the screen in focus).
    /// </summary>
    public static string? FreeWorkspace(int workspaceCount, IReadOnlySet<string> taken, IReadOnlyDictionary<string, int> pinned, int screen, int screens)
    {
        var free = Enumerable.Range(1, workspaceCount).Select(n => n.ToString()).Where(n => !taken.Contains(n)).ToList();
        return free.FirstOrDefault(n => pinned.TryGetValue(n, out var m) && m == screen)
               ?? free.FirstOrDefault(n => !pinned.TryGetValue(n, out var m) || m < 0 || m >= screens);
    }

    /// <summary>What a slot of a work mode starts, or null: empty, or an action that opens no window ("do:", "wm:", "send:").</summary>
    public static string? Startable(string? action, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(action)) return null;
        var text = action.Trim();
        if (!HotkeyAction.TryParse(text, out var a, out var problem))
        {
            error = problem;
            return null;
        }
        bool opensWindow = a.Kind is ActionKind.Web or ActionKind.Run or ActionKind.Open
                           || (a.Kind == ActionKind.App && a.AppId is not ("winmarchy" or "launcher"));
        if (opensWindow) return text;
        error = L.T("workModes.notAProgram", ("action", text));
        return null;
    }
}

/// <summary>
/// Win+Ctrl+D and Win+Ctrl+1 to 3: a work mode on all screens. Its first press puts fresh workspaces
/// on every screen and starts the mode's programs there, one by one, each caught as it opens. Later
/// presses go back to those workspaces and start only what was closed; pressing it while the mode is
/// on the screens goes back to what they showed before. A screen that gets more than one program
/// tiles them in Dwindle while the workspace is open.
/// </summary>
public sealed class WorkModeRunner
{
    /// <summary>One switch at a time: a second press while programs are still starting is dropped.</summary>
    static readonly SemaphoreSlim Busy = new(1, 1);

    readonly WindowCommands _windows;
    readonly LayoutEngine? _layouts;
    readonly Action<string> _notify;

    public WorkModeRunner(WindowCommands windows, LayoutEngine? layouts, Action<string> notify)
    {
        _windows = windows;
        _layouts = layouts;
        _notify = notify;
    }

    /// <param name="number">1 to 3.</param>
    /// <param name="start">Runs an action (the slot's program); it need not wait for the window.</param>
    public async Task RunAsync(int number, UserSettings settings, Action<string> start, CancellationToken ct)
    {
        if (!await Busy.WaitAsync(0, ct))
        {
            Log.Trace($"work mode {number}: the last switch is still starting programs", "desktop");
            return;
        }
        try
        {
            await RunLockedAsync(number, settings, start, ct);
        }
        finally
        {
            Busy.Release();
        }
    }

    async Task RunLockedAsync(int number, UserSettings settings, Action<string> start, CancellationToken ct)
    {
        var mode = settings.WorkModeOf(number);
        var title = mode.Title(number);
        var apps = mode.Apps.Select(a =>
        {
            var action = WorkModePlan.Startable(a, out var error);
            if (error != null) Log.Warn(L.T("workModes.skipped", ("name", title), ("detail", error)));
            return action;
        }).ToList();

        var state = await _windows.DesktopStateAsync(ct);
        if (state.Screens.Count == 0) return;
        var store = WorkModeStore.Load();
        var key = number.ToString();
        var tracked = store.Windows.TryGetValue(key, out var list) ? list : new List<TrackedWindow?>();
        var alive = new Dictionary<int, PlacedWindow>();
        for (int k = 0; k < Math.Min(tracked.Count, apps.Count); k++)
        {
            if (tracked[k] is not { } t || apps[k] != t.Action) continue;
            var found = state.Windows.FirstOrDefault(w => w.Handle == t.Handle && w.Process.Equals(t.Process, StringComparison.OrdinalIgnoreCase));
            if (found != null) alive[k] = found;
        }

        var plan = WorkModePlan.Make(state, apps, alive, store.Previous);
        Log.Trace($"work mode {number} ({title}): {state.Screens.Count} screens showing {string.Join(", ", state.Screens.Select(s => s.Shown ?? "-"))}; " +
                  $"apps {string.Join(", ", apps.Select(a => a ?? "-"))}; open {string.Join(", ", alive.Select(a => $"{a.Key}@{a.Value.Workspace}"))}; " +
                  $"plan {string.Join(", ", plan.Targets)}, start {string.Join(", ", plan.Start.Select(x => $"{x.Slot}→{x.Screen}"))}{(plan.Back ? ", back" : "")}", "desktop");
        if (plan.Nothing)
        {
            _notify(L.T("workModes.already", ("name", title)));
            return;
        }

        var pinned = settings.WorkspaceMonitors;
        if (plan.Back)
        {
            var back = await _windows.ShowScreensAsync(plan.Targets, GlazeConfig.WorkspaceCount, pinned, ct);
            store.Previous = null;
            store.Save();
            _notify(L.T("workModes.back", ("workspaces", Join(back))));
            return;
        }

        store.Previous = state.Screens.Select(s => s.Shown).ToList();
        var shown = await _windows.ShowScreensAsync(plan.Targets, GlazeConfig.WorkspaceCount, pinned, ct);
        if (plan.Targets.Select((t, m) => t.Goal == ScreenGoal.Fresh && (m >= shown.Count || shown[m] == null)).Any(x => x))
            _notify(L.T("tiling.allTaken", ("count", GlazeConfig.WorkspaceCount)));
        _notify(L.T("workModes.opened", ("name", title), ("workspaces", Join(shown))));

        for (int m = 0; m < plan.Targets.Count && m < shown.Count; m++)
            if (plan.Targets[m].Goal == ScreenGoal.Fresh && shown[m] is { } ws && plan.Start.Count(x => x.Screen == m) > 1 && _layouts != null)
                await _layouts.UseUntilClosedAsync(ws, LayoutMode.Dwindle, ct);

        var caught = new Dictionary<int, string>();
        foreach (var (slot, screen) in plan.Start)
        {
            if (screen >= shown.Count || shown[screen] is not { } workspace) continue;
            var action = apps[slot]!;
            Log.Trace($"work mode {number}: starting {action} on workspace {workspace}", "desktop");
            var id = await _windows.CatchNextWindowAsync(workspace, () => start(action), TimeSpan.FromSeconds(12), ct);
            if (id != null) caught[slot] = id;
            else Log.Info(L.T("tiling.desktopAppLate", ("action", action), ("workspace", workspace)));
        }
        // Each program started with its workspace in focus; afterwards the screen the user was on has it again.
        var focused = state.Screens.FirstOrDefault(s => s.Focused)?.Index ?? 0;
        if (plan.Start.Count > 0 && focused < shown.Count && shown[focused] is { } home) await _windows.FocusWorkspaceAsync(home, ct);

        var after = caught.Count > 0 ? await _windows.DesktopStateAsync(ct) : state;
        store.Windows[key] = Enumerable.Range(0, apps.Count).Select(k =>
            alive.ContainsKey(k) ? tracked[k]
            : caught.TryGetValue(k, out var id) && after.Windows.FirstOrDefault(w => w.Id == id) is { } w ? new TrackedWindow(w.Handle, w.Process, apps[k]!)
            : null).ToList();
        store.Save();
    }

    static string Join(IEnumerable<string?> names) => string.Join(" · ", names.OfType<string>().Distinct());
}

using System.Collections.Concurrent;
using System.Text.Json;

namespace WinMarchy.Core.GlazeWm;

/// <summary>
/// Window functions GlazeWM has no single command for, built from queries and commands:
/// Omarchy's scratchpad and split toggle, a new workspace on every monitor, next/previous workspace,
/// moving a window to the neighbouring monitor, restoring minimized windows, transparency per window.
/// </summary>
public sealed class WindowCommands
{
    readonly GlazeIpc _ipc;
    readonly Action<string> _notify;
    readonly SemaphoreSlim _gate;
    /// <summary>Scratchpad windows in the order they went in. Only touched while holding the gate.</summary>
    readonly List<string> _scratch = new();
    /// <summary>Scratchpad windows that are shown right now, and on which workspace.</summary>
    readonly Dictionary<string, string> _shownOn = new();
    readonly ConcurrentDictionary<string, bool> _transparent = new();

    public WindowCommands(GlazeIpc ipc, Action<string> notify, SemaphoreSlim? gate = null)
    {
        _ipc = ipc;
        _notify = notify;
        _gate = gate ?? new SemaphoreSlim(1, 1);
    }

    /// <summary>The share of the screen a shown scratchpad window takes, as in Omarchy's overlay.</summary>
    public const string ScratchSize = "80%";

    async Task<JsonElement?> FocusedWindowAsync(CancellationToken ct)
    {
        var focused = await _ipc.QueryAsync("focused", ct);
        return focused.TryGetProperty("focused", out var f) && GlazeTree.Type(f) == "window" ? f : null;
    }

    Task Cmd(string command, CancellationToken ct, string? id = null) => _ipc.CommandAsync(command, ct, id);

    async Task Exclusive(Func<Task> work, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { await work(); }
        finally { _gate.Release(); }
    }

    // ------------------------------------------------------------------ scratchpad

    /// <summary>
    /// Win+Alt+S: the window (by default the focused one) leaves the layout and disappears into the
    /// scratchpad. It stays there until Win+S shows it, or until it is moved to a workspace.
    /// </summary>
    public Task ScratchStoreAsync(CancellationToken ct, long handle = 0) => Exclusive(async () =>
    {
        string? id;
        if (handle != 0)
        {
            var windows = await _ipc.QueryAsync("windows", ct);
            id = GlazeTree.List(windows, "windows").Where(w => GlazeTree.Handle(w) == handle).Select(w => GlazeTree.Str(w, "id")).FirstOrDefault();
        }
        else id = await FocusedWindowAsync(ct) is { } w ? GlazeTree.Str(w, "id") : null;
        if (id == null) return;

        await Cmd("set-floating --centered --shown-on-top", ct, id);
        await Cmd($"move --workspace {GlazeConfig.ScratchpadWorkspace}", ct, id);
        _scratch.Remove(id);
        _scratch.Add(id);
        _shownOn.Remove(id);
        _notify(L.T("scratchpad.stored"));
    }, ct);

    /// <summary>
    /// Win+S, like Omarchy's special workspace: shows every scratchpad window over the current
    /// workspace, centered and large, and hides them all again on the next press. Shown windows do
    /// not join the layout. A window moved to a workspace (Win+Shift+1 …) leaves the scratchpad.
    /// </summary>
    public Task ScratchToggleAsync(CancellationToken ct) => Exclusive(async () =>
    {
        var data = await _ipc.QueryAsync("workspaces", ct);
        var all = GlazeTree.List(data, "workspaces").ToList();
        var pad = all.FirstOrDefault(w => GlazeTree.Str(w, "name") == GlazeConfig.ScratchpadWorkspace);
        var workspaceOf = new Dictionary<string, string>();
        foreach (var ws in all)
        foreach (var win in GlazeTree.Windows(ws))
            if (GlazeTree.Str(win, "id") is { } wid && GlazeTree.Str(ws, "name") is { } wname) workspaceOf[wid] = wname;

        // Windows in the scratchpad workspace belong to it, also after WinMarchy restarted.
        if (pad.ValueKind == JsonValueKind.Object)
            foreach (var win in GlazeTree.Windows(pad))
                if (GlazeTree.Str(win, "id") is { } wid && !_scratch.Contains(wid)) _scratch.Add(wid);
        // Forget windows that were closed.
        _scratch.RemoveAll(id => !workspaceOf.ContainsKey(id));
        foreach (var id in _shownOn.Keys.Where(k => !workspaceOf.ContainsKey(k)).ToList()) _shownOn.Remove(id);

        if (_scratch.Count == 0)
        {
            _notify(L.T("scratchpad.empty"));
            return;
        }
        var here = GlazeTree.FocusedWorkspace(data) is { } f ? GlazeTree.Str(f, "name") : null;
        if (here == null || here == GlazeConfig.ScratchpadWorkspace) return;

        bool shownHere = _scratch.Any(id => workspaceOf[id] == here);
        if (shownHere)
        {
            foreach (var id in _scratch.ToList())
            {
                var on = workspaceOf[id];
                if (on == GlazeConfig.ScratchpadWorkspace) continue;
                if (_shownOn.TryGetValue(id, out var shownOn) && shownOn != on)
                {
                    // Moved to another workspace on purpose: an ordinary window from now on.
                    _scratch.Remove(id);
                    _shownOn.Remove(id);
                    continue;
                }
                await Cmd($"move --workspace {GlazeConfig.ScratchpadWorkspace}", ct, id);
                _shownOn.Remove(id);
            }
            return;
        }

        foreach (var id in _scratch)
        {
            if (workspaceOf[id] != here) await Cmd($"move --workspace {here}", ct, id);
            await Cmd("set-floating --centered --shown-on-top", ct, id);
            await Cmd($"size --width {ScratchSize} --height {ScratchSize}", ct, id);
            await Cmd("position --centered", ct, id);
            _shownOn[id] = here;
        }
        await Cmd($"focus --container-id {_scratch[^1]}", ct);
    }, ct);

    // ------------------------------------------------------------------ split

    /// <summary>
    /// Win+J, Omarchy's "toggle split": the focused window and its neighbour swap side-by-side for
    /// one above the other (or back), right away. GlazeWM's own toggle-tiling-direction only decides
    /// where the next window goes, so nothing would move.
    /// </summary>
    public Task ToggleSplitAsync(CancellationToken ct) => Exclusive(async () =>
    {
        if (await FocusedWindowAsync(ct) is not { } focused || GlazeTree.WindowState(focused) != "tiling" ||
            GlazeTree.Str(focused, "id") is not { } id) return;
        var data = await _ipc.QueryAsync("workspaces", ct);
        var map = GlazeTree.Index(GlazeTree.List(data, "workspaces"));
        if (!map.TryGetValue(id, out var self)) return;

        // Dwindle wraps the focused window alone in a split container (that only decides where the next
        // window goes). The visible split is the first level up where the window has a neighbour.
        var node = self;
        var parent = Parent(map, node);
        while (parent is { } p && GlazeTree.Type(p) == "split" && GlazeTree.TilingChildren(p).Count() == 1)
        {
            node = p;
            parent = Parent(map, p);
        }
        if (parent is not { } pair) return;
        var nodeId = GlazeTree.Str(node, "id");
        var others = GlazeTree.TilingChildren(pair).Where(c => GlazeTree.Str(c, "id") != nodeId).ToList();
        if (others.Count == 0) return;

        if (GlazeTree.Type(pair) == "workspace")
        {
            await Cmd("toggle-tiling-direction", ct, GlazeTree.Str(pair, "id"));
            return;
        }
        if (others.Count != 1 || GlazeTree.Type(others[0]) != "window" || GlazeTree.Str(others[0], "id") is not { } otherId)
        {
            // Deeper nesting: GlazeWM's toggle, which takes effect with the next window.
            await Cmd("toggle-tiling-direction", ct, id);
            _notify(L.T("tiling.splitNext"));
            return;
        }

        var pairId = GlazeTree.Str(pair, "id");
        var direction = GlazeTree.Str(pair, "tilingDirection") ?? "horizontal";
        var inverse = direction == "horizontal" ? "vertical" : "horizontal";

        // The window directly in the pair: unwrap the single-child containers around it.
        for (int i = 0; i < 4 && GlazeTree.Str(self, "parentId") != pairId; i++)
        {
            var wrapper = Parent(map, self);
            var d = wrapper is { } w ? GlazeTree.Str(w, "tilingDirection") : null;
            await Cmd("set-tiling-direction " + (d == "horizontal" ? "vertical" : "horizontal"), ct, id);
            data = await _ipc.QueryAsync("workspaces", ct);
            map = GlazeTree.Index(GlazeTree.List(data, "workspaces"));
            if (!map.TryGetValue(id, out self)) return;
        }
        if (GlazeTree.Str(self, "parentId") != pairId) return;

        // The neighbour gets its own container in the other direction, then the window joins it: the
        // pair is now arranged the other way round.
        bool selfFirst = Index(pair, id) < Index(pair, otherId);
        await Cmd("set-tiling-direction " + inverse, ct, otherId);
        var toward = direction == "horizontal" ? (selfFirst ? "right" : "left") : (selfFirst ? "down" : "up");
        await Cmd("move --direction " + toward, ct, id);
        // GlazeWM puts the moved window second; the one that was left or on top stays first.
        if (selfFirst) await Cmd("move --direction " + (inverse == "vertical" ? "up" : "left"), ct, id);
    }, ct);

    static JsonElement? Parent(Dictionary<string, JsonElement> map, JsonElement e) =>
        GlazeTree.Str(e, "parentId") is { } pid && map.TryGetValue(pid, out var p) ? p : null;

    static int Index(JsonElement parent, string childId) =>
        GlazeTree.Children(parent).Select((c, i) => (c, i)).FirstOrDefault(x => GlazeTree.Str(x.c, "id") == childId).i;

    // ------------------------------------------------------------------ workspaces and monitors

    /// <summary>Win+Tab: next workspace with windows on this monitor (the scratchpad is skipped).</summary>
    public async Task WorkspaceStepAsync(int step, CancellationToken ct)
    {
        var workspaces = await _ipc.QueryAsync("workspaces", ct);
        if (GlazeTree.FocusedWorkspace(workspaces) is not { } focused) return;
        var monitor = GlazeTree.Str(focused, "parentId");
        var names = GlazeTree.List(workspaces, "workspaces")
            .Where(w => GlazeTree.Str(w, "parentId") == monitor && GlazeTree.IsRegularWorkspace(w))
            .Select(w => GlazeTree.Str(w, "name")!)
            .OrderBy(n => n, Comparer<string>.Create(GlazeTree.CompareNames))
            .ToList();
        if (names.Count < 2) return;
        var i = names.IndexOf(GlazeTree.Str(focused, "name")!);
        var next = names[((i + step) % names.Count + names.Count) % names.Count];
        await Cmd($"focus --workspace {next}", ct);
    }

    /// <summary>
    /// Win+Ctrl+D, like a new virtual desktop in Windows: every monitor gets an empty workspace.
    /// Monitors that already show an empty one keep it.
    /// </summary>
    public Task NewDesktopAsync(int workspaceCount, CancellationToken ct) => Exclusive(async () =>
    {
        var data = await _ipc.QueryAsync("monitors", ct);
        var monitors = GlazeTree.List(data, "monitors").ToList();
        if (monitors.Count == 0) return;
        int focusedMonitor = Math.Max(0, monitors.FindIndex(m => GlazeTree.Bool(m, "hasFocus")));
        var active = monitors.SelectMany(GlazeTree.Children).Select(w => GlazeTree.Str(w, "name")).OfType<string>().ToHashSet();
        var free = new Queue<string>(Enumerable.Range(1, workspaceCount).Select(n => n.ToString()).Where(n => !active.Contains(n)));
        var opened = new List<string>();

        for (int i = 0; i < monitors.Count; i++)
        {
            var shown = GlazeTree.Children(monitors[i]).FirstOrDefault(w => GlazeTree.Bool(w, "isDisplayed"));
            if (shown.ValueKind == JsonValueKind.Object && !GlazeTree.Windows(shown).Any() && GlazeTree.IsRegularWorkspace(shown))
            {
                opened.Add(GlazeTree.Str(shown, "name")!);
                continue;
            }
            if (free.Count == 0)
            {
                _notify(L.T("tiling.allTaken", ("count", workspaceCount)));
                break;
            }
            var name = free.Dequeue();
            await Cmd($"focus --monitor {i}", ct);
            await Cmd($"focus --workspace {name}", ct);
            opened.Add(name);
        }
        if (monitors.Count > 1) await Cmd($"focus --monitor {focusedMonitor}", ct);
        if (opened.Count > 0) _notify(L.T("tiling.newDesktop", ("workspaces", string.Join(" · ", opened))));
    }, ct);

    /// <summary>
    /// Win+Ctrl+Left/Right: every monitor goes to its next (or previous) workspace at once, the way
    /// Windows switches virtual desktops on all screens together.
    /// </summary>
    public Task DesktopStepAsync(int step, CancellationToken ct) => Exclusive(async () =>
    {
        var data = await _ipc.QueryAsync("monitors", ct);
        var monitors = GlazeTree.List(data, "monitors").ToList();
        if (monitors.Count == 0) return;
        int focusedMonitor = Math.Max(0, monitors.FindIndex(m => GlazeTree.Bool(m, "hasFocus")));
        bool moved = false;
        for (int i = 0; i < monitors.Count; i++)
        {
            var workspaces = GlazeTree.Children(monitors[i]).Where(GlazeTree.IsRegularWorkspace).ToList();
            var names = workspaces.Select(w => GlazeTree.Str(w, "name")!).OrderBy(n => n, Comparer<string>.Create(GlazeTree.CompareNames)).ToList();
            var shown = workspaces.FirstOrDefault(w => GlazeTree.Bool(w, "isDisplayed"));
            if (names.Count < 2 || shown.ValueKind != JsonValueKind.Object) continue;
            var at = names.IndexOf(GlazeTree.Str(shown, "name")!);
            var next = names[((at + step) % names.Count + names.Count) % names.Count];
            if (monitors.Count > 1) await Cmd($"focus --monitor {i}", ct);
            await Cmd($"focus --workspace {next}", ct);
            moved = true;
        }
        if (moved && monitors.Count > 1) await Cmd($"focus --monitor {focusedMonitor}", ct);
    }, ct);

    /// <summary>Win+Shift+Ctrl+Arrow: the focused window goes to the workspace shown on the neighbouring monitor.</summary>
    public async Task MoveWindowToMonitorAsync(string direction, CancellationToken ct)
    {
        var data = await _ipc.QueryAsync("monitors", ct);
        var monitors = GlazeTree.List(data, "monitors").ToList();
        var current = monitors.Cast<JsonElement?>().FirstOrDefault(m => GlazeTree.Bool(m!.Value, "hasFocus"));
        if (current is not { } cur) return;
        double Cx(JsonElement m) => GlazeTree.Int(m, "x") + GlazeTree.Int(m, "width") / 2.0;
        double Cy(JsonElement m) => GlazeTree.Int(m, "y") + GlazeTree.Int(m, "height") / 2.0;
        var candidates = monitors.Where(m => GlazeTree.Str(m, "id") != GlazeTree.Str(cur, "id")).Where(m => direction switch
        {
            "left" => Cx(m) < Cx(cur),
            "right" => Cx(m) > Cx(cur),
            "up" => Cy(m) < Cy(cur),
            _ => Cy(m) > Cy(cur),
        }).OrderBy(m => Math.Abs(Cx(m) - Cx(cur)) + Math.Abs(Cy(m) - Cy(cur))).ToList();
        if (candidates.Count == 0) return;
        var target = GlazeTree.Children(candidates[0]).Cast<JsonElement?>().FirstOrDefault(w => GlazeTree.Bool(w!.Value, "isDisplayed"));
        if (target is { } t && GlazeTree.Str(t, "name") is { } name)
            await Cmd($"move --workspace {name}", ct);
    }

    // ------------------------------------------------------------------ single windows

    public async Task RestoreMinimizedAsync(CancellationToken ct)
    {
        var data = await _ipc.QueryAsync("windows", ct);
        foreach (var w in GlazeTree.List(data, "windows").Where(w => GlazeTree.WindowState(w) == "minimized"))
            if (GlazeTree.Str(w, "id") is { } id)
                await Cmd("toggle-minimized", ct, id);
    }

    /// <summary>Win+Backspace, as in Omarchy: the focused window becomes see-through, or opaque again.</summary>
    public async Task ToggleTransparencyAsync(CancellationToken ct)
    {
        if (await FocusedWindowAsync(ct) is not { } w || GlazeTree.Str(w, "id") is not { } id) return;
        var on = !_transparent.ContainsKey(id);
        await Cmd("set-transparency --opacity " + (on ? "85%" : "100%"), ct, id);
        if (on) _transparent[id] = true; else _transparent.TryRemove(id, out _);
    }

    /// <summary>
    /// Focuses a window by its handle through GlazeWM, which also switches to its workspace. For
    /// WinMarchy's own window when it was left on another workspace.
    /// </summary>
    public async Task<bool> FocusHandleAsync(long handle, CancellationToken ct)
    {
        var data = await _ipc.QueryAsync("windows", ct);
        var id = GlazeTree.List(data, "windows").Where(w => GlazeTree.Handle(w) == handle).Select(w => GlazeTree.Str(w, "id")).FirstOrDefault();
        if (id == null) return false;
        await Cmd($"focus --container-id {id}", ct);
        return true;
    }
}

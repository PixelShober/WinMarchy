using System.Text.Json;

namespace WinMarchy.Core.GlazeWm;

/// <summary>
/// C:\WinMarchy\Config\scratchpad.json: which windows belong to the scratchpad, by handle. GlazeWM's
/// ids change when it restarts, a window's handle does not.
/// </summary>
public sealed class ScratchStore
{
    public List<long> Members { get; set; } = new();

    public static ScratchStore Load()
    {
        try
        {
            if (File.Exists(Paths.ScratchpadFile)) return Json.Deserialize<ScratchStore>(File.ReadAllText(Paths.ScratchpadFile)) ?? new ScratchStore();
        }
        catch (Exception ex)
        {
            Log.Warn("scratchpad.json: " + ex.Message);
        }
        return new ScratchStore();
    }

    public void Save()
    {
        try { FileSafety.WriteText(Paths.ScratchpadFile, Json.Serialize(this, indented: true)); }
        catch (Exception ex) { Log.Warn("scratchpad.json: " + Net.Describe(ex)); }
    }
}

/// <summary>
/// Window functions GlazeWM has no single command for, built from queries and commands: Omarchy's
/// scratchpad, focus and swap that also work from floating windows and in the scrolling layout, the
/// split toggle, a new workspace on every monitor, next/previous workspace, restoring minimized windows.
/// </summary>
public sealed class WindowCommands
{
    readonly GlazeIpc _ipc;
    readonly Action<string> _notify;
    readonly SemaphoreSlim _gate;
    readonly LayoutEngine? _layouts;
    readonly Func<UserSettings> _settings;
    readonly ScratchStore _pad = ScratchStore.Load();

    public WindowCommands(GlazeIpc ipc, Action<string> notify, SemaphoreSlim? gate = null, LayoutEngine? layouts = null, Func<UserSettings>? settings = null)
    {
        _ipc = ipc;
        _notify = notify;
        _gate = gate ?? new SemaphoreSlim(1, 1);
        _layouts = layouts;
        _settings = settings ?? (() => new UserSettings());
    }

    /// <summary>The share of the screen the shown scratchpad takes, as in Omarchy's overlay.</summary>
    public const double ScratchShare = 0.8;

    Task Cmd(string command, CancellationToken ct, string? id = null) => _ipc.CommandAsync(command, ct, id);

    async Task Exclusive(Func<Task> work, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { await work(); }
        finally { _gate.Release(); }
    }

    /// <summary>All windows and the focused regular workspace, from one query.</summary>
    async Task<(List<WindowInfo> All, string? Here)> SnapshotAsync(CancellationToken ct)
    {
        var data = await _ipc.QueryAsync("workspaces", ct);
        var here = GlazeTree.FocusedWorkspace(data) is { } f ? GlazeTree.Str(f, "name") : null;
        return (GlazeTree.WindowInfos(data), here != null && GlazeConfig.IsRegular(here) ? here : null);
    }

    // ------------------------------------------------------------------ scratchpad

    bool IsMember(WindowInfo w) => _pad.Members.Contains(w.Handle);

    /// <summary>
    /// Forgets closed windows, and windows that left on their own: put back into the tiles (Win+T)
    /// while the scratchpad was shown.
    /// </summary>
    void Prune(List<WindowInfo> all)
    {
        var byHandle = all.Where(w => w.Handle != 0).GroupBy(w => w.Handle).ToDictionary(g => g.Key, g => g.First());
        int removed = _pad.Members.RemoveAll(h => !byHandle.TryGetValue(h, out var w) || (GlazeConfig.IsRegular(w.Workspace) && w.State == "tiling"));
        if (removed > 0) _pad.Save();
    }

    /// <summary>
    /// After connecting: windows a crash left in the staging workspace, and windows in the scratchpad
    /// workspace that never went there through Win+Alt+S, come back to the current workspace.
    /// </summary>
    public Task RecoverAsync(CancellationToken ct) => Exclusive(async () =>
    {
        var (all, here) = await SnapshotAsync(ct);
        Prune(all);
        if (here == null) return;
        var lost = all.Where(w => w.Workspace == GlazeConfig.StageWorkspace || (w.Workspace == GlazeConfig.ScratchpadWorkspace && !IsMember(w))).ToList();
        foreach (var w in lost)
        {
            if (w.Floating) await Cmd("set-tiling", ct, w.Id);
            await Cmd($"move --workspace {here}", ct, w.Id);
        }
        if (lost.Count > 0) Log.Info(L.T("scratchpad.recovered", ("count", lost.Count), ("workspace", here)));
    }, ct);

    /// <summary>
    /// Win+Alt+S, Omarchy's "move to scratchpad": the focused window disappears into the scratchpad.
    /// Pressed on a scratchpad window, it brings the window back into the tiles of this workspace.
    /// </summary>
    public Task ScratchStoreAsync(CancellationToken ct) => Exclusive(async () =>
    {
        var (all, _) = await SnapshotAsync(ct);
        Prune(all);
        var focused = all.FirstOrDefault(w => w.Focused);
        if (focused == null || !GlazeConfig.IsRegular(focused.Workspace)) return;

        if (IsMember(focused))
        {
            _pad.Members.Remove(focused.Handle);
            _pad.Save();
            await Cmd("set-tiling", ct, focused.Id);
            _notify(L.T("scratchpad.released"));
            return;
        }
        bool scrolling = _layouts?.StripOf(focused.Workspace) != null;
        _layouts?.Forget(focused.Id);
        await Cmd("set-floating --shown-on-top", ct, focused.Id);
        await Cmd($"move --workspace {GlazeConfig.ScratchpadWorkspace}", ct, focused.Id);
        _pad.Members.Add(focused.Handle);
        _pad.Save();
        _notify(L.T("scratchpad.stored"));
        if (scrolling) await _layouts!.ArrangeLockedAsync(focused.Workspace, null, ct, focus: false);
    }, ct);

    /// <summary>
    /// Win+S, like Omarchy's special workspace: shows every scratchpad window over the current
    /// workspace, side by side in the middle of the screen, and hides them all on the next press.
    /// Ways out: Win+Alt+S on the window, Win+Shift+1 … (to that workspace), or Win+T.
    /// </summary>
    public Task ScratchToggleAsync(CancellationToken ct) => Exclusive(async () =>
    {
        var (all, here) = await SnapshotAsync(ct);
        Prune(all);
        var members = Members(all);
        if (members.Count == 0)
        {
            _notify(L.T("scratchpad.empty"));
            return;
        }
        if (here == null) return;
        var shown = members.Where(w => GlazeConfig.IsRegular(w.Workspace)).ToList();
        if (shown.Any(w => w.Workspace == here))
        {
            await HideLockedAsync(shown, ct);
            return;
        }
        await ShowLockedAsync(members, here, ct);
    }, ct);

    /// <summary>Scratchpad windows in the order they went in.</summary>
    List<WindowInfo> Members(List<WindowInfo> all) =>
        all.Where(IsMember).OrderBy(w => _pad.Members.IndexOf(w.Handle)).ToList();

    async Task HideLockedAsync(IEnumerable<WindowInfo> shown, CancellationToken ct)
    {
        foreach (var w in shown) await Cmd($"move --workspace {GlazeConfig.ScratchpadWorkspace}", ct, w.Id);
    }

    async Task ShowLockedAsync(List<WindowInfo> members, string here, CancellationToken ct)
    {
        foreach (var w in members)
        {
            if (w.Workspace != here) await Cmd($"move --workspace {here}", ct, w.Id);
            await Cmd("set-floating --shown-on-top", ct, w.Id);
        }
        var area = WorkspaceArea.All(await _ipc.QueryAsync("monitors", ct)).FirstOrDefault(a => a.Name == here);
        if (area != null)
        {
            // One overlay of 80 % with the windows side by side in it (more than three: two rows).
            int count = members.Count, cols = Math.Min(count, 3), rows = (count + cols - 1) / cols;
            int gap = (int)Math.Round(_settings().InnerGap * area.Scale);
            int ow = (int)(area.Width * ScratchShare), oh = (int)(area.Height * ScratchShare);
            int ox = area.X + (area.Width - ow) / 2, oy = area.Y + (area.Height - oh) / 2;
            int cw = (ow - gap * (cols - 1)) / cols, ch = (oh - gap * (rows - 1)) / rows;
            for (int i = 0; i < count; i++)
            {
                int col = i % cols, row = i / cols;
                await Cmd($"size --width {cw}px --height {ch}px", ct, members[i].Id);
                await Cmd($"position --x-pos {ox + col * (cw + gap)} --y-pos {oy + row * (ch + gap)}", ct, members[i].Id);
            }
        }
        await Cmd($"focus --container-id {members[^1].Id}", ct);
    }

    // ------------------------------------------------------------------ focus and swap

    /// <summary>
    /// Win+Arrow. In the shown scratchpad the arrows move between its windows; past the last one the
    /// scratchpad hides and the workspace has the focus again. In the scrolling layout they move along
    /// the columns. From any other floating window they go to the nearest window that way on the
    /// screen; GlazeWM itself only knows left and right there.
    /// </summary>
    public Task FocusAsync(string direction, CancellationToken ct) => Exclusive(async () =>
    {
        var (all, here) = await SnapshotAsync(ct);
        Prune(all);
        var focused = all.FirstOrDefault(w => w.Focused);
        if (focused != null && IsMember(focused) && GlazeConfig.IsRegular(focused.Workspace))
        {
            var others = all.Where(w => w.Workspace == focused.Workspace && IsMember(w));
            if (GlazeTree.Neighbor(focused, others, direction) is { } next)
            {
                await Cmd($"focus --container-id {next.Id}", ct);
                return;
            }
            await HideLockedAsync(Members(all).Where(w => GlazeConfig.IsRegular(w.Workspace)), ct);
            return;
        }
        if (here != null && _layouts != null && focused is not { OnTop: true } && await _layouts.ScrollFocusLockedAsync(here, direction, ct)) return;
        if (focused is { Floating: true })
        {
            var candidates = all.Where(w => w.Workspace == focused.Workspace && w.State is "tiling" or "floating");
            if (GlazeTree.Neighbor(focused, candidates, direction) is { } near)
            {
                await Cmd($"focus --container-id {near.Id}", ct);
                return;
            }
        }
        await Cmd($"focus --direction {direction}", ct);
    }, ct);

    /// <summary>Win+Shift+Arrow: swaps tiles, columns of the scrolling layout, or windows of the shown scratchpad.</summary>
    public Task SwapAsync(string direction, CancellationToken ct) => Exclusive(async () =>
    {
        var (all, here) = await SnapshotAsync(ct);
        Prune(all);
        var focused = all.FirstOrDefault(w => w.Focused);
        if (focused != null && IsMember(focused) && here != null && focused.Workspace == here)
        {
            var shown = all.Where(w => w.Workspace == here && IsMember(w));
            if (GlazeTree.Neighbor(focused, shown, direction) is { } other)
            {
                int a = _pad.Members.IndexOf(focused.Handle), b = _pad.Members.IndexOf(other.Handle);
                (_pad.Members[a], _pad.Members[b]) = (_pad.Members[b], _pad.Members[a]);
                _pad.Save();
                var members = Members(all).Where(w => w.Workspace == here).ToList();
                // The moved window keeps the focus: it goes last in the call, which focuses the last one.
                members.Remove(focused);
                await ShowLockedAsync(members.Append(focused).OrderBy(w => _pad.Members.IndexOf(w.Handle)).ToList(), here, ct);
                await Cmd($"focus --container-id {focused.Id}", ct);
            }
            return;
        }
        if (here != null && _layouts != null && await _layouts.ScrollSwapLockedAsync(here, direction, ct)) return;
        await Cmd($"move --direction {direction}", ct);
    }, ct);

    /// <summary>
    /// Win+Shift+1 …: the focused window goes to that workspace, and the focus follows. A scratchpad
    /// window leaves the scratchpad on the way and becomes an ordinary tiled window there.
    /// </summary>
    public Task ToWorkspaceAsync(string target, CancellationToken ct) => Exclusive(async () =>
    {
        var (all, _) = await SnapshotAsync(ct);
        Prune(all);
        var focused = all.FirstOrDefault(w => w.Focused);
        if (focused == null)
        {
            await Cmd($"focus --workspace {target}", ct);
            return;
        }
        var source = focused.Workspace;
        bool fromScrolling = _layouts?.StripOf(source) != null;
        if (IsMember(focused))
        {
            _pad.Members.Remove(focused.Handle);
            _pad.Save();
            await Cmd("set-tiling", ct, focused.Id);
        }
        else if (fromScrolling)
        {
            _layouts!.Forget(focused.Id);
            await Cmd("set-tiling", ct, focused.Id);
        }
        await Cmd($"move --workspace {target}", ct, focused.Id);
        await Cmd($"focus --workspace {target}", ct);
        if (_layouts == null) return;
        if (fromScrolling && source != target) await _layouts.ArrangeLockedAsync(source, null, ct, focus: false);
        if (_layouts.ModeOf(target) == LayoutMode.Scrolling) await _layouts.ArrangeLockedAsync(target, focused.Id, ct);
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

    async Task<JsonElement?> FocusedWindowAsync(CancellationToken ct)
    {
        var focused = await _ipc.QueryAsync("focused", ct);
        return focused.TryGetProperty("focused", out var f) && GlazeTree.Type(f) == "window" ? f : null;
    }

    static JsonElement? Parent(Dictionary<string, JsonElement> map, JsonElement e) =>
        GlazeTree.Str(e, "parentId") is { } pid && map.TryGetValue(pid, out var p) ? p : null;

    static int Index(JsonElement parent, string childId) =>
        GlazeTree.Children(parent).Select((c, i) => (c, i)).FirstOrDefault(x => GlazeTree.Str(x.c, "id") == childId).i;

    // ------------------------------------------------------------------ workspaces and monitors

    /// <summary>Win+Tab: next workspace with windows on this monitor (the hidden ones are skipped).</summary>
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
    /// Monitors that already show an empty one keep it. Workspaces pinned to a screen are only used
    /// there. Returns which workspace each monitor (by index) now shows.
    /// </summary>
    public async Task<IReadOnlyList<(int Monitor, string Workspace)>> NewDesktopAsync(int workspaceCount, IReadOnlyDictionary<string, int> pinned, CancellationToken ct)
    {
        var opened = new List<(int, string)>();
        await Exclusive(async () =>
        {
            var data = await _ipc.QueryAsync("monitors", ct);
            var monitors = GlazeTree.List(data, "monitors").ToList();
            if (monitors.Count == 0) return;
            int focusedMonitor = Math.Max(0, monitors.FindIndex(m => GlazeTree.Bool(m, "hasFocus")));
            var active = monitors.SelectMany(GlazeTree.Children).Select(w => GlazeTree.Str(w, "name")).OfType<string>().ToHashSet();
            var names = Enumerable.Range(1, workspaceCount).Select(n => n.ToString()).ToList();

            for (int i = 0; i < monitors.Count; i++)
            {
                var shown = GlazeTree.Children(monitors[i]).FirstOrDefault(w => GlazeTree.Bool(w, "isDisplayed"));
                if (shown.ValueKind == JsonValueKind.Object && !GlazeTree.Windows(shown).Any() && GlazeTree.IsRegularWorkspace(shown))
                {
                    opened.Add((i, GlazeTree.Str(shown, "name")!));
                    continue;
                }
                // This monitor's own workspaces if it has any, else the ones pinned to no monitor.
                bool hasOwn = pinned.Values.Contains(i);
                var name = names.FirstOrDefault(n => !active.Contains(n) &&
                    (hasOwn ? pinned.TryGetValue(n, out var m) && m == i : !pinned.ContainsKey(n)));
                if (name == null)
                {
                    _notify(L.T("tiling.allTaken", ("count", workspaceCount)));
                    continue;
                }
                active.Add(name);
                await Cmd($"focus --monitor {i}", ct);
                await Cmd($"focus --workspace {name}", ct);
                opened.Add((i, name));
            }
            if (monitors.Count > 1) await Cmd($"focus --monitor {focusedMonitor}", ct);
        }, ct);
        if (opened.Count > 0) _notify(L.T("tiling.newDesktop", ("workspaces", string.Join(" · ", opened.Select(o => o.Item2)))));
        return opened;
    }

    /// <summary>
    /// Waits for the next window GlazeWM takes on (an app that was just started) and moves it to the
    /// given workspace if it opened elsewhere. False if none came in time.
    /// </summary>
    public async Task<bool> CatchNextWindowAsync(string workspace, Action start, TimeSpan timeout, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnEvent(GlazeEvent e)
        {
            if (e.Type == "window_managed" && e.Data.TryGetProperty("managedWindow", out var w) && GlazeTree.Str(w, "id") is { } id)
                tcs.TrySetResult(id);
        }
        _ipc.Event += OnEvent;
        try
        {
            start();
            var done = await Task.WhenAny(tcs.Task, Task.Delay(timeout, ct));
            if (done != tcs.Task) return false;
            var data = await _ipc.QueryAsync("workspaces", ct);
            if (GlazeTree.WorkspaceOf(data, tcs.Task.Result) is { } ws && GlazeTree.Str(ws, "name") != workspace)
                await Cmd($"move --workspace {workspace}", ct, tcs.Task.Result);
            return true;
        }
        finally
        {
            _ipc.Event -= OnEvent;
        }
    }

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

    /// <summary>Win+Shift+Ctrl+Arrow (older profiles): the focused window goes to the workspace shown on the neighbouring monitor.</summary>
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

    /// <summary>Win+M: every minimized window back to where it was. Returns how many.</summary>
    public async Task<int> RestoreMinimizedAsync(CancellationToken ct)
    {
        var data = await _ipc.QueryAsync("windows", ct);
        int count = 0;
        foreach (var w in GlazeTree.List(data, "windows").Where(w => GlazeTree.WindowState(w) == "minimized"))
        {
            if (GlazeTree.Str(w, "id") is not { } id) continue;
            await Cmd("toggle-minimized", ct, id);
            count++;
        }
        return count;
    }

    /// <summary>Win+Backspace in older profiles: the focused window becomes see-through, or opaque again.</summary>
    public async Task ToggleTransparencyAsync(CancellationToken ct)
    {
        if (await FocusedWindowAsync(ct) is not { } w || GlazeTree.Str(w, "id") is not { } id) return;
        var on = !_transparent.Contains(id);
        await Cmd("set-transparency --opacity " + (on ? "85%" : "100%"), ct, id);
        if (on) _transparent.Add(id); else _transparent.Remove(id);
    }

    readonly HashSet<string> _transparent = new();

    /// <summary>Whether GlazeWM manages this window (minimize blocking leaves all others alone).</summary>
    public async Task<bool> ManagesAsync(long handle, CancellationToken ct)
    {
        var data = await _ipc.QueryAsync("windows", ct);
        return GlazeTree.List(data, "windows").Any(w => GlazeTree.Handle(w) == handle);
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

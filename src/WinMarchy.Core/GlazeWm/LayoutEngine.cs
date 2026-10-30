using System.Collections.Concurrent;
using System.Text.Json;

namespace WinMarchy.Core.GlazeWm;

/// <summary>
/// Omarchy's workspace layouts as far as GlazeWM can follow. Win+Ctrl+J cycles Dwindle → Columns →
/// Scrolling; Floating and Manual are set on the Tiling page.
/// <list type="bullet">
/// <item>Dwindle: every focused window splits along its longer side, so the next window takes half of it.</item>
/// <item>Columns: new windows line up side by side.</item>
/// <item>Scrolling: a row of columns wider than the screen, as in Hyprland's scrolling layout. GlazeWM
/// cannot tile wider than the screen, so WinMarchy places these windows itself (they float).</item>
/// <item>Floating: every window of the workspace floats; leaving the mode tiles those it floated again.</item>
/// <item>Manual: GlazeWM as it is; Win+J decides the split.</item>
/// </list>
/// </summary>
public enum LayoutMode { Dwindle, Columns, Floating, Manual, Scrolling }

public sealed class LayoutStore
{
    public Dictionary<string, LayoutMode> Workspaces { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Scrolling workspaces: their columns left to right, by window handle (survives a restart of WinMarchy).</summary>
    public Dictionary<string, List<long>> Columns { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Workspaces whose layout holds only while they are open (a work mode's shared screen), with the
    /// layout chosen for them before (null: the default).
    /// </summary>
    public Dictionary<string, LayoutMode?> UntilClosed { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static LayoutStore Load()
    {
        try
        {
            if (File.Exists(Paths.LayoutsFile)) return Json.Deserialize<LayoutStore>(File.ReadAllText(Paths.LayoutsFile)) ?? new LayoutStore();
        }
        catch (Exception ex)
        {
            Log.Warn("layouts.json: " + ex.Message);
        }
        return new LayoutStore();
    }

    public void Save() => FileSafety.WriteText(Paths.LayoutsFile, Json.Serialize(this, indented: true));
}

/// <summary>The columns of one scrolling workspace, left to right.</summary>
public sealed class ScrollStrip
{
    public List<string> Ids { get; } = new();
    public int Focus { get; set; }
    /// <summary>First column on the screen.</summary>
    public int Start { get; set; }

    /// <summary>Moves <see cref="Start"/> as little as possible so the focused column is on the screen.</summary>
    public void Reveal(int visible)
    {
        if (Ids.Count == 0) { Focus = Start = 0; return; }
        Focus = Math.Clamp(Focus, 0, Ids.Count - 1);
        visible = Math.Clamp(visible, 1, Ids.Count);
        if (Focus < Start) Start = Focus;
        if (Focus >= Start + visible) Start = Focus - visible + 1;
        Start = Math.Clamp(Start, 0, Ids.Count - visible);
    }

    /// <summary>
    /// Where each column goes inside the workspace area. Columns left or right of the screen are not
    /// placed (Visible false): they wait in a hidden workspace until the row scrolls to them, so
    /// nothing shows through a see-through window and nothing reaches the neighbouring monitor.
    /// </summary>
    public List<(string Id, int X, int Width, bool Visible)> Place(int x, int width, int visible, int gap)
    {
        var result = new List<(string, int, int, bool)>();
        int n = Ids.Count;
        if (n == 0) return result;
        visible = Math.Clamp(visible, 1, n);
        Reveal(visible);
        int column = Math.Max(200, (width - gap * (visible - 1)) / visible);
        for (int i = 0; i < n; i++)
        {
            bool on = i >= Start && i < Start + visible;
            result.Add((Ids[i], on ? x + (i - Start) * (column + gap) : 0, column, on));
        }
        return result;
    }
}

public sealed class LayoutEngine
{
    static readonly LayoutMode[] Cycle = { LayoutMode.Dwindle, LayoutMode.Columns, LayoutMode.Scrolling };

    readonly GlazeIpc _ipc;
    readonly Func<LayoutMode> _default;
    readonly Action<string> _notify;
    readonly Func<UserSettings> _settings;
    readonly LayoutStore _store = LayoutStore.Load();
    /// <summary>Windows the floating mode floated (and will tile again).</summary>
    readonly ConcurrentDictionary<string, string> _floatedBy = new();
    /// <summary>Scrolling workspaces by name. Only touched while holding the gate.</summary>
    readonly Dictionary<string, ScrollStrip> _strips = new(StringComparer.OrdinalIgnoreCase);
    readonly SemaphoreSlim _busy;

    public LayoutEngine(GlazeIpc ipc, Func<LayoutMode> defaultMode, Action<string> notify, SemaphoreSlim? gate = null, Func<UserSettings>? settings = null)
    {
        _ipc = ipc;
        _default = defaultMode;
        _notify = notify;
        _busy = gate ?? new SemaphoreSlim(1, 1);
        _settings = settings ?? (() => new UserSettings());
    }

    public static readonly string[] Events = { "focus_changed", "window_managed", "window_unmanaged", "workspace_deactivated" };

    public LayoutMode ModeOf(string workspace) =>
        _store.Workspaces.TryGetValue(workspace, out var m) ? m : _default();

    public static LayoutMode Parse(string? text) => text?.ToLowerInvariant() switch
    {
        "columns" => LayoutMode.Columns,
        "scrolling" => LayoutMode.Scrolling,
        "floating" => LayoutMode.Floating,
        "manual" => LayoutMode.Manual,
        _ => LayoutMode.Dwindle,
    };

    public static string Name(LayoutMode m) => L.T("layouts." + m.ToString().ToLowerInvariant());

    int Visible => Math.Clamp(_settings().ScrollingColumns, 1, 3);

    int _holding;

    /// <summary>
    /// While a work mode waits for the windows it starts, new windows are left alone: one that opens
    /// on the wrong workspace is moved first and only then placed (<see cref="AdoptAsync"/>).
    /// Otherwise the layout of the workspace it left would place it there again.
    /// </summary>
    public IDisposable HoldNewWindows()
    {
        Interlocked.Increment(ref _holding);
        return new Hold(this);
    }

    sealed class Hold(LayoutEngine owner) : IDisposable
    {
        int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0) Interlocked.Decrement(ref owner._holding);
        }
    }

    /// <summary>Places a window held back by <see cref="HoldNewWindows"/>, as if it had just opened where it is now.</summary>
    public async Task AdoptAsync(string id, CancellationToken ct)
    {
        await _busy.WaitAsync(ct);
        try
        {
            var workspaces = await _ipc.QueryAsync("workspaces", ct);
            var window = GlazeTree.List(workspaces, "workspaces").SelectMany(GlazeTree.Windows).FirstOrDefault(w => GlazeTree.Str(w, "id") == id);
            if (window.ValueKind == JsonValueKind.Object) await PlaceLockedAsync("window_managed", window, id, workspaces, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Debug("Layout: " + ex.Message);
        }
        finally
        {
            _busy.Release();
        }
    }

    public async Task OnEventAsync(GlazeEvent e, CancellationToken ct)
    {
        if (e.Type == "workspace_deactivated")
        {
            // Never skipped: a layout meant only for the open workspace would stay otherwise.
            try
            {
                if (GlazeTree.Str(e.Data, "deactivatedName") is { } closed) await ClosedAsync(new[] { closed }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Debug("Layout: " + ex.Message);
            }
            return;
        }
        // Events come in bursts; if one is still being handled after half a second, this one is skipped.
        if (!await _busy.WaitAsync(500, ct)) return;
        try
        {
            if (e.Type == "window_unmanaged")
            {
                var gone = GlazeTree.Str(e.Data, "unmanagedId");
                foreach (var (owner, column) in _strips.ToList())
                {
                    if (gone == null || !column.Ids.Remove(gone)) continue;
                    if (column.Focus >= column.Ids.Count) column.Focus = Math.Max(0, column.Ids.Count - 1);
                    await ArrangeLockedAsync(owner, null, ct);
                }
                return;
            }

            var window = e.Type switch
            {
                "focus_changed" => e.Data.TryGetProperty("focusedContainer", out var f) ? f : default,
                "window_managed" => e.Data.TryGetProperty("managedWindow", out var w) ? w : default,
                _ => default,
            };
            if (window.ValueKind != JsonValueKind.Object || GlazeTree.Type(window) != "window") return;
            var id = GlazeTree.Str(window, "id");
            if (id == null) return;
            if (e.Type == "window_managed" && Volatile.Read(ref _holding) > 0) return;

            await PlaceLockedAsync(e.Type, window, id, await _ipc.QueryAsync("workspaces", ct), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Debug("Layout: " + ex.Message);
        }
        finally
        {
            _busy.Release();
        }
    }

    /// <summary>What the layout of the window's workspace does with a window that opened, moved in or got the focus. Caller holds the gate.</summary>
    async Task PlaceLockedAsync(string eventType, JsonElement window, string id, JsonElement workspaces, CancellationToken ct)
    {
        var ws = GlazeTree.WorkspaceOf(workspaces, id);
        var name = ws is { } w2 ? GlazeTree.Str(w2, "name") : null;
        if (name == null || !GlazeConfig.IsRegular(name)) return;

        // A new window next to one in full screen: that one goes back to where it was (its tile, or
        // floating), so the new one is not hidden behind it or lying over it.
        if (eventType == "window_managed" && _settings().NewWindowEndsFullscreen && GlazeTree.WindowState(window) == "tiling")
        {
            foreach (var other in GlazeTree.Windows(ws!.Value).Where(w => GlazeTree.WindowState(w) == "fullscreen" && GlazeTree.Str(w, "id") != id).ToList())
            {
                Log.Trace($"fullscreen: {GlazeTree.Str(other, "id")} leaves full screen for new window {id}", "layout");
                await _ipc.CommandAsync("toggle-fullscreen", ct, GlazeTree.Str(other, "id"));
            }
        }

        switch (ModeOf(name))
        {
            case LayoutMode.Dwindle when GlazeTree.WindowState(window) == "tiling":
                var width = GlazeTree.Int(window, "width");
                var height = GlazeTree.Int(window, "height");
                if (width > 0 && height > 0)
                    await _ipc.CommandAsync("set-tiling-direction " + (width >= height ? "horizontal" : "vertical"), ct, id);
                break;

            case LayoutMode.Columns:
                var wsId = GlazeTree.Str(ws!.Value, "id");
                if (wsId != null && GlazeTree.Str(ws.Value, "tilingDirection") == "vertical")
                {
                    await _ipc.CommandAsync("set-tiling-direction horizontal", ct, wsId);
                    // GlazeWM changes the direction but does not draw it until something else moves.
                    await _ipc.CommandAsync("wm-redraw", ct);
                }
                break;

            case LayoutMode.Scrolling when !GlazeTree.ShownOnTop(window):
                // A new window, one moved here, or a click on a column off the screen.
                var strip = Strip(name);
                var index = strip.Ids.IndexOf(id);
                // A dialog or a window floated by a rule is not a column; leave its focus alone.
                if (index < 0 && GlazeTree.WindowState(window) != "tiling") break;
                if (index >= 0 && GlazeTree.WindowState(window) == "floating")
                {
                    if (index == strip.Focus) break;
                    bool onScreen = index >= strip.Start && index < strip.Start + Visible;
                    strip.Focus = index;
                    if (onScreen) break;
                }
                await ArrangeLockedAsync(name, id, ct);
                break;

            case LayoutMode.Floating when eventType == "window_managed" && GlazeTree.WindowState(window) == "tiling":
                await _ipc.CommandAsync("set-floating --centered", ct, id);
                _floatedBy[id] = name;
                break;
        }
    }

    /// <summary>Win+Ctrl+J on the focused workspace: Dwindle → Columns → Scrolling → Dwindle.</summary>
    public async Task CycleAsync(CancellationToken ct)
    {
        var workspaces = await _ipc.QueryAsync("workspaces", ct);
        if (GlazeTree.FocusedWorkspace(workspaces) is not { } ws || GlazeTree.Str(ws, "name") is not { } name) return;
        int i = Array.IndexOf(Cycle, ModeOf(name));
        await SetAsync(name, Cycle[(i + 1) % Cycle.Length], ct);
    }

    /// <summary>The user's choice for a workspace (Win+Ctrl+J, Tiling page): it stays, with a notice.</summary>
    public async Task SetAsync(string workspace, LayoutMode mode, CancellationToken ct)
    {
        // Say it first: rearranging can take a moment, the notice should not wait for it.
        _notify(L.T("layouts.changed", ("workspace", workspace), ("layout", Name(mode))));
        await ApplyAsync(workspace, mode, untilClosed: false, ct);
    }

    /// <summary>
    /// A layout only while the workspace stays open (a work mode's shared screen), without a notice.
    /// When GlazeWM closes the workspace, the layout it had before comes back; choosing one by hand
    /// in the meantime keeps that one.
    /// </summary>
    public async Task UseUntilClosedAsync(string workspace, LayoutMode mode, CancellationToken ct)
    {
        if (ModeOf(workspace) == mode) return;
        await ApplyAsync(workspace, mode, untilClosed: true, ct);
    }

    /// <summary>After connecting: workspaces that closed while WinMarchy was not listening get their layout from before.</summary>
    public async Task ForgetClosedAsync(CancellationToken ct)
    {
        if (_store.UntilClosed.Count == 0) return;
        var open = GlazeTree.List(await _ipc.QueryAsync("workspaces", ct), "workspaces").Select(w => GlazeTree.Str(w, "name")).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        await ClosedAsync(_store.UntilClosed.Keys.Where(n => !open.Contains(n)).ToList(), ct);
    }

    async Task ClosedAsync(IReadOnlyCollection<string> names, CancellationToken ct)
    {
        if (names.Count == 0) return;
        await _busy.WaitAsync(ct);
        try
        {
            bool changed = false;
            foreach (var name in names)
            {
                if (!_store.UntilClosed.Remove(name, out var before)) continue;
                if (before is { } b && b != _default()) _store.Workspaces[name] = b;
                else _store.Workspaces.Remove(name);
                // A closed workspace has no windows, so no columns either.
                _strips.Remove(name);
                _store.Columns.Remove(name);
                Log.Trace($"layout: workspace {name} closed, back to {Name(ModeOf(name))}", "layout");
                changed = true;
            }
            if (changed) _store.Save();
        }
        finally
        {
            _busy.Release();
        }
    }

    async Task ApplyAsync(string workspace, LayoutMode mode, bool untilClosed, CancellationToken ct)
    {
        await _busy.WaitAsync(ct);
        try
        {
            var before = ModeOf(workspace);
            if (!untilClosed) _store.UntilClosed.Remove(workspace);
            else if (!_store.UntilClosed.ContainsKey(workspace))
                _store.UntilClosed[workspace] = _store.Workspaces.TryGetValue(workspace, out var chosen) ? chosen : null;
            if (mode == _default()) _store.Workspaces.Remove(workspace);
            else _store.Workspaces[workspace] = mode;
            _store.Save();

            var workspaces = await _ipc.QueryAsync("workspaces", ct);
            var ws = GlazeTree.List(workspaces, "workspaces").Cast<JsonElement?>().FirstOrDefault(w => GlazeTree.Str(w!.Value, "name") == workspace);
            if (ws is not { } w) return;

            if (before == LayoutMode.Floating && mode != LayoutMode.Floating)
            {
                foreach (var (id, owner) in _floatedBy.ToArray())
                {
                    if (owner != workspace) continue;
                    try { await _ipc.CommandAsync("set-tiling", ct, id); } catch { /* window closed */ }
                    _floatedBy.TryRemove(id, out _);
                }
            }
            if (before == LayoutMode.Scrolling && mode != LayoutMode.Scrolling)
            {
                // Columns waiting off the screen come back first, then all of them into the tiles.
                var staged = GlazeTree.WindowInfos(workspaces).Where(x => x.Workspace == GlazeConfig.StageWorkspace).Select(x => x.Id).ToHashSet();
                var ids = _strips.Remove(workspace, out var old) ? old.Ids : new List<string>();
                foreach (var id in ids)
                {
                    try
                    {
                        if (staged.Contains(id)) await _ipc.CommandAsync($"move --workspace {workspace}", ct, id);
                        await _ipc.CommandAsync("set-tiling", ct, id);
                    }
                    catch { /* window closed */ }
                }
                _store.Columns.Remove(workspace);
                _store.Save();
            }
            if (mode == LayoutMode.Floating)
            {
                foreach (var win in GlazeTree.TilingWindows(w).ToList())
                {
                    if (GlazeTree.Str(win, "id") is not { } id) continue;
                    await _ipc.CommandAsync("set-floating --centered", ct, id);
                    _floatedBy[id] = workspace;
                }
            }
            if (mode == LayoutMode.Scrolling)
            {
                _strips.Remove(workspace);
                await ArrangeLockedAsync(workspace, null, ct);
            }
            if (mode is LayoutMode.Dwindle or LayoutMode.Columns && GlazeTree.Bool(w, "isDisplayed"))
                await RelayoutAsync(workspace, mode, ct);
        }
        finally
        {
            _busy.Release();
        }
    }

    // ------------------------------------------------------------------ scrolling

    ScrollStrip Strip(string workspace)
    {
        if (!_strips.TryGetValue(workspace, out var strip)) _strips[workspace] = strip = new ScrollStrip();
        return strip;
    }

    /// <summary>The columns of a scrolling workspace, or null. Caller holds the gate.</summary>
    internal ScrollStrip? StripOf(string workspace) =>
        ModeOf(workspace) == LayoutMode.Scrolling && _strips.TryGetValue(workspace, out var s) ? s : null;

    /// <summary>A window leaves its scrolling workspace (moved away, into the scratchpad). Caller holds the gate.</summary>
    internal void Forget(string windowId)
    {
        foreach (var strip in _strips.Values)
            if (strip.Ids.Remove(windowId) && strip.Focus >= strip.Ids.Count) strip.Focus = Math.Max(0, strip.Ids.Count - 1);
    }

    /// <summary>The strip of a scrolling workspace, built first if WinMarchy just started. Caller holds the gate.</summary>
    async Task<ScrollStrip?> EnsureStripAsync(string workspace, CancellationToken ct)
    {
        if (ModeOf(workspace) != LayoutMode.Scrolling) return null;
        if (!_strips.ContainsKey(workspace)) await ArrangeLockedAsync(workspace, null, ct);
        return _strips.TryGetValue(workspace, out var s) ? s : null;
    }

    /// <summary>
    /// Win+Left/Right on a scrolling workspace. It starts from the window that has the focus in
    /// GlazeWM (<paramref name="focusedId"/>), not from the remembered column: a click or a skipped
    /// event may have moved it. Caller holds the gate.
    /// </summary>
    internal async Task<bool> ScrollFocusLockedAsync(string workspace, string direction, string? focusedId, CancellationToken ct)
    {
        if (direction is not ("left" or "right") || await EnsureStripAsync(workspace, ct) is not { } strip) return false;
        if (focusedId != null && strip.Ids.IndexOf(focusedId) is var at and >= 0) strip.Focus = at;
        var next = strip.Focus + (direction == "left" ? -1 : 1);
        if (next < 0 || next >= strip.Ids.Count)
        {
            Log.Trace($"scrolling {workspace}: column {strip.Focus + 1} of {strip.Ids.Count} is the last one {direction}", "layout");
            return true;
        }
        // Named, or the arrangement would take the focus back to the window GlazeWM still focuses.
        await ArrangeLockedAsync(workspace, strip.Ids[next], ct);
        return true;
    }

    /// <summary>Win+Shift+Left/Right on a scrolling workspace: the column changes places. Caller holds the gate.</summary>
    internal async Task<bool> ScrollSwapLockedAsync(string workspace, string direction, string? focusedId, CancellationToken ct)
    {
        if (direction is not ("left" or "right") || await EnsureStripAsync(workspace, ct) is not { } strip) return false;
        if (focusedId != null && strip.Ids.IndexOf(focusedId) is var at and >= 0) strip.Focus = at;
        var other = strip.Focus + (direction == "left" ? -1 : 1);
        if (other < 0 || other >= strip.Ids.Count) return true;
        var moving = strip.Ids[strip.Focus];
        (strip.Ids[strip.Focus], strip.Ids[other]) = (strip.Ids[other], moving);
        await ArrangeLockedAsync(workspace, moving, ct);
        return true;
    }

    /// <summary>
    /// Brings a scrolling workspace in line with GlazeWM and places its columns (and focuses the
    /// focused column, unless <paramref name="focus"/> is false): tiling windows that arrived join
    /// after the focused column, closed ones leave. Columns off the screen go into the hidden staging
    /// workspace, the ones coming into view come back from there. After a restart of WinMarchy the
    /// order comes from layouts.json. Caller holds the gate.
    /// </summary>
    internal async Task ArrangeLockedAsync(string workspace, string? focusId, CancellationToken ct, bool focus = true)
    {
        var monitors = await _ipc.QueryAsync("monitors", ct);
        var area = WorkspaceArea.All(monitors).FirstOrDefault(a => a.Name == workspace);
        if (area == null) return;
        var all = GlazeTree.WindowInfos(monitors);
        var strip = Strip(workspace);
        var known = _store.Columns.TryGetValue(workspace, out var saved) ? saved : new List<long>();
        // This workspace's windows, and its columns that wait in the staging workspace.
        var windows = all.Where(w => w.Workspace == workspace ||
                                     (w.Workspace == GlazeConfig.StageWorkspace && (strip.Ids.Contains(w.Id) || known.Contains(w.Handle)))).ToList();

        // Full screen (Win+F) keeps its column; it is just not placed until it comes back.
        var present = windows.Where(w => !w.OnTop && w.State is "tiling" or "floating" or "fullscreen").ToDictionary(w => w.Id);
        strip.Ids.RemoveAll(id => !present.ContainsKey(id));
        if (strip.Ids.Count == 0)
        {
            // Freshly started: the saved order, then floating windows as they stand on the screen.
            strip.Ids.AddRange(known.Select(h => present.Values.FirstOrDefault(w => w.Handle == h && w.State != "tiling")).OfType<WindowInfo>().Select(w => w.Id));
            strip.Ids.AddRange(present.Values.Where(w => w.Floating && w.Workspace == workspace && !strip.Ids.Contains(w.Id))
                .OrderBy(w => w.X).ThenBy(w => w.Y).Select(w => w.Id));
        }
        var arriving = present.Values.Where(w => w.State == "tiling" && !strip.Ids.Contains(w.Id)).OrderBy(w => w.X).ThenBy(w => w.Y).ToList();
        int insertAt = strip.Ids.Count == 0 ? 0 : Math.Clamp(strip.Focus + 1, 0, strip.Ids.Count);
        foreach (var w in arriving) strip.Ids.Insert(insertAt++, w.Id);
        // Tiling windows of the strip float: new ones, and any that Win+T put back into the tiles.
        foreach (var id in strip.Ids.Where(id => present[id].State == "tiling").ToList())
            await _ipc.CommandAsync("set-floating --centered=false --shown-on-top=false", ct, id);
        if (focusId != null && strip.Ids.IndexOf(focusId) is var fi and >= 0) strip.Focus = fi;
        else if (arriving.Count > 0) strip.Focus = insertAt - 1;
        else if (windows.FirstOrDefault(w => w.Focused) is { } focused && strip.Ids.IndexOf(focused.Id) is var ff and >= 0) strip.Focus = ff;
        _store.Columns[workspace] = strip.Ids.Select(id => present[id].Handle).ToList();
        _store.Save();
        if (strip.Ids.Count == 0 || !area.Displayed) return;

        int gap = (int)Math.Round(_settings().InnerGap * area.Scale);
        var placed = strip.Place(area.X, area.Width, Visible, gap);
        // First out of sight, then into place: never two columns on the same spot.
        foreach (var (id, _, _, visible) in placed)
            if (!visible && present[id].Workspace != GlazeConfig.StageWorkspace)
                await _ipc.CommandAsync($"move --workspace {GlazeConfig.StageWorkspace}", ct, id);
        foreach (var (id, x, width, visible) in placed)
        {
            if (!visible || present[id].State == "fullscreen") continue;
            if (present[id].Workspace != workspace) await _ipc.CommandAsync($"move --workspace {workspace}", ct, id);
            await _ipc.CommandAsync($"size --width {width}px --height {area.Height}px", ct, id);
            await _ipc.CommandAsync($"position --x-pos {x} --y-pos {area.Y}", ct, id);
        }
        Log.Trace($"scrolling {workspace}: {strip.Ids.Count} columns, focus {strip.Focus}, from {strip.Start}, {Visible} on screen", "layout");
        // Only when the user is on this workspace: focusing a window switches GlazeWM to it.
        if (focus && area.Focused) await _ipc.CommandAsync($"focus --container-id {strip.Ids[strip.Focus]}", ct);
    }

    /// <summary>Whether a window waiting in the staging workspace is a column of a scrolling workspace.</summary>
    internal bool IsWaitingColumn(long handle) =>
        _store.Columns.Any(c => ModeOf(c.Key) == LayoutMode.Scrolling && c.Value.Contains(handle));

    // ------------------------------------------------------------------ dwindle and columns

    /// <summary>
    /// GlazeWM only places new windows. So that a layout change shows at once, the windows of the
    /// workspace go out one by one (into a hidden workspace of their own, never the scratchpad) and
    /// come back in the new arrangement: Columns side by side in their old order, Dwindle each one
    /// halving the last.
    /// </summary>
    async Task RelayoutAsync(string workspace, LayoutMode mode, CancellationToken ct)
    {
        var data = await _ipc.QueryAsync("workspaces", ct);
        var ws = GlazeTree.List(data, "workspaces").FirstOrDefault(w => GlazeTree.Str(w, "name") == workspace);
        if (ws.ValueKind != JsonValueKind.Object || GlazeTree.Str(ws, "id") is not { } wsId) return;
        var ids = GlazeTree.TilingWindows(ws).Select(w => GlazeTree.Str(w, "id")).OfType<string>().ToList();
        if (ids.Count < 2) return;
        var focused = GlazeTree.Windows(ws).Where(w => GlazeTree.Bool(w, "hasFocus")).Select(w => GlazeTree.Str(w, "id")).FirstOrDefault();

        var first = ids[0];
        var staged = new List<string>();
        try
        {
            foreach (var id in ids.Skip(1))
            {
                await _ipc.CommandAsync($"move --workspace {GlazeConfig.StageWorkspace}", ct, id);
                staged.Add(id);
            }
            await _ipc.CommandAsync($"focus --container-id {first}", ct);
            if (mode == LayoutMode.Columns)
            {
                // One row: the workspace horizontal, the first window directly in it. Each window
                // returns right after the first, so the last one goes back first.
                await _ipc.CommandAsync("set-tiling-direction horizontal", ct, wsId);
                await _ipc.CommandAsync("set-tiling-direction horizontal", ct, first);
                for (int i = staged.Count - 1; i >= 0; i--)
                {
                    await _ipc.CommandAsync($"move --workspace {workspace}", ct, staged[i]);
                    staged.RemoveAt(i);
                }
            }
            else
            {
                var last = first;
                while (staged.Count > 0)
                {
                    var shape = await WindowAsync(last, ct);
                    if (shape is { } s && GlazeTree.Int(s, "width") > 0)
                        await _ipc.CommandAsync("set-tiling-direction " + (GlazeTree.Int(s, "width") >= GlazeTree.Int(s, "height") ? "horizontal" : "vertical"), ct, last);
                    var next = staged[0];
                    await _ipc.CommandAsync($"move --workspace {workspace}", ct, next);
                    staged.RemoveAt(0);
                    await _ipc.CommandAsync($"focus --container-id {next}", ct);
                    last = next;
                }
            }
        }
        finally
        {
            // Whatever went wrong, no window stays behind in the hidden workspace.
            foreach (var id in staged)
            {
                try { await _ipc.CommandAsync($"move --workspace {workspace}", CancellationToken.None, id); } catch { /* closed */ }
            }
            if (focused != null)
            {
                try { await _ipc.CommandAsync($"focus --container-id {focused}", CancellationToken.None); } catch { }
            }
            try { await _ipc.CommandAsync("wm-redraw", CancellationToken.None); } catch { }
        }
    }

    async Task<JsonElement?> WindowAsync(string id, CancellationToken ct)
    {
        var data = await _ipc.QueryAsync("windows", ct);
        foreach (var w in GlazeTree.List(data, "windows"))
            if (GlazeTree.Str(w, "id") == id) return w;
        return null;
    }
}

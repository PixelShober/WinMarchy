using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace WinMarchy.Core.GlazeWm;

/// <summary>
/// Omarchy's workspace layouts as far as GlazeWM can follow. Win+Alt+L cycles Dwindle → Columns →
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
    /// Where each column goes inside the workspace area. Columns left or right of the screen wait
    /// behind the outermost visible one, a narrow strip of them showing at the edge.
    /// </summary>
    public List<(string Id, int X, int Width, bool Visible)> Place(int x, int width, int visible, int gap, int peek)
    {
        var result = new List<(string, int, int, bool)>();
        int n = Ids.Count;
        if (n == 0) return result;
        visible = Math.Clamp(visible, 1, n);
        Reveal(visible);
        int peekLeft = Start > 0 ? peek : 0, peekRight = Start + visible < n ? peek : 0;
        int column = Math.Max(200, (width - peekLeft - peekRight - gap * (visible - 1)) / visible);
        for (int i = 0; i < n; i++)
        {
            if (i < Start) result.Add((Ids[i], x, column, false));
            else if (i >= Start + visible) result.Add((Ids[i], x + width - column, column, false));
            else result.Add((Ids[i], x + peekLeft + (i - Start) * (column + gap), column, true));
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

    public static readonly string[] Events = { "focus_changed", "window_managed", "window_unmanaged" };

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

    public async Task OnEventAsync(GlazeEvent e, CancellationToken ct)
    {
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

            var workspaces = await _ipc.QueryAsync("workspaces", ct);
            var ws = GlazeTree.WorkspaceOf(workspaces, id);
            var name = ws is { } w2 ? GlazeTree.Str(w2, "name") : null;
            if (name == null || !GlazeConfig.IsRegular(name)) return;

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
                        await _ipc.CommandAsync("set-tiling-direction horizontal", ct, wsId);
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

                case LayoutMode.Floating when e.Type == "window_managed" && GlazeTree.WindowState(window) == "tiling":
                    await _ipc.CommandAsync("set-floating --centered", ct, id);
                    _floatedBy[id] = name;
                    break;
            }
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

    /// <summary>Win+Alt+L on the focused workspace: Dwindle → Columns → Scrolling → Dwindle.</summary>
    public async Task CycleAsync(CancellationToken ct)
    {
        var workspaces = await _ipc.QueryAsync("workspaces", ct);
        if (GlazeTree.FocusedWorkspace(workspaces) is not { } ws || GlazeTree.Str(ws, "name") is not { } name) return;
        int i = Array.IndexOf(Cycle, ModeOf(name));
        await SetAsync(name, Cycle[(i + 1) % Cycle.Length], ct);
    }

    public async Task SetAsync(string workspace, LayoutMode mode, CancellationToken ct)
    {
        // Say it first: rearranging can take a moment, the notice should not wait for it.
        _notify(L.T("layouts.changed", ("workspace", workspace), ("layout", Name(mode))));
        await _busy.WaitAsync(ct);
        try
        {
            var before = ModeOf(workspace);
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
            if (before == LayoutMode.Scrolling && mode != LayoutMode.Scrolling && _strips.Remove(workspace, out var old))
            {
                foreach (var id in old.Ids)
                {
                    try { await _ipc.CommandAsync("set-tiling", ct, id); } catch { /* window closed */ }
                }
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

    /// <summary>Win+Left/Right on a scrolling workspace. Caller holds the gate.</summary>
    internal async Task<bool> ScrollFocusLockedAsync(string workspace, string direction, CancellationToken ct)
    {
        if (direction is not ("left" or "right") || await EnsureStripAsync(workspace, ct) is not { } strip) return false;
        var next = strip.Focus + (direction == "left" ? -1 : 1);
        if (next < 0 || next >= strip.Ids.Count) return true;
        strip.Focus = next;
        await ArrangeLockedAsync(workspace, null, ct);
        return true;
    }

    /// <summary>Win+Shift+Left/Right on a scrolling workspace: the column changes places. Caller holds the gate.</summary>
    internal async Task<bool> ScrollSwapLockedAsync(string workspace, string direction, CancellationToken ct)
    {
        if (direction is not ("left" or "right") || await EnsureStripAsync(workspace, ct) is not { } strip) return false;
        var other = strip.Focus + (direction == "left" ? -1 : 1);
        if (other < 0 || other >= strip.Ids.Count) return true;
        (strip.Ids[strip.Focus], strip.Ids[other]) = (strip.Ids[other], strip.Ids[strip.Focus]);
        strip.Focus = other;
        await ArrangeLockedAsync(workspace, null, ct);
        return true;
    }

    /// <summary>
    /// Brings a scrolling workspace in line with GlazeWM and places its columns (and focuses the
    /// focused column, unless <paramref name="focus"/> is false): tiling windows that
    /// arrived join (after the focused column), closed ones leave. After a restart of WinMarchy the
    /// floating windows are taken over in their order on the screen. Caller holds the gate.
    /// </summary>
    internal async Task ArrangeLockedAsync(string workspace, string? focusId, CancellationToken ct, bool focus = true)
    {
        var monitors = await _ipc.QueryAsync("monitors", ct);
        var area = WorkspaceArea.All(monitors).FirstOrDefault(a => a.Name == workspace);
        if (area == null) return;
        var windows = GlazeTree.WindowInfos(monitors).Where(w => w.Workspace == workspace).ToList();
        var strip = Strip(workspace);

        // Full screen (Win+F) keeps its column; it is just not placed until it comes back.
        var present = windows.Where(w => !w.OnTop && w.State is "tiling" or "floating" or "fullscreen").ToDictionary(w => w.Id, w => w.State);
        strip.Ids.RemoveAll(id => !present.ContainsKey(id));
        if (strip.Ids.Count == 0)
            strip.Ids.AddRange(windows.Where(w => w.Floating && !w.OnTop).OrderBy(w => w.X).ThenBy(w => w.Y).Select(w => w.Id));
        var arriving = windows.Where(w => w.State == "tiling" && !strip.Ids.Contains(w.Id)).OrderBy(w => w.X).ThenBy(w => w.Y).ToList();
        int insertAt = strip.Ids.Count == 0 ? 0 : Math.Clamp(strip.Focus + 1, 0, strip.Ids.Count);
        foreach (var w in arriving) strip.Ids.Insert(insertAt++, w.Id);
        // Tiling windows of the strip float: new ones, and any that Win+T put back into the tiles.
        foreach (var id in strip.Ids.Where(id => present[id] == "tiling"))
            await _ipc.CommandAsync("set-floating --centered=false --shown-on-top=false", ct, id);
        if (focusId != null && strip.Ids.IndexOf(focusId) is var fi and >= 0) strip.Focus = fi;
        else if (arriving.Count > 0) strip.Focus = insertAt - 1;
        else if (windows.FirstOrDefault(w => w.Focused) is { } focused && strip.Ids.IndexOf(focused.Id) is var ff and >= 0) strip.Focus = ff;
        if (strip.Ids.Count == 0 || !area.Displayed) return;

        int gap = (int)Math.Round(_settings().InnerGap * area.Scale);
        int peek = (int)Math.Round(14 * area.Scale);
        var placed = strip.Place(area.X, area.Width, Visible, gap, peek);
        foreach (var (id, x, width, _) in placed)
        {
            if (present[id] == "fullscreen") continue;
            await _ipc.CommandAsync($"size --width {width}px --height {area.Height}px", ct, id);
            await _ipc.CommandAsync($"position --x-pos {x} --y-pos {area.Y}", ct, id);
        }
        // Only when the user is on this workspace: focusing a window switches GlazeWM to it.
        if (focus && area.Focused) await _ipc.CommandAsync($"focus --container-id {strip.Ids[strip.Focus]}", ct);

        // Columns off the screen go behind the visible column at their edge, so only their strip shows.
        var handles = windows.ToDictionary(w => w.Id, w => w.Handle);
        var shown = placed.Where(p => p.Visible).ToList();
        if (shown.Count == 0) return;
        await Task.Delay(60, ct);
        foreach (var (id, x, _, visible) in placed.Where(p => !p.Visible))
        {
            var edge = x == area.X ? shown[0].Id : shown[^1].Id;
            if (handles.TryGetValue(id, out var h) && handles.TryGetValue(edge, out var above)) PlaceBelow(h, above);
        }
    }

    static void PlaceBelow(long window, long above)
    {
        if (!OperatingSystem.IsWindows() || window == 0 || above == 0) return;
        SetWindowPos(new IntPtr(window), new IntPtr(above), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200;

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

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

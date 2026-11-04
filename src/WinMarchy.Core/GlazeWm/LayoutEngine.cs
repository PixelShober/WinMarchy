using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WinMarchy.Core.GlazeWm;

/// <summary>
/// Omarchy's layouts as far as GlazeWM can follow. Each screen has one (Tiling page; Win+Ctrl+J
/// cycles that of the screen with the focus), and every workspace shown on it follows. A work mode
/// can give a workspace its own layout for as long as it is open. Single windows leave the layout
/// with Win+T: they float above the tiles, and Win+T puts them back.
/// <list type="bullet">
/// <item>Dwindle: every focused window splits along its longer side, so the next window takes half of it.</item>
/// <item>Columns: new windows line up side by side.</item>
/// <item>Scrolling: a row of columns wider than the screen, as in Hyprland's scrolling layout. GlazeWM
/// cannot tile wider than the screen, so WinMarchy places these windows itself (they float).</item>
/// </list>
/// </summary>
public enum LayoutMode { Dwindle, Columns, Scrolling }

public sealed class LayoutStore
{
    /// <summary>The layout each workspace's windows are arranged in now. Missing: as GlazeWM tiles them by itself.</summary>
    public Dictionary<string, LayoutMode> Workspaces { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Scrolling workspaces: their columns left to right, by window handle (survives a restart of WinMarchy).</summary>
    public Dictionary<string, List<long>> Columns { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Workspaces with a layout of their own while they are open (a work mode's shared screen),
    /// whatever their screen has.
    /// </summary>
    public Dictionary<string, LayoutMode> Overrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Workspaces in the floating layout of older versions; that layout is gone, their windows are tiled once.</summary>
    [JsonIgnore]
    public HashSet<string> FormerlyFloating { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static LayoutStore Load()
    {
        try
        {
            if (File.Exists(Paths.LayoutsFile)) return Parse(File.ReadAllText(Paths.LayoutsFile));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn("layouts.json: " + ex.Message);
        }
        return new LayoutStore();
    }

    /// <summary>
    /// Reads layouts.json, also as older versions wrote it: with the floating and manual layouts, and
    /// with "untilClosed" (the layout from before a work mode; the one it used is in "workspaces").
    /// </summary>
    public static LayoutStore Parse(string json)
    {
        var store = new LayoutStore();
        if (JsonNode.Parse(json) is not JsonObject root) return store;
        foreach (var (name, mode) in Modes(root["workspaces"]))
        {
            if (LayoutEngine.TryParse(mode, out var m)) store.Workspaces[name] = m;
            else if (string.Equals(mode, "floating", StringComparison.OrdinalIgnoreCase)) store.FormerlyFloating.Add(name);
        }
        foreach (var (name, mode) in Modes(root["overrides"]))
            if (LayoutEngine.TryParse(mode, out var m)) store.Overrides[name] = m;
        if (root["untilClosed"] is JsonObject untilClosed)
            foreach (var (name, _) in untilClosed)
                store.Overrides[name] = store.Workspaces.TryGetValue(name, out var used) ? used : LayoutMode.Dwindle;
        if (root["columns"] is JsonObject columns)
        {
            foreach (var (name, list) in columns)
            {
                if (list is not JsonArray handles) continue;
                store.Columns[name] = handles.Select(h => h is JsonValue v && v.TryGetValue(out long l) ? l : 0).Where(l => l != 0).ToList();
            }
        }
        return store;
    }

    static IEnumerable<(string Name, string? Mode)> Modes(JsonNode? node) =>
        node is JsonObject o
            ? o.Select(p => (p.Key, p.Value is JsonValue v && v.TryGetValue(out string? text) ? text : null))
            : Enumerable.Empty<(string, string?)>();

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
    readonly Action<string> _notify;
    readonly Func<UserSettings> _settings;
    readonly Action<int, LayoutMode> _chooseScreen;
    readonly LayoutStore _store = LayoutStore.Load();
    /// <summary>Scrolling workspaces by name. Only touched while holding the gate.</summary>
    readonly Dictionary<string, ScrollStrip> _strips = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The screen each workspace was on at the last look at the monitors.</summary>
    Dictionary<string, int> _screenOf = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Windows that left full screen for a new window, by id: their workspace, whether it was the
    /// "maximized" full screen, and the windows that came since. Only touched while holding the gate.
    /// </summary>
    readonly Dictionary<string, (string Workspace, bool Maximized, HashSet<string> Newcomers)> _yielded = new();
    readonly SemaphoreSlim _busy;

    /// <param name="chooseScreen">Saves a screen's layout chosen with Win+Ctrl+J into the settings.</param>
    public LayoutEngine(GlazeIpc ipc, Action<string> notify, SemaphoreSlim? gate = null, Func<UserSettings>? settings = null, Action<int, LayoutMode>? chooseScreen = null)
    {
        _ipc = ipc;
        _notify = notify;
        _busy = gate ?? new SemaphoreSlim(1, 1);
        var own = new UserSettings();
        _settings = settings ?? (() => own);
        _chooseScreen = chooseScreen ?? ((screen, mode) => own.SetScreenLayout(screen, mode));
    }

    public static readonly string[] Events = { "focus_changed", "window_managed", "window_unmanaged", "workspace_deactivated" };

    /// <summary>The layout a workspace should have: a work mode's while it is open, else its screen's.</summary>
    public LayoutMode ModeOf(string workspace) =>
        _store.Overrides.TryGetValue(workspace, out var own) ? own : _settings().ScreenLayoutOf(_screenOf.TryGetValue(workspace, out var screen) ? screen : 0);

    /// <summary>The layout a workspace's windows are in now; null: as GlazeWM tiles them by itself.</summary>
    LayoutMode? Arranged(string workspace) => _store.Workspaces.TryGetValue(workspace, out var m) ? m : null;

    /// <summary>"dwindle", "columns" and "scrolling"; anything else (also the floating and manual layouts of older versions) is Dwindle.</summary>
    public static LayoutMode Parse(string? text) => TryParse(text, out var mode) ? mode : LayoutMode.Dwindle;

    public static bool TryParse(string? text, out LayoutMode mode)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "dwindle": mode = LayoutMode.Dwindle; return true;
            case "columns": mode = LayoutMode.Columns; return true;
            case "scrolling": mode = LayoutMode.Scrolling; return true;
            default: mode = LayoutMode.Dwindle; return false;
        }
    }

    /// <summary>The layout's name in settings.json: "dwindle", "columns" or "scrolling".</summary>
    public static string Id(LayoutMode mode) => mode.ToString().ToLowerInvariant();

    public static string Name(LayoutMode m) => L.T("layouts." + Id(m));

    /// <summary>The next layout of Win+Ctrl+J: Dwindle → Columns → Scrolling → Dwindle.</summary>
    public static LayoutMode Next(LayoutMode mode) => Cycle[(Array.IndexOf(Cycle, mode) + 1) % Cycle.Length];

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
            var monitors = await _ipc.QueryAsync("monitors", ct);
            var window = GlazeTree.Workspaces(monitors).SelectMany(GlazeTree.Windows).FirstOrDefault(w => GlazeTree.Str(w, "id") == id);
            if (window.ValueKind == JsonValueKind.Object) await PlaceLockedAsync("window_managed", window, id, monitors, ct);
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
                if (gone != null) await GiveFullscreenBackLockedAsync(gone, ct);
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

            await PlaceLockedAsync(e.Type, window, id, await _ipc.QueryAsync("monitors", ct), ct);
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

    /// <summary>
    /// What the layout of the window's workspace does with a window that opened, moved in or got the
    /// focus. A workspace that is shown first gets the layout of its screen, if its windows are still
    /// in another one (the screen's layout changed, or the workspace came from another screen).
    /// Caller holds the gate.
    /// </summary>
    async Task PlaceLockedAsync(string eventType, JsonElement window, string id, JsonElement monitors, CancellationToken ct)
    {
        var ws = GlazeTree.WorkspaceOf(monitors, id);
        var name = ws is { } w2 ? GlazeTree.Str(w2, "name") : null;
        if (name == null || !GlazeConfig.IsRegular(name)) return;
        var mode = await ReconcileLockedAsync(name, monitors, ct);

        // A new window next to one in full screen: that one goes back to where it was (its tile, or
        // floating), so the new one is not hidden behind it or lying over it. Once the new window is
        // closed again, the other gets its full screen back (GiveFullscreenBackLockedAsync).
        if (eventType == "window_managed" && GlazeTree.WindowState(window) == "tiling")
        {
            foreach (var waiting in _yielded.Values.Where(y => y.Workspace == name)) waiting.Newcomers.Add(id);
            if (_settings().NewWindowEndsFullscreen)
            {
                foreach (var other in GlazeTree.Windows(ws!.Value).Where(w => GlazeTree.WindowState(w) == "fullscreen" && GlazeTree.Str(w, "id") != id).ToList())
                {
                    if (GlazeTree.Str(other, "id") is not { } otherId) continue;
                    Log.Trace($"fullscreen: {otherId} ({GlazeTree.Str(other, "processName")}) leaves full screen for new window {id} ({GlazeTree.Str(window, "processName")})", "layout");
                    await _ipc.CommandAsync("toggle-fullscreen", ct, otherId);
                    if (!_yielded.TryGetValue(otherId, out var yielded))
                        _yielded[otherId] = yielded = (name, other.TryGetProperty("state", out var st) && GlazeTree.Bool(st, "maximized"), new HashSet<string>());
                    yielded.Newcomers.Add(id);
                }
            }
        }

        switch (mode)
        {
            case LayoutMode.Dwindle when GlazeTree.WindowState(window) == "tiling":
                await DwindleSplitAsync(window, ct);
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
        }
    }

    /// <summary>
    /// A window closed. If it was the last of those that came since a window left full screen for
    /// them (a dialog, a pop-up, a window opened for a moment), that window gets its full screen back:
    /// otherwise it would stay in half the screen. Not if it moved to another workspace or another
    /// window is in full screen there now. Caller holds the gate.
    /// </summary>
    async Task GiveFullscreenBackLockedAsync(string gone, CancellationToken ct)
    {
        // The window that had left full screen closed itself.
        if (_yielded.Remove(gone)) return;
        var back = new List<string>();
        foreach (var (id, yielded) in _yielded)
            if (yielded.Newcomers.Remove(gone) && yielded.Newcomers.Count == 0) back.Add(id);
        if (back.Count == 0) return;
        var workspaces = await _ipc.QueryAsync("workspaces", ct);
        foreach (var id in back)
        {
            var (workspace, maximized, _) = _yielded[id];
            _yielded.Remove(id);
            if (GlazeTree.WorkspaceOf(workspaces, id) is not { } ws || GlazeTree.Str(ws, "name") != workspace) continue;
            var windows = GlazeTree.Windows(ws).ToList();
            if (windows.Any(w => GlazeTree.WindowState(w) == "fullscreen")) continue;
            if (windows.Any(w => GlazeTree.Str(w, "id") == id && GlazeTree.WindowState(w) == "minimized")) continue;
            Log.Trace($"fullscreen: {id} gets its full screen back, the windows that came for it are closed", "layout");
            await _ipc.CommandAsync("set-fullscreen" + (maximized ? " --maximized" : ""), ct, id);
        }
    }

    /// <summary>Dwindle: the window's split turns along its longer side, so the next window takes half of it.</summary>
    async Task DwindleSplitAsync(JsonElement window, CancellationToken ct)
    {
        var width = GlazeTree.Int(window, "width");
        var height = GlazeTree.Int(window, "height");
        if (width > 0 && height > 0 && GlazeTree.Str(window, "id") is { } id)
            await _ipc.CommandAsync("set-tiling-direction " + (width >= height ? "horizontal" : "vertical"), ct, id);
    }

    // ------------------------------------------------------------------ screens

    /// <summary>
    /// Brings a shown workspace's windows into the layout it should have now (<see cref="ModeOf"/>),
    /// if they are arranged in another. A hidden workspace waits until it is shown: rearranging means
    /// focusing its windows, and that would switch to it. Returns the layout the windows follow now.
    /// Caller holds the gate.
    /// </summary>
    async Task<LayoutMode> ReconcileLockedAsync(string workspace, JsonElement monitors, CancellationToken ct)
    {
        _screenOf = GlazeTree.ScreenOfWorkspaces(monitors);
        var wanted = ModeOf(workspace);
        var now = Arranged(workspace);
        if (now == wanted) return wanted;
        var area = WorkspaceArea.All(monitors).FirstOrDefault(a => a.Name == workspace);
        // Hidden: its windows stay as they are, so they are placed as they are (GlazeWM's tiles split like Dwindle).
        if (area is not { Displayed: true }) return now ?? LayoutMode.Dwindle;

        Log.Trace($"layout: workspace {workspace} from {(now is { } n ? Id(n) : "GlazeWM's tiles")} to {Id(wanted)}", "layout");
        _store.Workspaces[workspace] = wanted;
        _store.Save();
        if (now == LayoutMode.Scrolling)
        {
            // Columns waiting off the screen come back first, then all of them into the tiles.
            foreach (var (id, staged) in ColumnsOf(workspace, monitors))
            {
                try
                {
                    if (staged) await _ipc.CommandAsync($"move --workspace {workspace}", ct, id);
                    await _ipc.CommandAsync("set-tiling", ct, id);
                }
                catch (WinMarchyException) { /* window closed meanwhile */ }
            }
            _strips.Remove(workspace);
            _store.Columns.Remove(workspace);
            _store.Save();
        }
        if (wanted == LayoutMode.Scrolling)
        {
            _strips.Remove(workspace);
            await ArrangeLockedAsync(workspace, null, ct);
        }
        // Windows GlazeWM tiled by itself stay where they are; a layout WinMarchy had set is redone.
        else if (now != null) await RelayoutAsync(workspace, wanted, ct);
        return wanted;
    }

    /// <summary>
    /// The columns of a scrolling workspace and whether each waits in the staging workspace: the strip
    /// if WinMarchy has it in memory, else the windows layouts.json knows by handle.
    /// </summary>
    List<(string Id, bool Staged)> ColumnsOf(string workspace, JsonElement monitors)
    {
        var all = GlazeTree.WindowInfos(monitors);
        var known = _store.Columns.TryGetValue(workspace, out var saved) ? saved : new List<long>();
        var ids = _strips.TryGetValue(workspace, out var strip) ? strip.Ids.ToHashSet() : new HashSet<string>();
        return all.Where(w => (w.Workspace == workspace || w.Workspace == GlazeConfig.StageWorkspace) && !w.OnTop &&
                              (ids.Contains(w.Id) || known.Contains(w.Handle)))
            .Select(w => (w.Id, w.Workspace == GlazeConfig.StageWorkspace)).ToList();
    }

    /// <summary>
    /// Win+Ctrl+J: the screen with the focus goes to its next layout (Dwindle → Columns → Scrolling),
    /// and every workspace on it follows. A work mode's own layout there ends.
    /// </summary>
    public async Task CycleAsync(CancellationToken ct)
    {
        var monitors = await _ipc.QueryAsync("monitors", ct);
        var list = GlazeTree.List(monitors, "monitors").ToList();
        int screen = Math.Max(0, list.FindIndex(m => GlazeTree.Bool(m, "hasFocus")));
        await SetScreenAsync(screen, Next(_settings().ScreenLayoutOf(screen)), list.Count, ct);
    }

    /// <summary>"do:layout scrolling": the screen with the focus gets that layout.</summary>
    public async Task SetFocusedScreenAsync(LayoutMode mode, CancellationToken ct)
    {
        var list = GlazeTree.List(await _ipc.QueryAsync("monitors", ct), "monitors").ToList();
        await SetScreenAsync(Math.Max(0, list.FindIndex(m => GlazeTree.Bool(m, "hasFocus"))), mode, list.Count, ct);
    }

    async Task SetScreenAsync(int screen, LayoutMode mode, int screens, CancellationToken ct)
    {
        _chooseScreen(screen, mode);
        // Say it first: rearranging can take a moment, the notice should not wait for it.
        _notify(screens > 1 ? L.T("layouts.screenChanged", ("screen", screen + 1), ("layout", Name(mode))) : L.T("layouts.changed", ("layout", Name(mode))));
        await ApplyScreensAsync(ct, endOverridesOn: screen);
    }

    /// <summary>
    /// After the screens' layouts changed (Win+Ctrl+J, Tiling page): every shown workspace takes its
    /// screen's layout now, the hidden ones when they are shown.
    /// </summary>
    /// <param name="endOverridesOn">A screen whose workspaces lose a work mode's own layout: the user chose one for the screen.</param>
    public async Task ApplyScreensAsync(CancellationToken ct, int? endOverridesOn = null)
    {
        await _busy.WaitAsync(ct);
        try
        {
            var monitors = await _ipc.QueryAsync("monitors", ct);
            _screenOf = GlazeTree.ScreenOfWorkspaces(monitors);
            if (endOverridesOn is { } screen)
            {
                foreach (var name in _store.Overrides.Keys.Where(n => _screenOf.TryGetValue(n, out var s) && s == screen).ToList())
                    _store.Overrides.Remove(name);
                _store.Save();
            }
            foreach (var area in WorkspaceArea.All(monitors).Where(a => a.Displayed && GlazeConfig.IsRegular(a.Name)).ToList())
                await ReconcileLockedAsync(area.Name, monitors, ct);
        }
        finally
        {
            _busy.Release();
        }
    }

    /// <summary>
    /// A layout only while the workspace stays open (a work mode's shared screen), without a notice.
    /// When GlazeWM closes the workspace, the override goes; Win+Ctrl+J on its screen ends it too.
    /// </summary>
    public async Task UseWhileOpenAsync(string workspace, LayoutMode mode, CancellationToken ct)
    {
        await _busy.WaitAsync(ct);
        try
        {
            // A workspace that just opened: which screen it is on is only known from a fresh look.
            var monitors = await _ipc.QueryAsync("monitors", ct);
            _screenOf = GlazeTree.ScreenOfWorkspaces(monitors);
            if (ModeOf(workspace) == mode) return;
            _store.Overrides[workspace] = mode;
            _store.Save();
            await ReconcileLockedAsync(workspace, monitors, ct);
        }
        finally
        {
            _busy.Release();
        }
    }

    /// <summary>
    /// After connecting: what layouts.json says about workspaces that closed meanwhile goes, and the
    /// windows of workspaces in the floating layout of older versions come back into the tiles.
    /// </summary>
    public async Task ForgetClosedAsync(CancellationToken ct)
    {
        var monitors = await _ipc.QueryAsync("monitors", ct);
        _screenOf = GlazeTree.ScreenOfWorkspaces(monitors);
        var open = _screenOf.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        await ClosedAsync(_store.Workspaces.Keys.Concat(_store.Overrides.Keys).Concat(_store.Columns.Keys).Where(n => !open.Contains(n)).Distinct().ToList(), ct);
        if (_store.FormerlyFloating.Count == 0) return;
        await _busy.WaitAsync(ct);
        try
        {
            foreach (var w in GlazeTree.WindowInfos(monitors).Where(w => _store.FormerlyFloating.Contains(w.Workspace) && w.Floating && !w.OnTop))
            {
                try { await _ipc.CommandAsync("set-tiling", ct, w.Id); }
                catch (WinMarchyException) { /* closed meanwhile */ }
            }
            Log.Info(L.T("layouts.floatingGone", ("workspaces", string.Join(", ", _store.FormerlyFloating))));
            _store.FormerlyFloating.Clear();
            _store.Save();
        }
        finally
        {
            _busy.Release();
        }
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
                // A closed workspace has no windows: no arrangement, no columns, and a work mode's layout ends.
                changed |= _store.Overrides.Remove(name) | _store.Workspaces.Remove(name) | _store.Columns.Remove(name);
                _strips.Remove(name);
                foreach (var id in _yielded.Where(y => y.Value.Workspace == name).Select(y => y.Key).ToList()) _yielded.Remove(id);
                Log.Trace($"layout: workspace {name} closed", "layout");
            }
            if (changed) _store.Save();
        }
        finally
        {
            _busy.Release();
        }
    }

    // ------------------------------------------------------------------ single windows (Win+T)

    /// <summary>
    /// Win+T on a tile or a column: the window leaves the layout and floats above the tiles, centred
    /// at a comfortable size (<paramref name="corner"/>: small, bottom right, as Win+O pops it out).
    /// The others close the gap. Caller holds the gate.
    /// </summary>
    internal async Task FloatLockedAsync(WindowInfo window, bool corner, JsonElement monitors, CancellationToken ct)
    {
        var area = WorkspaceArea.All(monitors).FirstOrDefault(a => a.Name == window.Workspace);
        bool column = _strips.TryGetValue(window.Workspace, out var strip) && strip.Ids.Contains(window.Id);
        if (column) Forget(window.Id);
        if (window.State == "fullscreen") await _ipc.CommandAsync("toggle-fullscreen", ct, window.Id);
        await _ipc.CommandAsync("set-floating --shown-on-top --centered=false", ct, window.Id);
        if (area != null)
        {
            var (x, y, w, h) = FloatRect(area, window.Width, window.Height, corner, (int)Math.Round(_settings().InnerGap * area.Scale));
            await _ipc.CommandAsync($"size --width {w}px --height {h}px", ct, window.Id);
            await _ipc.CommandAsync($"position --x-pos {x} --y-pos {y}", ct, window.Id);
        }
        if (column) await ArrangeLockedAsync(window.Workspace, null, ct, focus: false);
        await _ipc.CommandAsync($"focus --container-id {window.Id}", ct);
        Log.Trace($"float: {window.Id} leaves the {(column ? "columns" : "tiles")} of {window.Workspace}", "layout");
    }

    /// <summary>
    /// Where a window floats: centred, as wide as its tile was but at least 40 % and at most 70 % of
    /// the screen (height: 50 to 80 %); in the corner a third of the screen, bottom right.
    /// </summary>
    public static (int X, int Y, int Width, int Height) FloatRect(WorkspaceArea area, int width, int height, bool corner, int gap)
    {
        if (corner)
        {
            int cw = area.Width / 3, ch = area.Height / 3;
            return (area.X + area.Width - cw - gap, area.Y + area.Height - ch - gap, cw, ch);
        }
        int w = Math.Clamp(width, area.Width * 2 / 5, area.Width * 7 / 10);
        int h = Math.Clamp(height, area.Height / 2, area.Height * 4 / 5);
        return (area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
    }

    /// <summary>
    /// Win+T on a floating window: back into the layout of its workspace. Dwindle splits the tile that
    /// had the focus last along its longer side (or GlazeWM puts the window back where it was, if that
    /// spot is still there); Columns adds a column; Scrolling adds a column after the focused one.
    /// Caller holds the gate.
    /// </summary>
    internal async Task TileLockedAsync(WindowInfo window, JsonElement monitors, CancellationToken ct)
    {
        var mode = await ReconcileLockedAsync(window.Workspace, monitors, ct);
        var ws = GlazeTree.Workspaces(monitors).Cast<JsonElement?>().FirstOrDefault(w => GlazeTree.Str(w!.Value, "name") == window.Workspace);
        // GlazeWM puts the window next to the tile that had the focus last; turned along its longer side, the window takes half of it.
        if (mode == LayoutMode.Dwindle && ws is { } dwindle && GlazeTree.LastFocusedTiling(dwindle) is { } target)
            await DwindleSplitAsync(target, ct);
        if (mode == LayoutMode.Columns && ws is { } columns && GlazeTree.Str(columns, "tilingDirection") == "vertical")
            await _ipc.CommandAsync("set-tiling-direction horizontal", ct, GlazeTree.Str(columns, "id"));
        await _ipc.CommandAsync("set-tiling", ct, window.Id);
        if (mode == LayoutMode.Scrolling) await ArrangeLockedAsync(window.Workspace, window.Id, ct);
        else
        {
            await _ipc.CommandAsync($"focus --container-id {window.Id}", ct);
            await _ipc.CommandAsync("wm-redraw", ct);
        }
        Log.Trace($"float: {window.Id} back into the {Id(mode)} layout of {window.Workspace}", "layout");
    }

    /// <summary>A window moved to <paramref name="workspace"/> (Win+Shift+1 …): that workspace's layout takes it. Caller holds the gate.</summary>
    internal async Task ArrivedLockedAsync(string workspace, string id, CancellationToken ct)
    {
        if (await ReconcileLockedAsync(workspace, await _ipc.QueryAsync("monitors", ct), ct) == LayoutMode.Scrolling)
            await ArrangeLockedAsync(workspace, id, ct);
    }

    // ------------------------------------------------------------------ scrolling

    ScrollStrip Strip(string workspace)
    {
        if (!_strips.TryGetValue(workspace, out var strip)) _strips[workspace] = strip = new ScrollStrip();
        return strip;
    }

    /// <summary>The columns of a scrolling workspace, or null. Caller holds the gate.</summary>
    internal ScrollStrip? StripOf(string workspace) =>
        Arranged(workspace) == LayoutMode.Scrolling && _strips.TryGetValue(workspace, out var s) ? s : null;

    /// <summary>A window leaves its scrolling workspace (moved away, into the scratchpad). Caller holds the gate.</summary>
    internal void Forget(string windowId)
    {
        foreach (var strip in _strips.Values)
            if (strip.Ids.Remove(windowId) && strip.Focus >= strip.Ids.Count) strip.Focus = Math.Max(0, strip.Ids.Count - 1);
    }

    /// <summary>The strip of a scrolling workspace, built first if WinMarchy just started. Caller holds the gate.</summary>
    async Task<ScrollStrip?> EnsureStripAsync(string workspace, CancellationToken ct)
    {
        if (Arranged(workspace) != LayoutMode.Scrolling) return null;
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
        _store.Columns.Any(c => Arranged(c.Key) == LayoutMode.Scrolling && c.Value.Contains(handle));

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

using System.Collections.Concurrent;
using System.Text.Json;

namespace WinMarchy.Core.GlazeWm;

/// <summary>
/// Omarchy's workspace layouts (SUPER+L cycles Dwindle → Scrolling → Floating) as far as GlazeWM can
/// follow:
/// <list type="bullet">
/// <item>Dwindle: every focused window splits along its longer side, so the next window takes half of it.</item>
/// <item>Columns: new windows line up side by side. The closest GlazeWM gets to Scrolling – real
/// scrolling (columns wider than the screen) does not exist in GlazeWM.</item>
/// <item>Floating: every window of the workspace floats; leaving the mode tiles those it floated again.</item>
/// <item>Manual: GlazeWM as it is; Win+J decides the split.</item>
/// </list>
/// </summary>
public enum LayoutMode { Dwindle, Columns, Floating, Manual }

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

public sealed class LayoutEngine
{
    static readonly LayoutMode[] Cycle = { LayoutMode.Dwindle, LayoutMode.Columns, LayoutMode.Floating };

    readonly GlazeIpc _ipc;
    readonly Func<LayoutMode> _default;
    readonly Action<string> _notify;
    readonly LayoutStore _store = LayoutStore.Load();
    /// <summary>Windows the floating mode floated (and will tile again).</summary>
    readonly ConcurrentDictionary<string, string> _floatedBy = new();
    readonly SemaphoreSlim _busy;

    public LayoutEngine(GlazeIpc ipc, Func<LayoutMode> defaultMode, Action<string> notify, SemaphoreSlim? gate = null)
    {
        _ipc = ipc;
        _default = defaultMode;
        _notify = notify;
        _busy = gate ?? new SemaphoreSlim(1, 1);
    }

    public static readonly string[] Events = { "focus_changed", "window_managed" };

    public LayoutMode ModeOf(string workspace) =>
        _store.Workspaces.TryGetValue(workspace, out var m) ? m : _default();

    public static LayoutMode Parse(string? text) => text?.ToLowerInvariant() switch
    {
        // "scrolling" is Omarchy's name; columns is the closest GlazeWM can do.
        "columns" or "scrolling" => LayoutMode.Columns,
        "floating" => LayoutMode.Floating,
        "manual" => LayoutMode.Manual,
        _ => LayoutMode.Dwindle,
    };

    public static string Name(LayoutMode m) => L.T("layouts." + m.ToString().ToLowerInvariant());

    public async Task OnEventAsync(GlazeEvent e, CancellationToken ct)
    {
        // Events come in bursts; if one is still being handled after half a second, this one is skipped.
        if (!await _busy.WaitAsync(500, ct)) return;
        try
        {
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
            if (name == null || name == GlazeConfig.ScratchpadWorkspace) return;

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

    /// <summary>Win+Alt+L / Win+Alt+Shift+L on the focused workspace.</summary>
    public async Task CycleAsync(bool back, CancellationToken ct)
    {
        var workspaces = await _ipc.QueryAsync("workspaces", ct);
        if (GlazeTree.FocusedWorkspace(workspaces) is not { } ws || GlazeTree.Str(ws, "name") is not { } name) return;
        var current = ModeOf(name);
        int i = Array.IndexOf(Cycle, current);
        var next = Cycle[((i < 0 ? 0 : i + (back ? -1 : 1)) % Cycle.Length + Cycle.Length) % Cycle.Length];
        await SetAsync(name, next, ct);
    }

    public async Task SetAsync(string workspace, LayoutMode mode, CancellationToken ct)
    {
        await _busy.WaitAsync(ct);
        try
        {
            var before = ModeOf(workspace);
            if (mode == _default()) _store.Workspaces.Remove(workspace);
            else _store.Workspaces[workspace] = mode;
            _store.Save();

            var workspaces = await _ipc.QueryAsync("workspaces", ct);
            var ws = GlazeTree.List(workspaces, "workspaces").Cast<JsonElement?>().FirstOrDefault(w => GlazeTree.Str(w!.Value, "name") == workspace);
            if (ws is { } w)
            {
                if (before == LayoutMode.Floating && mode != LayoutMode.Floating)
                {
                    foreach (var (id, owner) in _floatedBy.ToArray())
                    {
                        if (owner != workspace) continue;
                        try { await _ipc.CommandAsync("set-tiling", ct, id); } catch { /* window closed */ }
                        _floatedBy.TryRemove(id, out _);
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
                if (mode is LayoutMode.Dwindle or LayoutMode.Columns && GlazeTree.Bool(w, "isDisplayed"))
                    await RelayoutAsync(workspace, mode, ct);
            }
        }
        finally
        {
            _busy.Release();
        }
        _notify(L.T("layouts.changed", ("workspace", workspace), ("layout", Name(mode))));
    }

    /// <summary>
    /// GlazeWM only places new windows. So that a layout change shows at once, the windows of the
    /// workspace go out one by one (into the hidden scratchpad workspace) and come back in the new
    /// arrangement: Columns side by side in their old order, Dwindle each one halving the last.
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
                await _ipc.CommandAsync($"move --workspace {GlazeConfig.ScratchpadWorkspace}", ct, id);
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

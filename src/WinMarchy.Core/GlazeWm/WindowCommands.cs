using System.Collections.Concurrent;
using System.Text.Json;

namespace WinMarchy.Core.GlazeWm;

/// <summary>
/// Window functions GlazeWM has no single command for, built from queries and commands:
/// Omarchy's scratchpad, next/previous workspace on this monitor, moving a window to the
/// neighbouring monitor, restoring minimized windows, transparency per window.
/// </summary>
public sealed class WindowCommands
{
    readonly GlazeIpc _ipc;
    readonly Action<string> _notify;
    readonly ConcurrentDictionary<string, bool> _scratch = new();
    readonly ConcurrentDictionary<string, bool> _transparent = new();

    public WindowCommands(GlazeIpc ipc, Action<string> notify)
    {
        _ipc = ipc;
        _notify = notify;
    }

    async Task<JsonElement?> FocusedWindowAsync(CancellationToken ct)
    {
        var focused = await _ipc.QueryAsync("focused", ct);
        return focused.TryGetProperty("focused", out var f) && GlazeTree.Type(f) == "window" ? f : null;
    }

    // ------------------------------------------------------------------ scratchpad

    /// <summary>Win+Alt+S: the focused window floats and goes to the hidden scratchpad workspace.</summary>
    public async Task ScratchStoreAsync(CancellationToken ct)
    {
        if (await FocusedWindowAsync(ct) is not { } w || GlazeTree.Str(w, "id") is not { } id) return;
        await _ipc.CommandAsync("set-floating --centered --shown-on-top", ct, id);
        await _ipc.CommandAsync($"move --workspace {GlazeConfig.ScratchpadWorkspace}", ct, id);
        _scratch[id] = true;
        _notify(L.T("scratchpad.stored"));
    }

    /// <summary>Win+S: shows the scratchpad windows on top of the current workspace, or hides them again.</summary>
    public async Task ScratchToggleAsync(CancellationToken ct)
    {
        var workspaces = await _ipc.QueryAsync("workspaces", ct);
        var all = GlazeTree.List(workspaces, "workspaces").ToList();
        var pad = all.Cast<JsonElement?>().FirstOrDefault(w => GlazeTree.Str(w!.Value, "name") == GlazeConfig.ScratchpadWorkspace);
        var hidden = pad is { } p ? GlazeTree.Windows(p).Select(w => GlazeTree.Str(w, "id")).OfType<string>().ToList() : new List<string>();
        foreach (var id in hidden) _scratch[id] = true;

        // Forget windows that were closed.
        var alive = all.SelectMany(GlazeTree.Windows).Select(w => GlazeTree.Str(w, "id")).OfType<string>().ToHashSet();
        foreach (var id in _scratch.Keys.Where(k => !alive.Contains(k)).ToList()) _scratch.TryRemove(id, out _);

        if (_scratch.IsEmpty)
        {
            _notify(L.T("scratchpad.empty"));
            return;
        }
        if (hidden.Count > 0)
        {
            var target = GlazeTree.FocusedWorkspace(workspaces) is { } f ? GlazeTree.Str(f, "name") : null;
            if (target == null) return;
            foreach (var id in hidden)
            {
                await _ipc.CommandAsync($"move --workspace {target}", ct, id);
                await _ipc.CommandAsync("set-floating --centered --shown-on-top", ct, id);
            }
            await _ipc.CommandAsync($"focus --container-id {hidden[^1]}", ct);
        }
        else
        {
            foreach (var id in _scratch.Keys)
                await _ipc.CommandAsync($"move --workspace {GlazeConfig.ScratchpadWorkspace}", ct, id);
        }
    }

    // ------------------------------------------------------------------ workspaces and monitors

    /// <summary>Win+Tab: next workspace with windows on this monitor (the scratchpad is skipped).</summary>
    public async Task WorkspaceStepAsync(int step, CancellationToken ct)
    {
        var workspaces = await _ipc.QueryAsync("workspaces", ct);
        if (GlazeTree.FocusedWorkspace(workspaces) is not { } focused) return;
        var monitor = GlazeTree.Str(focused, "parentId");
        var names = GlazeTree.List(workspaces, "workspaces")
            .Where(w => GlazeTree.Str(w, "parentId") == monitor && GlazeTree.Str(w, "name") != GlazeConfig.ScratchpadWorkspace)
            .Select(w => GlazeTree.Str(w, "name")!)
            .OrderBy(n => n, Comparer<string>.Create(GlazeTree.CompareNames))
            .ToList();
        if (names.Count < 2) return;
        var i = names.IndexOf(GlazeTree.Str(focused, "name")!);
        var next = names[((i + step) % names.Count + names.Count) % names.Count];
        await _ipc.CommandAsync($"focus --workspace {next}", ct);
    }

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
            await _ipc.CommandAsync($"move --workspace {name}", ct);
    }

    public async Task RestoreMinimizedAsync(CancellationToken ct)
    {
        var data = await _ipc.QueryAsync("windows", ct);
        foreach (var w in GlazeTree.List(data, "windows").Where(w => GlazeTree.WindowState(w) == "minimized"))
            if (GlazeTree.Str(w, "id") is { } id)
                await _ipc.CommandAsync("toggle-minimized", ct, id);
    }

    /// <summary>Win+Backspace, as in Omarchy: the focused window becomes see-through, or opaque again.</summary>
    public async Task ToggleTransparencyAsync(CancellationToken ct)
    {
        if (await FocusedWindowAsync(ct) is not { } w || GlazeTree.Str(w, "id") is not { } id) return;
        var on = !_transparent.ContainsKey(id);
        await _ipc.CommandAsync("set-transparency --opacity " + (on ? "85%" : "100%"), ct, id);
        if (on) _transparent[id] = true; else _transparent.TryRemove(id, out _);
    }
}

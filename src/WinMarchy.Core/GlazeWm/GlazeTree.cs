using System.Text.Json;

namespace WinMarchy.Core.GlazeWm;

/// <summary>Reading GlazeWM's container tree (query workspaces / monitors / windows).</summary>
public static class GlazeTree
{
    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public static int Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : 0;

    public static string Type(JsonElement e) => Str(e, "type") ?? "";

    /// <summary>"tiling", "floating", "minimized" or "fullscreen".</summary>
    public static string WindowState(JsonElement window) =>
        window.TryGetProperty("state", out var s) ? Str(s, "type") ?? "" : "";

    public static IEnumerable<JsonElement> Children(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty("children", out var c) && c.ValueKind == JsonValueKind.Array
            ? c.EnumerateArray() : Enumerable.Empty<JsonElement>();

    public static IEnumerable<JsonElement> Descendants(JsonElement e)
    {
        foreach (var child in Children(e))
        {
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    public static IEnumerable<JsonElement> Windows(JsonElement container) => Descendants(container).Where(d => Type(d) == "window");

    /// <summary>The array under data.workspaces / data.monitors / data.windows.</summary>
    public static IEnumerable<JsonElement> List(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray() : Enumerable.Empty<JsonElement>();

    public static JsonElement? FocusedWorkspace(JsonElement workspacesData) =>
        List(workspacesData, "workspaces").Cast<JsonElement?>().FirstOrDefault(w => Bool(w!.Value, "hasFocus"));

    public static JsonElement? WorkspaceOf(JsonElement workspacesData, string windowId) =>
        List(workspacesData, "workspaces").Cast<JsonElement?>()
            .FirstOrDefault(w => Windows(w!.Value).Any(win => Str(win, "id") == windowId));

    /// <summary>Workspace names sort as numbers first (1 … 10), then alphabetically.</summary>
    public static int CompareNames(string? a, string? b)
    {
        bool na = int.TryParse(a, out var ia), nb = int.TryParse(b, out var ib);
        if (na && nb) return ia.CompareTo(ib);
        if (na) return -1;
        if (nb) return 1;
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }
}

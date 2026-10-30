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

    public static IEnumerable<JsonElement> TilingWindows(JsonElement container) => Windows(container).Where(w => WindowState(w) == "tiling");

    /// <summary>Children that take part in the layout: split containers and tiling windows.</summary>
    public static IEnumerable<JsonElement> TilingChildren(JsonElement e) =>
        Children(e).Where(c => Type(c) == "split" || (Type(c) == "window" && WindowState(c) == "tiling"));

    /// <summary>Every container below the given roots by id, for walking up via parentId.</summary>
    public static Dictionary<string, JsonElement> Index(IEnumerable<JsonElement> roots)
    {
        var map = new Dictionary<string, JsonElement>();
        foreach (var root in roots)
        {
            if (Str(root, "id") is { } rid) map[rid] = root;
            foreach (var d in Descendants(root))
                if (Str(d, "id") is { } id) map[id] = d;
        }
        return map;
    }

    /// <summary>A window's handle (HWND) as GlazeWM reports it.</summary>
    public static long Handle(JsonElement window) =>
        window.ValueKind == JsonValueKind.Object && window.TryGetProperty("handle", out var v) && v.TryGetInt64(out var h) ? h : 0;

    /// <summary>Workspaces that hold windows the user works with (not the scratchpad, not the staging area).</summary>
    public static bool IsRegularWorkspace(JsonElement workspace) => Str(workspace, "name") is { } n && GlazeConfig.IsRegular(n);

    /// <summary>A floating window that stays above all others (scratchpad, Win+O).</summary>
    public static bool ShownOnTop(JsonElement window) =>
        window.TryGetProperty("state", out var s) && Bool(s, "shownOnTop");

    /// <summary>Every window with its workspace and rectangle, from "query workspaces" or "query monitors".</summary>
    public static List<WindowInfo> WindowInfos(JsonElement data)
    {
        var list = new List<WindowInfo>();
        var workspaces = List(data, "workspaces").ToList();
        if (workspaces.Count == 0) workspaces = List(data, "monitors").SelectMany(Children).Where(w => Type(w) == "workspace").ToList();
        foreach (var ws in workspaces)
        {
            var name = Str(ws, "name") ?? "";
            foreach (var w in Windows(ws))
                if (Str(w, "id") is { } id)
                    list.Add(new WindowInfo(id, Handle(w), name, WindowState(w), ShownOnTop(w), Bool(w, "hasFocus"),
                        Int(w, "x"), Int(w, "y"), Int(w, "width"), Int(w, "height")));
        }
        return list;
    }

    /// <summary>
    /// The window next to <paramref name="from"/> in a direction, by position on the screen: the
    /// closest one whose centre lies that way, preferring windows on the same line.
    /// </summary>
    public static WindowInfo? Neighbor(WindowInfo from, IEnumerable<WindowInfo> candidates, string direction)
    {
        WindowInfo? best = null;
        double bestScore = double.MaxValue;
        foreach (var c in candidates)
        {
            if (c.Id == from.Id) continue;
            double dx = c.CenterX - from.CenterX, dy = c.CenterY - from.CenterY;
            var (along, across) = direction switch
            {
                "left" => (-dx, Math.Abs(dy)),
                "right" => (dx, Math.Abs(dy)),
                "up" => (-dy, Math.Abs(dx)),
                _ => (dy, Math.Abs(dx)),
            };
            if (along <= 1) continue;
            var score = along + 2 * across;
            if (score < bestScore)
            {
                bestScore = score;
                best = c;
            }
        }
        return best;
    }

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

/// <summary>A window as the scratchpad and the scrolling layout need it.</summary>
public sealed record WindowInfo(string Id, long Handle, string Workspace, string State, bool OnTop, bool Focused, int X, int Y, int Width, int Height)
{
    public double CenterX => X + Width / 2.0;
    public double CenterY => Y + Height / 2.0;
    public bool Floating => State == "floating";
}

/// <summary>A workspace's area (without outer gaps) and its monitor's scale, from "query monitors".</summary>
public sealed record WorkspaceArea(string Name, string Id, int X, int Y, int Width, int Height, double Scale, bool Displayed, bool Focused)
{
    public static List<WorkspaceArea> All(JsonElement monitorsData)
    {
        var list = new List<WorkspaceArea>();
        foreach (var m in GlazeTree.List(monitorsData, "monitors"))
        {
            var scale = m.TryGetProperty("scaleFactor", out var sf) && sf.TryGetDouble(out var d) && d > 0 ? d : 1.0;
            foreach (var ws in GlazeTree.Children(m).Where(c => GlazeTree.Type(c) == "workspace"))
                list.Add(new WorkspaceArea(GlazeTree.Str(ws, "name") ?? "", GlazeTree.Str(ws, "id") ?? "",
                    GlazeTree.Int(ws, "x"), GlazeTree.Int(ws, "y"), GlazeTree.Int(ws, "width"), GlazeTree.Int(ws, "height"),
                    scale, GlazeTree.Bool(ws, "isDisplayed"), GlazeTree.Bool(ws, "hasFocus")));
        }
        return list;
    }
}

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

    /// <summary>A floating window that stays above all others (scratchpad, Win+T, Win+O).</summary>
    public static bool ShownOnTop(JsonElement window) =>
        window.TryGetProperty("state", out var s) && Bool(s, "shownOnTop");

    /// <summary>Every workspace, from "query workspaces" or "query monitors".</summary>
    public static IEnumerable<JsonElement> Workspaces(JsonElement data)
    {
        var workspaces = List(data, "workspaces").ToList();
        return workspaces.Count > 0 ? workspaces : List(data, "monitors").SelectMany(Children).Where(w => Type(w) == "workspace");
    }

    /// <summary>
    /// Workspace name → index of the monitor it is on, in GlazeWM's order (the one "bind_to_monitor"
    /// and WinMarchy's screens count in), from "query monitors".
    /// </summary>
    public static Dictionary<string, int> ScreenOfWorkspaces(JsonElement monitorsData)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int i = 0;
        foreach (var m in List(monitorsData, "monitors"))
        {
            foreach (var ws in Children(m).Where(c => Type(c) == "workspace"))
                if (Str(ws, "name") is { } name) map[name] = i;
            i++;
        }
        return map;
    }

    /// <summary>
    /// The tiling window that had the focus last in a container (a workspace), following GlazeWM's
    /// focus order down the tree. GlazeWM puts a window that comes into the tiles next to this one.
    /// </summary>
    public static JsonElement? LastFocusedTiling(JsonElement container)
    {
        var node = container;
        for (int depth = 0; depth < 32; depth++)
        {
            var children = Children(node).Where(c => Type(c) == "split" || (Type(c) == "window" && WindowState(c) == "tiling")).ToList();
            if (children.Count == 0) return null;
            var order = node.TryGetProperty("childFocusOrder", out var o) && o.ValueKind == JsonValueKind.Array
                ? o.EnumerateArray().Select(x => x.GetString()).ToList() : new List<string?>();
            // Most recently focused first; a split without a tiling window in it does not count.
            var next = order.Select(id => children.Cast<JsonElement?>().FirstOrDefault(c => Str(c!.Value, "id") == id))
                .OfType<JsonElement>().FirstOrDefault(c => Type(c) == "window" || TilingWindows(c).Any());
            if (next.ValueKind != JsonValueKind.Object) next = children.FirstOrDefault(c => Type(c) == "window" || TilingWindows(c).Any());
            if (next.ValueKind != JsonValueKind.Object) return null;
            if (Type(next) == "window") return next;
            node = next;
        }
        return null;
    }

    /// <summary>Every window with its workspace and rectangle, from "query workspaces" or "query monitors".</summary>
    public static List<WindowInfo> WindowInfos(JsonElement data)
    {
        var list = new List<WindowInfo>();
        foreach (var ws in Workspaces(data))
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
    /// The workspace shown on the monitor next to the one with workspace <paramref name="from"/>, in a
    /// direction, from "query monitors": of the monitors beyond that edge, the nearest. A monitor
    /// beside a taller one is not above it, even if its centre is higher. Null at the outer edge, or
    /// if that monitor shows no regular workspace.
    /// </summary>
    public static string? WorkspaceInDirection(JsonElement monitorsData, string from, string direction)
    {
        var monitors = List(monitorsData, "monitors").ToList();
        var current = monitors.Cast<JsonElement?>().FirstOrDefault(m => Children(m!.Value).Any(w => Str(w, "name") == from));
        if (current is not { } cur) return null;
        static (int L, int T, int R, int B) Rect(JsonElement m) => (Int(m, "x"), Int(m, "y"), Int(m, "x") + Int(m, "width"), Int(m, "y") + Int(m, "height"));
        double Cx(JsonElement m) => Int(m, "x") + Int(m, "width") / 2.0;
        double Cy(JsonElement m) => Int(m, "y") + Int(m, "height") / 2.0;
        var c = Rect(cur);
        // A few pixels of overlap, as scaled screens leave them, still count as next to each other.
        const int slack = 8;
        var next = monitors.Where(m => Str(m, "id") != Str(cur, "id")).Where(m =>
        {
            var r = Rect(m);
            return direction switch
            {
                "left" => r.R <= c.L + slack,
                "right" => r.L >= c.R - slack,
                "up" => r.B <= c.T + slack,
                _ => r.T >= c.B - slack,
            };
        }).OrderBy(m => Math.Abs(Cx(m) - Cx(cur)) + Math.Abs(Cy(m) - Cy(cur))).Cast<JsonElement?>().FirstOrDefault();
        if (next is not { } target) return null;
        return Children(target).Where(w => Bool(w, "isDisplayed") && IsRegularWorkspace(w)).Select(w => Str(w, "name")).FirstOrDefault();
    }

    /// <summary>
    /// The window where the focus comes in when it arrives from the side opposite
    /// <paramref name="direction"/>: going right, the leftmost one; going down, the top one.
    /// </summary>
    public static WindowInfo? EntryWindow(IEnumerable<WindowInfo> windows, string direction) => direction switch
    {
        "left" => windows.MaxBy(w => w.X + w.Width),
        "right" => windows.MinBy(w => w.X),
        "up" => windows.MaxBy(w => w.Y + w.Height),
        _ => windows.MinBy(w => w.Y),
    };

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

    /// <summary>The workspace with the focus, from "query workspaces" or "query monitors".</summary>
    public static JsonElement? FocusedWorkspace(JsonElement data) =>
        Workspaces(data).Cast<JsonElement?>().FirstOrDefault(w => Bool(w!.Value, "hasFocus"));

    /// <summary>The workspace a window is in, from "query workspaces" or "query monitors".</summary>
    public static JsonElement? WorkspaceOf(JsonElement data, string windowId) =>
        Workspaces(data).Cast<JsonElement?>().FirstOrDefault(w => Windows(w!.Value).Any(win => Str(win, "id") == windowId));

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

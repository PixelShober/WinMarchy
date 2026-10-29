using System.Text;
using WinMarchy.Core.Hotkeys;
using WinMarchy.Core.Themes;

namespace WinMarchy.Core.GlazeWm;

/// <summary>Everything that goes into GlazeWM's config.yaml.</summary>
public sealed class GlazeConfigOptions
{
    public string FocusedBorder { get; set; } = "#7aa2f7";
    public string OtherBorder { get; set; } = "#414868";
    public int InnerGap { get; set; } = 10;
    public int OuterGap { get; set; } = 10;
    /// <summary>Extra space at the top for the bar (0 without bar).</summary>
    public int BarHeight { get; set; }
    public bool SquareCorners { get; set; } = true;
    public bool HideTitleBars { get; set; }
    public bool DimInactive { get; set; }
    public bool FocusFollowsCursor { get; set; }
    public List<string> StartupCommands { get; set; } = new();
    public List<string> ShutdownCommands { get; set; } = new();
    /// <summary>Workspace name → monitor index (0 = leftmost), for workspaces pinned to a screen.</summary>
    public Dictionary<string, int> WorkspaceMonitors { get; set; } = new();
    /// <summary>Only for the GlazeWM hotkey engine; with AutoHotkey the list stays empty.</summary>
    public List<(IReadOnlyList<string> Commands, string Binding, string Comment)> Keybindings { get; set; } = new();
}

/// <summary>
/// Writes GlazeWM's config.yaml as a whole, in Omarchy's look: square corners, a 2 px accent border
/// on the focused window, gaps of 5/10 px (Hyprland counts per side, GlazeWM between windows), no
/// window title bars if chosen. The previous file is in the restore point.
/// </summary>
public static class GlazeConfig
{
    public const string ScratchpadWorkspace = "scratchpad";
    /// <summary>Hidden workspace where a layout change parks windows for a moment. Never the scratchpad.</summary>
    public const string StageWorkspace = "winmarchy-stage";
    public const int WorkspaceCount = 10;

    /// <summary>The numbered workspaces, not the two hidden ones.</summary>
    public static bool IsRegular(string name) => name != ScratchpadWorkspace && name != StageWorkspace;
    public const string OverlayTitle = "WinMarchy Overlay";

    public static GlazeConfigOptions FromSettings(UserSettings s, Palette palette, int barHeight) => new()
    {
        FocusedBorder = palette.Accent,
        OtherBorder = palette.InactiveBorder,
        InnerGap = s.InnerGap,
        OuterGap = s.OuterGap,
        BarHeight = s.ShowBar ? barHeight : 0,
        SquareCorners = s.SquareCorners,
        HideTitleBars = s.HideTitleBars,
        DimInactive = s.DimInactive,
        FocusFollowsCursor = s.FocusFollowsCursor,
        WorkspaceMonitors = new Dictionary<string, int>(s.WorkspaceMonitors),
    };

    public static string Generate(GlazeConfigOptions o)
    {
        var sb = new StringBuilder();
        void Line(string text = "") => sb.Append(text).Append('\n');

        Line("# Written by WinMarchy. Changes here are overwritten the next time WinMarchy applies its settings;");
        Line("# change gaps, borders and shortcuts in WinMarchy instead. The previous file is in WinMarchy → Undo.");
        Line();
        Line("general:");
        Line("  startup_commands: " + List(o.StartupCommands));
        Line("  shutdown_commands: " + List(o.ShutdownCommands));
        Line("  config_reload_commands: []");
        Line($"  focus_follows_cursor: {Bool(o.FocusFollowsCursor)}");
        Line("  toggle_workspace_on_refocus: false");
        Line("  cursor_jump:");
        Line("    enabled: true");
        Line("    trigger: 'monitor_focus'");
        Line("  hide_method: 'cloak'");
        Line("  show_all_in_taskbar: false");
        Line();
        Line("gaps:");
        Line("  scale_with_dpi: true");
        Line($"  inner_gap: '{o.InnerGap}px'");
        Line("  outer_gap:");
        Line($"    top: '{o.OuterGap + o.BarHeight}px'");
        Line($"    right: '{o.OuterGap}px'");
        Line($"    bottom: '{o.OuterGap}px'");
        Line($"    left: '{o.OuterGap}px'");
        Line();
        Line("window_effects:");
        Effects(sb, "focused_window", o.FocusedBorder, o, dim: false);
        Effects(sb, "other_windows", o.OtherBorder, o, dim: o.DimInactive);
        Line();
        Line("window_behavior:");
        Line("  initial_state: 'tiling'");
        Line("  state_defaults:");
        Line("    floating:");
        Line("      centered: true");
        Line("      shown_on_top: false");
        Line("    fullscreen:");
        Line("      maximized: false");
        Line("      shown_on_top: false");
        Line();
        Line("workspaces:");
        for (int i = 1; i <= WorkspaceCount; i++)
        {
            Line($"  - name: '{i}'");
            // Pinned to a screen (Tiling page): Win+{i} always opens it there.
            if (o.WorkspaceMonitors.TryGetValue(i.ToString(), out var monitor) && monitor >= 0) Line($"    bind_to_monitor: {monitor}");
        }
        // Hidden workspaces: Win+S / Win+Alt+S, and a parking spot for layout changes.
        Line($"  - name: '{ScratchpadWorkspace}'");
        Line($"  - name: '{StageWorkspace}'");
        Line();
        Line("window_rules:");
        Rule(sb, "ignore", ("window_process", "equals", "zebar"));
        Rule(sb, "ignore", ("window_title", "equals", OverlayTitle));
        Rule(sb, "ignore", ("window_process", "equals", "Flow.Launcher"));
        Rule(sb, "ignore", ("window_title", "regex", "[Pp]icture.in.[Pp]icture|Bild im Bild"), ("window_class", "regex", "Chrome_WidgetWin_1|MozillaDialogClass"));
        Rule(sb, "set-floating --centered", ("window_process", "equals", "Taskmgr"));
        Rule(sb, "set-floating --centered", ("window_process", "equals", "CalculatorApp"));
        Rule(sb, "set-floating --centered", ("window_process", "equals", "SnippingTool"));
        // Teams and Outlook pop-ups (incoming call, meeting reminder) must not push the layout around.
        Rule(sb, "ignore", ("window_process", "equals", "ms-teams"), ("window_title", "regex", "^(Microsoft Teams Notification|Benachrichtigung|Notification)"));
        Rule(sb, "set-floating --centered", ("window_process", "equals", "OUTLOOK"), ("window_title", "regex", "Erinnerung|Reminder"));
        Line();
        Line("binding_modes: []");
        Line();
        if (o.Keybindings.Count == 0)
        {
            Line("# AutoHotkey owns the shortcuts (WinMarchy → Hotkeys).");
            Line("keybindings: []");
        }
        else
        {
            Line("keybindings:");
            foreach (var (commands, binding, comment) in o.Keybindings)
            {
                Line($"  # {comment.Replace('\n', ' ')}");
                Line("  - commands: " + List(commands));
                Line($"    bindings: [{Q(binding)}]");
            }
        }
        return sb.ToString();
    }

    static void Effects(StringBuilder sb, string section, string border, GlazeConfigOptions o, bool dim)
    {
        sb.Append($"  {section}:\n");
        sb.Append("    border:\n");
        sb.Append("      enabled: true\n");
        sb.Append($"      color: {Q(border)}\n");
        sb.Append("    hide_title_bar:\n");
        sb.Append($"      enabled: {Bool(o.HideTitleBars)}\n");
        sb.Append("    corner_style:\n");
        sb.Append($"      enabled: {Bool(o.SquareCorners)}\n");
        sb.Append("      style: 'square'\n");
        sb.Append("    transparency:\n");
        sb.Append($"      enabled: {Bool(dim)}\n");
        sb.Append("      opacity: '90%'\n");
    }

    static void Rule(StringBuilder sb, string command, params (string Field, string Op, string Value)[] match)
    {
        sb.Append($"  - commands: [{Q(command)}]\n");
        sb.Append("    match:\n");
        for (int i = 0; i < match.Length; i++)
        {
            var (field, op, value) = match[i];
            sb.Append(i == 0 ? "      - " : "        ").Append($"{field}: {{ {op}: {Q(value)} }}\n");
        }
    }

    /// <summary>YAML single-quoted scalar: only the quote itself needs escaping.</summary>
    public static string Q(string value) => "'" + value.Replace("'", "''") + "'";

    static string List(IEnumerable<string> items) => "[" + string.Join(", ", items.Select(Q)) + "]";

    static string Bool(bool b) => b ? "true" : "false";

    /// <summary>
    /// Arrows and Win+Shift+1 … go through WinMarchy for the scratchpad and the scrolling layout.
    /// With the GlazeWM engine that would start WinMarchy.exe on every key press, so they stay
    /// GlazeWM's own commands there.
    /// </summary>
    static IReadOnlyList<string>? Native(HotkeyAction action)
    {
        if (action.Kind != ActionKind.Do) return null;
        var parts = action.Value.Split(' ', 2, StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return null;
        return parts[0] switch
        {
            "focus" => new[] { "focus --direction " + parts[1] },
            "swap" => new[] { "move --direction " + parts[1] },
            "to-workspace" => new[] { "move --workspace " + parts[1], "focus --workspace " + parts[1] },
            _ => null,
        };
    }

    /// <summary>
    /// Keybindings for the GlazeWM engine. Window commands run in GlazeWM itself, everything else goes
    /// through WinMarchy.exe --action (GlazeWM cannot send keys or show menus).
    /// </summary>
    public static List<(IReadOnlyList<string> Commands, string Binding, string Comment)> Keybindings(
        HotkeySet set, IKeyboardLayout layout, string winMarchyExe, IReadOnlyCollection<string>? leaveAlone = null,
        IReadOnlyList<HotkeyBinding>? extra = null)
    {
        var list = new List<(IReadOnlyList<string>, string, string)>();
        var taken = set.Active.Select(b => b.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var b in set.Active.Concat(extra?.Where(x => !taken.Contains(x.Keys)) ?? Enumerable.Empty<HotkeyBinding>()))
        {
            if (b.Combo is not { } combo || b.Action is not { } action) continue;
            if (leaveAlone?.Contains(combo.ToString()) == true) continue;
            var key = combo.ToGlaze(layout);
            if (key == null) continue;
            IReadOnlyList<string> commands = action.Kind == ActionKind.Wm
                ? action.WmCommands.ToList()
                : Native(action) ?? new[] { $"shell-exec --hide-window {winMarchyExe} --action {action}" };
            list.Add((commands, key, b.Describe()));
        }
        return list;
    }
}

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
    public const int WorkspaceCount = 10;
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
        for (int i = 1; i <= WorkspaceCount; i++) Line($"  - name: '{i}'");
        // Hidden workspace for Win+S / Win+Alt+S. WinMarchy moves windows in and out of it.
        Line($"  - name: '{ScratchpadWorkspace}'");
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
    /// Keybindings for the GlazeWM engine. Window commands run in GlazeWM itself, everything else goes
    /// through WinMarchy.exe --action (GlazeWM cannot send keys or show menus).
    /// </summary>
    public static List<(IReadOnlyList<string> Commands, string Binding, string Comment)> Keybindings(
        HotkeySet set, IKeyboardLayout layout, string winMarchyExe)
    {
        var list = new List<(IReadOnlyList<string>, string, string)>();
        foreach (var b in set.Active)
        {
            if (b.Combo is not { } combo || b.Action is not { } action) continue;
            var key = combo.ToGlaze(layout);
            if (key == null) continue;
            IReadOnlyList<string> commands = action.Kind == ActionKind.Wm
                ? action.WmCommands.ToList()
                : new[] { $"shell-exec --hide-window {winMarchyExe} --action {action}" };
            list.Add((commands, key, b.Describe()));
        }
        return list;
    }
}

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using WinMarchy.Core.GlazeWm;
using WinMarchy.Core.Hotkeys;
using WinMarchy.Core.Themes;

namespace WinMarchy.Core.Integrations;

/// <summary>
/// WinMarchy's bar in YASB: config.yaml and styles.css in %USERPROFILE%\.config\yasb, each written as
/// a whole (the previous files are in the restore point). YASB reloads either file by itself when it
/// changes. The look is Omarchy's Waybar: square, one accent, small menus that drop down from the
/// bar. Icons come from Segoe Fluent Icons, which Windows 11 brings along.
/// </summary>
/// <summary>What the bar's config depends on besides the settings.</summary>
/// <param name="Fastfetch">Fastfetch is installed: the logo menu offers the system information.</param>
/// <param name="WifiMenu">
/// Windows lets desktop apps see the location. Without it Windows 11 hands out no Wi-Fi list, so a
/// click on the network opens Windows' own list instead of YASB's.
/// </param>
public sealed record YasbOptions(bool Fastfetch = false, bool WifiMenu = true);

public static class YasbBar
{
    public const string BarName = "winmarchy";
    const string Marker = "# Written by WinMarchy.";

    /// <summary>
    /// The menus a shortcut can open (do:bar-menu …). YASB listens for a key combination nobody
    /// presses by hand and WinMarchy sends it; YASB has no other way in from outside.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string Module, string Keys, string Action)> Menus = new Dictionary<string, (string, string, string)>
    {
        ["logo"] = ("logo", "Ctrl+Alt+Shift+F13", "toggle_menu"),
        ["calendar"] = ("clock", "Ctrl+Alt+Shift+F14", "toggle_calendar"),
        ["audio"] = ("audio", "Ctrl+Alt+Shift+F15", "toggle_volume_menu"),
        ["network"] = ("network", "Ctrl+Alt+Shift+F16", "toggle_menu"),
        ["media"] = ("media", "Ctrl+Alt+Shift+F17", "toggle_media_menu"),
        ["cpu"] = ("cpu", "Ctrl+Alt+Shift+F18", "toggle_menu"),
        ["memory"] = ("memory", "Ctrl+Alt+Shift+F19", "toggle_menu"),
    };

    /// <summary>The menu's hidden key combination, as YASB writes it, for the module's "keybindings".</summary>
    static JsonArray Keybinding(string module)
    {
        var list = new JsonArray();
        foreach (var (_, (_, keys, action)) in Menus.Where(m => m.Value.Module == module))
            list.Add(new JsonObject { ["keys"] = keys.ToLowerInvariant(), ["action"] = action, ["screen"] = "active" });
        return list;
    }

    /// <summary>The classes YASB gives a workspace button.</summary>
    static readonly string[] WorkspaceStates = { "populated", "empty", "active_populated", "active_empty", "focused_populated", "focused_empty" };

    /// <summary>The bar's files are WinMarchy's (and not the user's own YASB setup).</summary>
    public static bool IsInstalled(Locations loc) =>
        File.Exists(loc.YasbConfig) && FileSafety.ReadShared(loc.YasbConfig).StartsWith(Marker, StringComparison.Ordinal);

    // ------------------------------------------------------------------ config.yaml

    /// <summary>
    /// The whole config.yaml. It is written as JSON, which YAML reads as well; that way every string
    /// is escaped correctly, whatever the user calls a shortcut.
    /// </summary>
    public static string Config(string winMarchyExe, UserSettings s, YasbOptions? options = null)
    {
        var o = options ?? new YasbOptions();
        var widgets = new JsonObject();
        var left = new JsonArray();
        var center = new JsonArray();
        var right = new JsonArray();
        bool On(string module) => Bars.IsOn(s, module);
        void Add(JsonArray side, string name, JsonObject widget)
        {
            widgets[name] = widget;
            side.Add(name);
        }

        if (On("logo")) Add(left, "logo", Logo(winMarchyExe, s, o.Fastfetch));
        if (On("workspaces")) Add(left, "workspaces", Workspaces());
        if (On("direction")) Add(left, "direction", Widget("glazewm.tiling_direction.GlazewmTilingDirectionWidget", new JsonObject
        {
            ["horizontal_label"] = "↔",
            ["vertical_label"] = "↕",
        }));
        if (On("clock")) Add(center, "clock", Clock(winMarchyExe));

        // The user's own buttons sit right of the clock, before the status entries.
        for (int i = 0; i < s.BarShortcuts.Count; i++)
        {
            var shortcut = s.BarShortcuts[i];
            if (!shortcut.InBar || !IsUsable(shortcut)) continue;
            Add(right, $"shortcut{i + 1}", Widget("yasb.custom.CustomWidget", new JsonObject
            {
                ["label"] = Label(shortcut.Title),
                ["label_alt"] = Label(shortcut.Title),
                ["label_placeholder"] = Plain(shortcut.Title),
                ["class_name"] = "shortcut-widget",
                // Nothing to fetch: the label is drawn once and stays.
                ["exec_options"] = new JsonObject { ["run_once"] = true },
                // Run through WinMarchy by its number: the action itself may hold characters cmd.exe would read.
                ["callbacks"] = Callbacks(Exec(winMarchyExe, $"do:bar-shortcut {i + 1}"), "do_nothing", "do_nothing"),
            }));
        }

        if (On("media")) Add(right, "media", Media());
        if (On("tray")) Add(right, "tray", Widget("yasb.systray.SystrayWidget", new JsonObject
        {
            ["class_name"] = "systray",
            // Folded away as in Omarchy: Alt+click pins an icon so it stays in sight.
            ["label_collapsed"] = "‹",
            ["label_expanded"] = "›",
            ["label_position"] = "left",
            ["icon_size"] = 14,
            ["pin_click_modifier"] = "alt",
            ["show_unpinned"] = false,
            ["show_unpinned_button"] = true,
            ["tooltip"] = true,
        }));
        if (On("network")) Add(right, "network", Network(winMarchyExe, o.WifiMenu));
        if (On("audio")) Add(right, "audio", Audio(winMarchyExe));
        if (On("cpu")) Add(right, "cpu", Widget("yasb.cpu.CpuWidget", new JsonObject
        {
            ["label"] = "cpu {info[percent][total]}%",
            ["label_alt"] = "cpu {info[percent][total]}% · {info[freq][current]:.0f} MHz",
            ["update_interval"] = 3000,
            ["hide_decimal"] = true,
            ["callbacks"] = Callbacks("toggle_menu", "do_nothing", Exec(winMarchyExe, "app:taskmanager")),
            ["menu"] = Popup(new JsonObject { ["enabled"] = true, ["show_graph"] = true }),
            ["keybindings"] = Keybinding("cpu"),
        }));
        if (On("memory")) Add(right, "memory", Widget("yasb.memory.MemoryWidget", new JsonObject
        {
            ["label"] = "ram {virtual_mem_percent}%",
            ["label_alt"] = "ram {virtual_mem_used}/{virtual_mem_total}",
            ["update_interval"] = 5000,
            ["hide_decimal"] = true,
            ["callbacks"] = Callbacks("toggle_menu", "do_nothing", Exec(winMarchyExe, "app:taskmanager")),
            ["menu"] = Popup(new JsonObject { ["enabled"] = true, ["show_graph"] = true }),
            ["keybindings"] = Keybinding("memory"),
        }));
        if (On("battery")) Add(right, "battery", Battery(winMarchyExe));

        var bar = new JsonObject
        {
            ["enabled"] = true,
            ["screens"] = new JsonArray("*"),
            ["class_name"] = "yasb-bar",
            ["context_menu"] = false,
            ["alignment"] = new JsonObject { ["position"] = "top", ["align"] = "center" },
            ["blur_effect"] = new JsonObject { ["enabled"] = false, ["round_corners"] = false, ["border_color"] = "None" },
            ["animation"] = new JsonObject { ["enabled"] = false },
            // GlazeWM leaves room through its top gap, as for Zebar; no app bar reservation.
            ["window_flags"] = new JsonObject { ["always_on_top"] = false, ["windows_app_bar"] = false, ["hide_on_fullscreen"] = true },
            ["dimensions"] = new JsonObject { ["width"] = "100%", ["height"] = Bars.Height },
            ["padding"] = new JsonObject { ["top"] = 0, ["left"] = 6, ["bottom"] = 0, ["right"] = 6 },
            ["widgets"] = new JsonObject { ["left"] = left, ["center"] = center, ["right"] = right },
        };
        var root = new JsonObject
        {
            ["watch_stylesheet"] = true,
            ["watch_config"] = true,
            ["debug"] = false,
            // YASB's own updater runs its MSI, which needs admin rights; WinMarchy updates it instead.
            ["update_check"] = false,
            // WinMarchy's tray icon is enough; YASB's own would only sit next to it.
            ["show_systray"] = false,
            ["bars"] = new JsonObject { [BarName] = bar },
            ["widgets"] = widgets,
        };
        return Marker + " Changes here are overwritten the next time WinMarchy applies its settings;\n" +
               "# choose the entries on WinMarchy's Tiling page instead. The previous file is in WinMarchy → Undo.\n" +
               Json.Format(root) + "\n";
    }

    /// <summary>
    /// The logo opens a menu of WinMarchy's own entries and the user's shortcuts, with YASB's system and
    /// power entries below. Right click opens the WinMarchy menu (Win+Space).
    /// </summary>
    static JsonObject Logo(string exe, UserSettings s, bool fastfetch)
    {
        var items = new JsonArray
        {
            MenuItem(L.T("bar.menu.winmarchy"), exe, "do:menu"),
            MenuItem(L.T("bar.menu.keybindings"), exe, "do:keybindings"),
            MenuItem(L.T("bar.menu.themes"), exe, "do:theme-menu"),
            MenuItem(L.T("bar.menu.wallpapers"), exe, "do:wallpaper-menu"),
        };
        if (fastfetch) items.Add(MenuItem(L.T("bar.menu.about"), exe, "do:about"));
        var own = s.BarShortcuts.Where(IsUsable).ToList();
        if (own.Count > 0)
        {
            items.Add(new JsonObject { ["separator"] = true });
            foreach (var shortcut in own) items.Add(MenuItem(Plain(shortcut.Title), exe, shortcut.Action));
        }
        items.Add(new JsonObject { ["separator"] = true });
        items.Add(MenuItem(L.T("bar.menu.settings"), exe, "app:winmarchy"));
        return Widget("yasb.home.HomeWidget", new JsonObject
        {
            ["label"] = "<span></span>",
            ["menu_list"] = items,
            ["system_menu"] = true,
            ["power_menu"] = true,
            ["blur"] = false,
            ["round_corners"] = false,
            ["border_color"] = "None",
            ["alignment"] = "left",
            ["direction"] = "down",
            ["offset_top"] = 0,
            ["offset_left"] = 0,
            ["menu_labels"] = new JsonObject
            {
                ["shutdown"] = L.T("bar.power.shutdown"),
                ["restart"] = L.T("bar.power.restart"),
                ["hibernate"] = L.T("bar.power.hibernate"),
                ["logout"] = L.T("bar.power.logout"),
                ["lock"] = L.T("bar.power.lock"),
                ["sleep"] = L.T("bar.power.sleep"),
                ["system"] = L.T("bar.power.system"),
                ["about"] = L.T("bar.power.about"),
                ["task_manager"] = L.T("bar.power.taskManager"),
            },
            ["callbacks"] = Callbacks("toggle_menu", "do_nothing", Exec(exe, "do:menu")),
            ["keybindings"] = Keybinding("logo"),
        });
    }

    /// <summary>A menu entry that starts WinMarchy directly (no cmd.exe in between, so any action text is safe).</summary>
    static JsonObject MenuItem(string title, string exe, string action) => new()
    {
        ["title"] = title,
        ["command"] = exe,
        ["args"] = new JsonArray("--action", action),
    };

    static JsonObject Workspaces() => Widget("glazewm.workspaces.GlazewmWorkspacesWidget", new JsonObject
    {
        ["offline_label"] = L.T("bar.glazeOffline"),
        ["hide_if_offline"] = false,
        ["monitor_exclusive"] = true,
        ["enable_scroll_switching"] = true,
        ["reverse_scroll_direction"] = false,
    });

    /// <summary>Weekday and time; a click drops down the calendar with week numbers and holidays.</summary>
    static JsonObject Clock(string exe)
    {
        var de = L.Language == "de";
        var calendar = Popup(new JsonObject
        {
            ["alignment"] = "center",
            ["show_week_numbers"] = true,
            ["show_holidays"] = Holidays() != null,
            ["holiday_color"] = "#E0AF68",
        });
        if (Holidays() is { } country) calendar["country_code"] = country;
        return Widget("yasb.clock.ClockWidget", new JsonObject
        {
            ["label"] = de ? "{%A %H:%M}" : "{%A %I:%M %p}",
            ["label_alt"] = de ? "{%d.%m.%Y %H:%M:%S}" : "{%Y-%m-%d %I:%M:%S %p}",
            ["update_interval"] = 1000,
            ["locale"] = L.Culture.Name,
            ["tooltip"] = false,
            ["calendar"] = calendar,
            ["callbacks"] = Callbacks("toggle_calendar", "toggle_label", Exec(exe, "app:calendar")),
            ["keybindings"] = Keybinding("clock"),
        });
    }

    /// <summary>The country whose public holidays the calendar marks, from the language setting.</summary>
    static string? Holidays()
    {
        try
        {
            var region = new RegionInfo(L.Culture.Name).TwoLetterISORegionName;
            return region.Length == 2 ? region : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    static JsonObject Media() => Widget("yasb.media.MediaWidget", new JsonObject
    {
        ["label"] = "{title}",
        ["label_alt"] = "{artist} · {title}",
        ["hide_empty"] = true,
        ["show_thumbnail"] = false,
        ["controls_left"] = false,
        ["icons"] = new JsonObject { ["prev_track"] = "", ["next_track"] = "", ["play"] = "", ["pause"] = "" },
        ["max_field_size"] = new JsonObject { ["label"] = 24, ["label_alt"] = 40, ["truncate_whole_label"] = false },
        ["media_menu"] = Popup(new JsonObject { ["show_source"] = true }),
        ["callbacks"] = Callbacks("toggle_media_menu", "toggle_play_pause", "toggle_label"),
        ["keybindings"] = Keybinding("media"),
    });

    /// <summary>
    /// Wi-Fi strength or the network cable; a click lists the networks (YASB's list, or Windows' own
    /// when Windows keeps the location from desktop apps), right click opens the settings.
    /// </summary>
    static JsonObject Network(string exe, bool wifiMenu) => Widget("yasb.wifi.WifiWidget", new JsonObject
    {
        ["label"] = "<span>{wifi_icon}</span>",
        ["label_alt"] = "<span>{wifi_icon}</span> {wifi_name}",
        ["update_interval"] = 5000,
        ["wifi_icons"] = new JsonArray("", "", "", "", ""),
        ["ethernet_label"] = "<span>{wifi_icon}</span>",
        ["ethernet_label_alt"] = "<span>{wifi_icon}</span> {ip_addr}",
        ["ethernet_icon"] = "",
        ["callbacks"] = Callbacks(wifiMenu ? "toggle_menu" : Exec(exe, NetworkListAction), "toggle_label", Exec(exe, "open:ms-settings:network")),
        ["menu_config"] = Popup(new JsonObject()),
        ["keybindings"] = wifiMenu ? Keybinding("network") : new JsonArray(),
    });

    /// <summary>Windows' own list of Wi-Fi networks.</summary>
    public const string NetworkListAction = "open:ms-availablenetworks:";

    /// <summary>Volume with a slider and the output devices; right click mutes, the wheel turns it up or down.</summary>
    static JsonObject Audio(string exe) => Widget("yasb.volume.VolumeWidget", new JsonObject
    {
        ["label"] = "<span>{icon}</span> {level}",
        ["label_alt"] = "<span>{icon}</span> {level}",
        ["mute_text"] = L.T("bar.muted"),
        ["tooltip"] = true,
        ["scroll_step"] = 5,
        ["slider_beep"] = false,
        ["icons"] = new JsonObject { ["muted"] = "", ["10"] = "", ["30"] = "", ["60"] = "", ["100"] = "" },
        ["audio_menu"] = Popup(new JsonObject { ["show_apps"] = true, ["show_app_labels"] = false }),
        ["callbacks"] = Callbacks("toggle_volume_menu", Exec(exe, "open:ms-settings:sound"), "toggle_mute"),
        ["keybindings"] = Keybinding("audio"),
    });

    static JsonObject Battery(string exe) => Widget("yasb.battery.BatteryWidget", new JsonObject
    {
        ["label"] = "<span>{icon}</span> {percent}%",
        ["label_alt"] = "<span>{icon}</span> {percent}% · {time_remaining}",
        ["update_interval"] = 15000,
        ["time_remaining_natural"] = true,
        ["hide_unsupported"] = true,
        ["charging_options"] = new JsonObject { ["icon_format"] = "{charging_icon}", ["blink_charging_icon"] = false },
        ["status_icons"] = new JsonObject
        {
            ["icon_charging"] = "", ["icon_critical"] = "", ["icon_low"] = "",
            ["icon_medium"] = "", ["icon_high"] = "", ["icon_full"] = "",
        },
        ["callbacks"] = Callbacks("toggle_label", "do_nothing", Exec(exe, "open:ms-settings:batterysaver")),
    });

    /// <summary>Menus drop straight down from the bar, square and without blur.</summary>
    static JsonObject Popup(JsonObject options)
    {
        options["blur"] = false;
        options["round_corners"] = false;
        options["border_color"] = "None";
        if (!options.ContainsKey("alignment")) options["alignment"] = "right";
        options["direction"] = "down";
        options["offset_top"] = 0;
        options["offset_left"] = 0;
        return options;
    }

    static JsonObject Widget(string type, JsonObject options) => new() { ["type"] = type, ["options"] = options };

    static JsonObject Callbacks(string left, string middle, string right) => new()
    {
        ["on_left"] = left,
        ["on_middle"] = middle,
        ["on_right"] = right,
    };

    /// <summary>
    /// YASB's "exec" callback: it splits on spaces, keeps "quoted" parts together and hands the rest
    /// to cmd.exe. Only fixed actions without spaces or cmd characters go this way.
    /// </summary>
    internal static string Exec(string exe, string action) => $"exec \"{exe}\" --action {action}";

    /// <summary>
    /// Qt shows a text as rich text as soon as it holds something like a tag, and YASB splits labels
    /// at "&lt;span&gt;". Angle brackets become look-alikes, so a title always shows as typed.
    /// </summary>
    static string Plain(string text) => text.Trim().Replace('<', '\u2039').Replace('>', '\u203A');

    /// <summary>A custom widget's label also goes through Python's str.format: braces are doubled.</summary>
    static string Label(string text) => Plain(text).Replace("{", "{{").Replace("}", "}}");

    static bool IsUsable(BarShortcut s) => s.Title.Trim().Length > 0 && HotkeyAction.TryParse(s.Action, out _, out _);

    // ------------------------------------------------------------------ styles.css

    /// <summary>
    /// The stylesheet in the theme's colours. YASB's CSS engine takes Qt style sheets; it strips
    /// everything after "//", so no URL may appear in here.
    /// </summary>
    public static string Styles(Palette p)
    {
        var bg = p.Background;
        var fg = p.Foreground;
        var accent = ColorMath.Legible(p.Accent, bg, 3.0);
        var muted = p.DarkForeground;
        var warn = ColorMath.Legible(p.Yellow, bg);
        var danger = ColorMath.Legible(p.Red, bg);
        var border = p.InactiveBorder;
        var hover = p.LighterBackground;
        var selected = p.Selection;
        var onAccent = ColorMath.Contrast(p.Accent, "#000000") > ColorMath.Contrast(p.Accent, "#ffffff") ? "#000000" : "#ffffff";

        var sb = new StringBuilder();
        void Rule(string selector, string body) => sb.Append(selector).Append(" {\n    ").Append(body.Trim().Replace("; ", ";\n    ")).Append("\n}\n");

        sb.Append($"/* Written by WinMarchy from the Omarchy theme \"{p.Name}\". The next theme overwrites it. */\n");
        Rule("*", $"font-family: 'Cascadia Mono', 'Segoe UI'; font-size: 12px; color: {fg};");
        Rule(".yasb-bar", $"background-color: {bg}; border: none;");
        Rule(".container-left, .container-center, .container-right", "background-color: transparent;");
        Rule(".widget", "background-color: transparent; padding: 0 7px; margin: 0; border: none;");
        Rule(".widget .widget-container", "background-color: transparent; border: none;");
        Rule(".widget .label", "padding: 0 1px;");
        Rule(".icon", $"font-family: 'Segoe Fluent Icons', 'Segoe MDL2 Assets'; font-size: 13px; color: {fg}; padding: 0 3px 0 0;");

        Rule(".home-widget .icon", $"color: {accent}; font-size: 14px; padding: 0;");
        Rule(".home-widget .icon:hover", $"color: {fg};");

        Rule(".glazewm-workspaces", "padding: 0; margin: 0;");
        Rule(".glazewm-workspaces .ws-btn", $"background-color: transparent; border: none; border-bottom: 2px solid transparent; color: {muted}; padding: 0 6px; margin: 0 1px; min-width: 10px;");
        Rule(".glazewm-workspaces .ws-btn.populated", $"color: {fg};");
        Rule(".glazewm-workspaces .ws-btn.active_populated, .glazewm-workspaces .ws-btn.active_empty", $"color: {fg}; border-bottom: 2px solid {border};");
        Rule(".glazewm-workspaces .ws-btn.focused_populated, .glazewm-workspaces .ws-btn.focused_empty", $"color: {accent}; border-bottom: 2px solid {accent};");
        Rule(".glazewm-workspaces .ws-btn:hover", $"color: {accent};");
        // The scratchpad and the staging area of the scrolling layout are hidden workspaces. YASB
        // has no list of workspaces to leave out, so their buttons are squeezed to nothing. Each
        // state is named, so this rule outranks the ones above.
        var hidden = string.Join(",\n", new[] { GlazeConfig.ScratchpadWorkspace, GlazeConfig.StageWorkspace }
            .SelectMany(name => WorkspaceStates.Select(state => $".glazewm-workspaces .ws-btn.{state}[text=\"{name}\"]")));
        Rule(hidden, "max-width: 0px; min-width: 0px; padding: 0px; margin: 0px; border: none; font-size: 1px; color: transparent; background-color: transparent;");
        Rule(".glazewm-workspaces .offline-status", $"color: {muted};");
        Rule(".glazewm-tiling-direction .label, .glazewm-tiling-direction .btn", $"color: {muted}; background-color: transparent; border: none;");

        Rule(".clock-widget .label", $"color: {fg};");
        Rule(".cpu-widget .label, .memory-widget .label", $"color: {muted};");
        Rule(".battery-widget .label.status-low, .battery-widget .icon.status-low", $"color: {warn};");
        Rule(".battery-widget .label.status-critical, .battery-widget .icon.status-critical", $"color: {danger};");
        Rule(".shortcut-widget .label", $"color: {fg};");
        Rule(".shortcut-widget .label:hover, .clock-widget .label:hover, .cpu-widget .label:hover, .memory-widget .label:hover", $"color: {accent};");
        Rule(".media-widget .label", $"color: {muted};");
        Rule(".media-widget .btn", $"font-family: 'Segoe Fluent Icons', 'Segoe MDL2 Assets'; color: {fg}; background-color: transparent; border: none; padding: 0 4px;");
        Rule(".media-widget .btn:hover", $"color: {accent};");
        Rule(".media-widget .btn.disabled", $"color: {border};");

        Rule(".systray", "background-color: transparent; border: none;");
        Rule(".systray .button", "background-color: transparent; border: none; padding: 0 3px;");
        Rule(".systray .button:hover", $"background-color: {hover};");
        Rule(".systray .unpinned-visibility-btn", $"color: {muted}; background-color: transparent; border: none; padding: 0 4px;");
        Rule(".systray .unpinned-visibility-btn:hover, .systray .unpinned-visibility-btn:checked", $"color: {accent};");

        // The menus that drop down from the bar.
        var panel = $"background-color: {bg}; border: 1px solid {accent}; border-radius: 0px;";
        Rule(".home-menu, .wifi-menu, .audio-menu, .media-menu, .cpu-popup, .memory-popup, .systray-popup", panel);
        Rule(".home-menu .menu-item", $"padding: 6px 16px; font-size: 12px; color: {fg}; background-color: transparent;");
        Rule(".home-menu .menu-item:hover", $"background-color: {selected}; color: {fg};");
        Rule(".home-menu .separator", $"max-height: 1px; min-height: 1px; background-color: {border}; margin: 4px 0;");

        Rule(".clock-popup.calendar", $"{panel} padding: 10px;");
        Rule(".clock-popup.calendar .calendar-table, .clock-popup.calendar .calendar-table::item", $"background-color: transparent; color: {fg}; border: none; margin: 0; font-size: 12px;");
        Rule(".clock-popup.calendar .calendar-table::item:selected", $"background-color: {accent}; color: {onAccent};");
        Rule(".clock-popup.calendar .day-label, .clock-popup.calendar .month-label, .clock-popup.calendar .date-label", $"color: {fg}; font-size: 13px;");
        Rule(".clock-popup.calendar .week-label", $"color: {muted};");
        Rule(".clock-popup.calendar .holiday-label", $"color: {warn};");

        Rule(".wifi-menu .header, .wifi-menu .footer", $"background-color: {bg}; color: {muted}; border: none; padding: 8px 12px;");
        Rule(".wifi-menu .wifi-list", "background-color: transparent;");
        Rule(".wifi-menu .wifi-item", "padding: 2px 12px; margin: 1px 4px;");
        Rule(".wifi-menu .wifi-item:hover", $"background-color: {hover};");
        Rule(".wifi-menu .wifi-item[active=true]", $"background-color: {selected};");
        Rule(".wifi-menu .wifi-item .icon, .wifi-menu .footer .refresh-icon", $"font-family: 'Segoe Fluent Icons'; color: {fg};");
        Rule(".wifi-menu .wifi-item .connect", $"background-color: {accent}; color: {onAccent}; border: none; padding: 4px 24px;");
        Rule(".wifi-menu .footer .settings-button", $"color: {fg}; background-color: transparent; border: none;");

        Rule(".audio-menu", $"{panel} padding: 8px;");
        Rule(".audio-menu .audio-container .device", $"background-color: transparent; color: {fg}; border: none; padding: 4px 8px;");
        Rule(".audio-menu .audio-container .device:hover", $"background-color: {hover};");
        Rule(".audio-menu .audio-container .device.selected", $"background-color: {selected};");
        Rule(".audio-menu .toggle-apps", $"color: {muted}; background-color: transparent; border: none;");

        Rule(".media-menu .title, .media-menu .artist, .media-menu .source, .media-menu .playback-time", $"color: {fg}; background-color: transparent;");
        Rule(".media-menu .btn", $"font-family: 'Segoe Fluent Icons'; color: {fg}; background-color: transparent; border: none; font-size: 16px;");
        Rule(".media-menu .btn:hover", $"color: {accent};");

        Rule(".cpu-popup .header, .memory-popup .header", $"color: {fg}; background-color: transparent; border-bottom: 1px solid {border};");
        Rule(".cpu-popup .stats .stat-item, .memory-popup .stats .stat-item", $"color: {fg}; background-color: transparent;");

        Rule(".context-menu", $"background-color: {bg}; border: 1px solid {accent}; color: {fg}; padding: 4px 0;");
        Rule(".tooltip", $"background-color: {bg}; border: 1px solid {border}; color: {fg}; padding: 4px 8px; font-size: 12px;");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ files

    public static void Install(IntegrationContext ctx, Palette palette, string winMarchyExe, YasbOptions options)
    {
        ctx.WriteFile(ctx.Loc.YasbConfig, Config(winMarchyExe, ctx.Settings, options));
        ctx.WriteFile(ctx.Loc.YasbStyles, Styles(palette));
        ctx.Report(L.T("integrations.yasb.done", ("path", ctx.Loc.YasbDir)));
    }

    /// <summary>The entries changed (Tiling page). YASB reloads the file by itself. False if WinMarchy's bar is not set up.</summary>
    public static bool WriteConfig(IntegrationContext ctx, string winMarchyExe, YasbOptions options)
    {
        if (!IsInstalled(ctx.Loc)) return false;
        ctx.WriteFile(ctx.Loc.YasbConfig, Config(winMarchyExe, ctx.Settings, options));
        return true;
    }

    /// <summary>Theme change: a new stylesheet, which YASB picks up by itself.</summary>
    public static void ApplyTheme(IntegrationContext ctx, Palette palette)
    {
        if (!IsInstalled(ctx.Loc)) return;
        ctx.WriteFile(ctx.Loc.YasbStyles, Styles(palette));
    }
}

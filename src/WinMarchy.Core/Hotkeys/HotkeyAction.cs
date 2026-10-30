namespace WinMarchy.Core.Hotkeys;

public enum ActionKind
{
    /// <summary>GlazeWM command(s), separated by ";": "wm:move --workspace 2;focus --workspace 2".</summary>
    Wm,
    /// <summary>A program WinMarchy knows how to find: "app:terminal".</summary>
    App,
    /// <summary>A website in its own app window (Edge --app), like Omarchy's web apps: "web:https://maps.google.com/".</summary>
    Web,
    /// <summary>A link or settings page Windows opens: "open:ms-settings:bluetooth".</summary>
    Open,
    /// <summary>Another key combination: "send:Win+V".</summary>
    Send,
    /// <summary>A WinMarchy function: "do:theme-menu", "do:menu system".</summary>
    Do,
    /// <summary>A program or script of the user: "run:C:\Tools\login.ps1".</summary>
    Run,
}

/// <summary>What a shortcut does, parsed from "kind:value".</summary>
public sealed record HotkeyAction(ActionKind Kind, string Value)
{
    /// <summary>GlazeWM commands that may be bound. shell-exec stays out: programs start through WinMarchy.</summary>
    public static readonly IReadOnlySet<string> WmVerbs = new HashSet<string>(StringComparer.Ordinal)
    {
        "close", "focus", "move", "move-workspace", "resize", "size", "position", "set-floating", "set-tiling",
        "set-fullscreen", "set-minimized", "toggle-floating", "toggle-fullscreen", "toggle-minimized", "toggle-tiling",
        "toggle-tiling-direction", "set-tiling-direction", "wm-cycle-focus", "wm-reload-config", "wm-redraw",
        "wm-toggle-pause", "set-transparency", "set-title-bar-visibility", "adjust-borders",
        "wm-enable-binding-mode", "wm-disable-binding-mode",
    };

    /// <summary>Programs behind "app:…" (see AppLauncher for how each is found).</summary>
    public static readonly IReadOnlyList<string> Apps = new[]
    {
        "terminal", "browser", "browser2", "browser-private", "files", "editor", "launcher", "calculator", "taskmanager",
        "settings", "explorer", "mail", "calendar", "teams", "notes", "word", "herdr", "winmarchy",
        "signal", "telegram", "whatsapp", "spotify", "obsidian", "bitwarden", "notepad", "stackfield", "pi", "lunatask",
    };

    /// <summary>WinMarchy functions behind "do:…" (first word; some take an argument).</summary>
    public static readonly IReadOnlyList<string> Commands = new[]
    {
        "menu", "theme-menu", "keybindings", "wallpaper-next", "bar-toggle", "taskbar-toggle",
        "layout-next", "layout-prev", "layout", "scratch-toggle", "scratch-store", "workspace-next", "workspace-prev",
        "scale-up", "scale-down", "lock", "show-time", "show-battery", "keep-awake", "gaps-toggle",
        "transparency-toggle", "move-window-monitor", "restore-minimized", "copy", "paste", "reload",
        "toggle-split", "desktop-new", "desktop-next", "desktop-prev", "wallpaper-menu",
        "focus", "swap", "to-workspace", "screenshot", "about", "bar-shortcut", "bar-menu",
    };

    /// <summary>Commands WinMarchy uses itself (the bar's own buttons) and the shortcut editor does not offer.</summary>
    public static readonly IReadOnlySet<string> InternalCommands = new HashSet<string>(StringComparer.Ordinal) { "bar-shortcut" };

    /// <summary>Link schemes "open:" may use.</summary>
    public static readonly IReadOnlySet<string> OpenSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "https", "http", "mailto", "ms-settings", "ms-availablenetworks", "ms-actioncenter", "ms-clock", "ms-phone",
        "ms-screenclip", "msteams", "ms-teams", "onenote", "outlookcal", "spotify", "tg", "whatsapp", "obsidian",
        "ms-windows-store",
    };

    public static bool TryParse(string? text, out HotkeyAction action, out string error)
    {
        action = new HotkeyAction(ActionKind.Do, "");
        error = "";
        var colon = text?.IndexOf(':') ?? -1;
        if (text == null || colon <= 0)
        {
            error = L.T("hotkeys.errors.actionFormat", ("action", text ?? ""));
            return false;
        }
        var kindText = text[..colon].Trim().ToLowerInvariant();
        var value = text[(colon + 1)..].Trim();
        ActionKind? kind = kindText switch
        {
            "wm" => ActionKind.Wm, "app" => ActionKind.App, "web" => ActionKind.Web, "open" => ActionKind.Open,
            "send" => ActionKind.Send, "do" => ActionKind.Do, "run" => ActionKind.Run, _ => null,
        };
        if (kind == null || value.Length == 0)
        {
            error = L.T("hotkeys.errors.actionFormat", ("action", text));
            return false;
        }
        action = new HotkeyAction(kind.Value, value);
        error = action.Problem() ?? "";
        return error.Length == 0;
    }

    public static HotkeyAction Parse(string text) =>
        TryParse(text, out var a, out var e) ? a : throw new FormatException(e);

    /// <summary>Why the action cannot run, or null.</summary>
    public string? Problem()
    {
        switch (Kind)
        {
            case ActionKind.Wm:
                foreach (var command in WmCommands)
                {
                    var verb = command.Split(' ', 2)[0];
                    if (!WmVerbs.Contains(verb)) return L.T("hotkeys.errors.wmVerb", ("verb", verb));
                }
                return null;
            case ActionKind.App:
                return Apps.Contains(Value) ? null : L.T("hotkeys.errors.unknownApp", ("app", Value));
            case ActionKind.Web:
                return Uri.TryCreate(Value, UriKind.Absolute, out var web) && web.Scheme == "https"
                    ? null : L.T("hotkeys.errors.webUrl", ("url", Value));
            case ActionKind.Open:
                var scheme = Value.Split(':', 2)[0];
                return OpenSchemes.Contains(scheme) ? null : L.T("hotkeys.errors.openScheme", ("scheme", scheme));
            case ActionKind.Send:
                return KeyCombo.TryParse(Value, out _, out var keyError) ? null : keyError;
            case ActionKind.Do:
                return Commands.Contains(Value.Split(' ', 2)[0]) ? null : L.T("hotkeys.errors.unknownCommand", ("command", Value));
            case ActionKind.Run:
                return Value.Length > 0 ? null : L.T("hotkeys.errors.runEmpty");
        }
        return null;
    }

    public IEnumerable<string> WmCommands =>
        Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public override string ToString() => Kind.ToString().ToLowerInvariant() + ":" + Value;
}

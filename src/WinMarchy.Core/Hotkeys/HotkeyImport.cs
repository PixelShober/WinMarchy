using System.Text.RegularExpressions;

namespace WinMarchy.Core.Hotkeys;

/// <summary>One shortcut found in an imported file, and what WinMarchy would make of it.</summary>
/// <param name="Problem">Why it cannot be taken over (no Windows counterpart, bad keys); null if it can.</param>
/// <param name="Replaces">The label of what the keys do now, if they are taken.</param>
/// <param name="Same">The keys already do exactly this.</param>
public sealed record ImportCandidate(string Keys, string Do, string Label, string Origin, string? Problem, string? Replaces, bool Same)
{
    /// <summary>Ticked at first: it can be taken over, it is new, and it takes no keys that are in use.</summary>
    public bool Suggested => Problem == null && !Same && Replaces == null;

    public HotkeyBinding ToBinding() => new()
    {
        Keys = Keys,
        Do = Do,
        Group = Do.StartsWith("do:") ? "utilities" : Do.StartsWith("wm:") ? "tiling" : "apps",
        Label = LocalizedText.Same(Label),
    };
}

/// <summary>What an import file holds: shortcuts to choose from, plus an export's favourites and switched-off keys.</summary>
public sealed record ImportResult(IReadOnlyList<ImportCandidate> Candidates, CustomHotkeys? Export);

/// <summary>
/// Reads shortcuts from another setup so the user can pick which to take over: a WinMarchy export
/// (hotkeys.json), Omarchy's bindings.lua (<c>o.bind("SUPER + SHIFT + T", "Telegram", "…")</c>) or
/// Hyprland's bindings.conf (<c>bindd = SUPER SHIFT, M, Music, exec, …</c>). Linux commands are
/// translated where Windows has the same thing: web apps, Spotify links, known programs.
/// </summary>
public static class HotkeyImport
{
    static readonly Regex LuaBind = new(@"^\s*o\.bind\(\s*""(?<keys>[^""]+)""\s*,\s*(?:""(?<label>[^""]*)""|nil)\s*,\s*""(?<cmd>(?:[^""\\]|\\.)*)""", RegexOptions.Compiled);
    static readonly Regex ConfBind = new(@"^\s*bind[a-z]*\s*=\s*(?<rest>.+)$", RegexOptions.Compiled);
    static readonly Regex Url = new(@"https://[^\s""'\\]+", RegexOptions.Compiled);
    static readonly Regex SpotifyUri = new(@"spotify:(playlist|album|track|artist):[A-Za-z0-9]+", RegexOptions.Compiled);

    /// <summary>Programs WinMarchy knows, by a word in the Linux command.</summary>
    static readonly (string Word, string Action)[] Known =
    {
        ("omarchy-menu-keybindings", "do:keybindings"), ("omarchy-capture-screenshot", "do:screenshot"), ("screenshot", "do:screenshot"),
        ("omarchy-menu", "do:menu"), ("omarchy-lock-screen", "do:lock"), ("hyprlock", "do:lock"),
        ("spotify", "app:spotify"), ("telegram", "app:telegram"), ("whatsapp", "app:whatsapp"), ("signal", "app:signal"),
        ("obsidian", "app:obsidian"), ("1password", "app:bitwarden"), ("bitwarden", "app:bitwarden"), ("nautilus", "app:files"),
        ("omarchy-launch-browser", "app:browser"), ("$browser", "app:browser"), ("chromium", "app:browser"), ("firefox", "app:browser"),
        ("$terminal", "app:terminal"), ("xdg-terminal-exec", "app:terminal"), ("alacritty", "app:terminal"), ("ghostty", "app:terminal"),
        ("kitty", "app:terminal"), ("btop", "app:taskmanager"),
        ("omarchy-launch-editor", "app:editor"), ("nvim", "app:editor"), ("typora", "app:word"), ("libreoffice", "app:word"),
        ("thunderbird", "app:mail"), ("gnome-calculator", "app:calculator"), ("qalculate", "app:calculator"),
    };

    public static ImportResult Read(string file, HotkeySet current)
    {
        var text = File.ReadAllText(file);
        if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || text.TrimStart().StartsWith('{'))
        {
            var export = CustomHotkeys.Import(file);
            var list = export.Bindings.Select(b => Candidate(b.Keys, b.Do, b.Describe(), "WinMarchy", current)).ToList();
            return new ImportResult(list, export);
        }
        var found = new List<ImportCandidate>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.TrimStart().StartsWith("--") || line.TrimStart().StartsWith('#')) continue;
            if (LuaBind.Match(line) is { Success: true } lua)
            {
                var label = lua.Groups["label"].Success ? lua.Groups["label"].Value : "";
                found.Add(FromCommand(lua.Groups["keys"].Value, label, lua.Groups["cmd"].Value.Replace("\\\"", "\""), current));
            }
            else if (ConfBind.Match(line) is { Success: true } conf && FromConf(conf.Groups["rest"].Value, current) is { } c)
            {
                found.Add(c);
            }
        }
        // The last binding of the same keys wins, as in the file.
        var last = found.GroupBy(c => c.Keys, StringComparer.OrdinalIgnoreCase).Select(g => g.Last()).ToList();
        return new ImportResult(last, null);
    }

    /// <summary>Hyprland dispatchers with a WinMarchy counterpart ("{0}" is the argument).</summary>
    static readonly Dictionary<string, string> Dispatchers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["killactive"] = "wm:close", ["togglefloating"] = "wm:toggle-floating --centered", ["fullscreen"] = "wm:toggle-fullscreen",
        ["togglesplit"] = "do:toggle-split", ["workspace"] = "wm:focus --workspace {0}", ["movetoworkspace"] = "do:to-workspace {0}",
        ["movefocus"] = "do:focus {0}", ["swapwindow"] = "do:swap {0}", ["movewindow"] = "do:swap {0}",
        ["togglespecialworkspace"] = "do:scratch-toggle", ["movetoworkspacesilent"] = "do:scratch-store",
    };

    /// <summary>"SUPER SHIFT, M, Music, exec, cmd" (bindd), "SUPER SHIFT, M, exec, cmd" (bind) or a dispatcher.</summary>
    static ImportCandidate? FromConf(string rest, HotkeySet current)
    {
        var parts = rest.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 3) return null;
        var keys = (parts[0].Replace(' ', '+') + "+" + parts[1]).Trim('+');
        int exec = Array.FindIndex(parts, p => p == "exec");
        if (exec >= 2)
        {
            var label = exec >= 3 ? parts[2] : "";
            return FromCommand(keys, label, string.Join(", ", parts.Skip(exec + 1)), current);
        }
        // bindd = MODS, KEY, Label, dispatcher, arg   or   bind = MODS, KEY, dispatcher, arg
        int at = Array.FindIndex(parts, 2, p => Dispatchers.ContainsKey(p));
        if (at < 0) return null;
        var arg = at + 1 < parts.Length ? parts[at + 1] : "";
        arg = arg switch { "l" => "left", "r" => "right", "u" => "up", "d" => "down", _ => arg };
        // Only the scratchpad ("special") is moved to silently in WinMarchy.
        if (parts[at].Equals("movetoworkspacesilent", StringComparison.OrdinalIgnoreCase) && !arg.StartsWith("special")) return null;
        var action = string.Format(Dispatchers[parts[at]], arg).Trim();
        return Candidate(keys, action, at >= 3 ? parts[2] : Describe(action), parts[at] + " " + arg, current);
    }

    static ImportCandidate FromCommand(string keys, string label, string command, HotkeySet current)
    {
        var action = Map(command);
        if (label.Length == 0) label = action != null ? Describe(action) : command;
        if (action == null)
            return new ImportCandidate(Normalize(keys) ?? keys, "", label, command, L.T("hotkeys.importNoCounterpart"), null, false);
        return Candidate(keys, action, label, command, current);
    }

    static ImportCandidate Candidate(string keys, string action, string label, string origin, HotkeySet current)
    {
        if (Normalize(keys) is not { } normalized)
            return new ImportCandidate(keys, action, label, origin, L.T("hotkeys.importBadKeys", ("keys", keys)), null, false);
        if (KeyCombo.Parse(normalized).ReservedReason() is { } reserved)
            return new ImportCandidate(normalized, action, label, origin, reserved, null, false);
        if (!HotkeyAction.TryParse(action, out _, out var error))
            return new ImportCandidate(normalized, action, label, origin, error, null, false);
        var now = current.Active.FirstOrDefault(b => b.Keys == normalized);
        return new ImportCandidate(normalized, action, label, origin, null, now != null && now.Do != action ? now.Describe() : null, now?.Do == action);
    }

    /// <summary>"SUPER + SHIFT + T" or "SUPER+SHIFT+RETURN" → "Win+Shift+Enter"; null if it is not a key combination.</summary>
    public static string? Normalize(string keys) =>
        KeyCombo.TryParse(keys.Replace(" ", ""), out var combo, out _) ? combo.ToString() : null;

    /// <summary>The WinMarchy action for a Linux command, or null if Windows has nothing like it.</summary>
    public static string? Map(string command)
    {
        if (SpotifyUri.Match(command) is { Success: true } spotify) return "open:" + spotify.Value;
        if (Url.Match(command) is { Success: true } url) return "web:" + url.Value.TrimEnd(')', '"', '\'');
        var lower = command.ToLowerInvariant();
        foreach (var (word, action) in Known)
            if (lower.Contains(word)) return action;
        return null;
    }

    static string Describe(string action) =>
        action.StartsWith("app:") ? L.T("apps." + action[4..])
        : action.StartsWith("do:") ? L.T("commands." + action[3..].Split(' ')[0])
        : action[(action.IndexOf(':') + 1)..];
}

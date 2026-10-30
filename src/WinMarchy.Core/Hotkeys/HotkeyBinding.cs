using System.Text.Json.Serialization;

namespace WinMarchy.Core.Hotkeys;

/// <summary>A text in German and English, as the profiles carry it.</summary>
public sealed class LocalizedText
{
    public string De { get; set; } = "";
    public string En { get; set; } = "";

    public override string ToString()
    {
        var text = L.Language == "de" ? De : En;
        if (text.Length == 0) text = De.Length > 0 ? De : En;
        return L.Swiss ? L.ToSwiss(text) : text;
    }

    public static LocalizedText Same(string text) => new() { De = text, En = text };
}

/// <summary>One shortcut: keys, what it does, how it is described.</summary>
public sealed class HotkeyBinding
{
    public string Keys { get; set; } = "";

    /// <summary>"kind:value", see <see cref="HotkeyAction"/>.</summary>
    public string Do { get; set; } = "";

    /// <summary>tiling, workspaces, apps, menus, utilities or system.</summary>
    public string Group { get; set; } = "apps";

    public LocalizedText? Label { get; set; }

    /// <summary>The Omarchy binding this one stands for, when it differs (e.g. "SUPER + L").</summary>
    public string? Omarchy { get; set; }

    public LocalizedText? Note { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Expands "{n}" (1 … 9, 0) and "{ws}" (1 … 10) to all ten workspaces.</summary>
    public string? Repeat { get; set; }

    /// <summary>The profile files it under "Optional": Win+K lists it at the end, in its own section.</summary>
    public bool Optional { get; set; }

    /// <summary>Starred by the user: shown at the top of Win+K.</summary>
    [JsonIgnore]
    public bool Favorite { get; set; }

    /// <summary>For the ten bindings "Win+{n}" expands to: the keys as the profile writes them, so lists can show one row.</summary>
    [JsonIgnore]
    public string? Template { get; set; }

    /// <summary>The label with "{ws}" still in it, for that one row.</summary>
    [JsonIgnore]
    public LocalizedText? TemplateLabel { get; set; }

    /// <summary>"business", "personal" or "custom".</summary>
    [JsonIgnore]
    public string Source { get; set; } = "custom";

    /// <summary>The binding of a lower profile this one replaces.</summary>
    [JsonIgnore]
    public HotkeyBinding? Replaces { get; set; }

    [JsonIgnore]
    public KeyCombo? Combo => KeyCombo.TryParse(Keys, out var c, out _) ? c : null;

    [JsonIgnore]
    public HotkeyAction? Action => HotkeyAction.TryParse(Do, out var a, out _) ? a : null;

    public string Describe() => Label?.ToString() ?? Do;

    public HotkeyBinding Copy() => (HotkeyBinding)MemberwiseClone();
}

public sealed class HotkeyProfile
{
    public string Id { get; set; } = "";
    public LocalizedText? Title { get; set; }
    public List<HotkeyBinding> Bindings { get; set; } = new();
}

/// <summary>C:\WinMarchy\Config\hotkeys.json: the user's own shortcuts on top of the profiles.</summary>
public sealed class CustomHotkeys
{
    /// <summary>New shortcuts and replacements of profile shortcuts (same keys).</summary>
    public List<HotkeyBinding> Bindings { get; set; } = new();

    /// <summary>Profile shortcuts the user switched off, by keys ("Win+Shift+A").</summary>
    public List<string> Disabled { get; set; } = new();

    /// <summary>The user's own favourites (true) and non-favourites (false), by keys.</summary>
    public Dictionary<string, bool> Favorites { get; set; } = new();

    public static CustomHotkeys Load(string file)
    {
        try
        {
            if (File.Exists(file)) return Json.Deserialize<CustomHotkeys>(File.ReadAllText(file)) ?? new CustomHotkeys();
        }
        catch (Exception ex)
        {
            Log.Warn(L.T("hotkeys.customUnreadable", ("detail", ex.Message)));
        }
        return new CustomHotkeys();
    }

    public void Save(string file) => FileSafety.WriteText(file, Json.Serialize(this, indented: true));

    /// <summary>Reads an exported file; throws if it is not one.</summary>
    public static CustomHotkeys Import(string file) =>
        Json.Deserialize<CustomHotkeys>(File.ReadAllText(file)) ?? throw new WinMarchyException("hotkeysImportInvalid");
}

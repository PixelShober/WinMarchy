using WinMarchy.Core.Backup;
using WinMarchy.Core.Themes;

namespace WinMarchy.Core.Integrations;

/// <summary>Windows as it comes: light or dark mode, the accent colour and the wallpaper of its own theme.</summary>
public sealed record PlainLook(bool AppsLight, bool SystemLight, string Accent, bool AutoColorization, string? Wallpaper);

/// <summary>
/// The look of a plain Windows, for the pause: what Windows' default theme (aero.theme) sets, so the
/// PC looks like any other office PC, whatever WinMarchy or an earlier tool changed before. A
/// wallpaper set by group policy wins over the theme's.
/// </summary>
public static class WindowsDefaults
{
    /// <summary>Where a group policy puts the user's wallpaper ("Desktop Wallpaper").</summary>
    public const string PolicyKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";

    /// <summary>Windows' blue, the accent of its default theme.</summary>
    const string WindowsBlue = "#0078D4";

    /// <summary>
    /// The default look from aero.theme's text. Without the theme file (or without its lines),
    /// Windows 11 is light all over and Windows 10 has light apps with a dark taskbar, as they come.
    /// </summary>
    /// <param name="windowsDir">The Windows folder, for "%SystemRoot%" and "%WinDir%" in the theme.</param>
    /// <param name="windows11">Windows 11 or later (build 22000 and up).</param>
    public static PlainLook Parse(string? theme, string windowsDir, bool windows11)
    {
        var sections = Sections(theme ?? "");
        string? Value(string section, string name) =>
            sections.TryGetValue(section, out var values) && values.TryGetValue(name, out var v) ? v : null;

        bool Light(string? mode, bool fallback) => mode == null ? fallback : !mode.Equals("Dark", StringComparison.OrdinalIgnoreCase);
        bool apps = Light(Value("VisualStyles", "AppMode"), true);
        bool system = Light(Value("VisualStyles", "SystemMode"), windows11);

        var accent = WindowsBlue;
        // "0XC40078D4": alpha, then red, green, blue.
        if (Value("VisualStyles", "ColorizationColor") is { } colour &&
            long.TryParse(colour.Replace("0X", "", StringComparison.OrdinalIgnoreCase), System.Globalization.NumberStyles.HexNumber, null, out var argb))
            accent = "#" + (argb & 0xFFFFFF).ToString("X6");
        bool auto = Value("VisualStyles", "AutoColorization") == "1";

        var wallpaper = Value(@"Control Panel\Desktop", "Wallpaper") is { Length: > 0 } w
            ? Expand(w, windowsDir)
            : Path.Combine(windowsDir, "Web", "Wallpaper", "Windows", "img0.jpg");
        return new PlainLook(apps, system, accent, auto, wallpaper);
    }

    /// <summary>The default look on this PC: aero.theme from the Windows folder, a policy wallpaper first.</summary>
    public static PlainLook Read(IUserRegistry registry)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var file = Path.Combine(windows, "Resources", "Themes", "aero.theme");
        string? text = null;
        try
        {
            if (File.Exists(file)) text = File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Debug($"{file}: {ex.Message}");
        }
        var look = Parse(text, windows, Environment.OSVersion.Version.Build >= 22000);
        if (registry.Read(PolicyKey, "Wallpaper")?.Text is { Length: > 0 } policy) look = look with { Wallpaper = Expand(policy, windows) };
        if (look.Wallpaper != null && !File.Exists(look.Wallpaper)) look = look with { Wallpaper = null };
        return look;
    }

    /// <summary>
    /// The look as the values WinMarchy's theme writes (<see cref="WindowsAppearance.Values"/>) and
    /// the wallpaper, in the form a restore point puts back. Without a wallpaper file, the wallpaper stays.
    /// </summary>
    public static List<CapturedItem> Items(PlainLook look)
    {
        var now = DateTime.Now;
        CapturedItem Value(string key, string name, RegistryData data) =>
            new() { Kind = "registry", Target = key + "|" + name, Existed = true, Registry = data, Captured = now };
        var items = new List<CapturedItem>
        {
            Value(WindowsAppearance.PersonalizeKey, "AppsUseLightTheme", RegistryData.Dword(look.AppsLight ? 1 : 0)),
            Value(WindowsAppearance.PersonalizeKey, "SystemUsesLightTheme", RegistryData.Dword(look.SystemLight ? 1 : 0)),
            Value(WindowsAppearance.DwmKey, "AccentColor", RegistryData.Dword(ColorMath.ToAbgr(look.Accent))),
            Value(WindowsAppearance.DwmKey, "ColorizationColor", RegistryData.Dword(ColorMath.ToArgbInt(look.Accent, 0xC4))),
            Value(WindowsAppearance.AccentKey, "AccentColorMenu", RegistryData.Dword(ColorMath.ToAbgr(look.Accent))),
            Value(WindowsAppearance.DesktopKey, "AutoColorization", RegistryData.Str(look.AutoColorization ? "1" : "0")),
        };
        if (look.Wallpaper != null)
            items.Add(new CapturedItem { Kind = "wallpaper", Target = "wallpaper", Existed = true, OriginalPath = look.Wallpaper, Captured = now });
        return items;
    }

    static string Expand(string path, string windowsDir) => path
        .Replace("%SystemRoot%", windowsDir, StringComparison.OrdinalIgnoreCase)
        .Replace("%WinDir%", windowsDir, StringComparison.OrdinalIgnoreCase);

    /// <summary>An INI file's sections (names without brackets) with their name=value lines.</summary>
    static Dictionary<string, Dictionary<string, string>> Sections(string text)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? current = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var name = line[1..^1];
                if (!result.TryGetValue(name, out current)) result[name] = current = new(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            var eq = line.IndexOf('=');
            if (current != null && eq > 0) current[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return result;
    }
}

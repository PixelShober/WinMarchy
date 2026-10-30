namespace WinMarchy.Core.Themes;

/// <summary>
/// The colours of one Omarchy theme with sensible fallbacks, so older or hand-made colors.toml files
/// with fewer keys still produce a complete palette.
/// </summary>
public sealed class Palette
{
    readonly OmarchyTheme _theme;

    public Palette(OmarchyTheme theme) => _theme = theme;

    public string Name => _theme.Name;
    public bool IsLight => _theme.IsLight || ColorMath.Luminance(Background) > 0.5;

    public string Get(string fallback, params string[] keys)
    {
        foreach (var k in keys)
            if (_theme.Colors.TryGetValue(k, out var v) && ColorMath.IsHex(v.ToLowerInvariant())) return v.ToLowerInvariant();
        return fallback;
    }

    public string Background => Get("#1a1b26", "background", "bg");
    public string Foreground => Get(IsLightGuess ? "#1a1b26" : "#c0caf5", "foreground", "fg");
    public string Accent => Get("#7aa2f7", "accent", "blue", "cyan");
    public string Selection => Get(ColorMath.Mix(Background, Accent, 0.25), "selection", "selection_background");
    public string Muted => Get(ColorMath.Mix(Background, Foreground, 0.4), "muted", "bright_black");
    public string DarkBackground => Get(ColorMath.Mix(Background, IsLightGuess ? "#ffffff" : "#000000", 0.25), "dark_background");
    public string LighterBackground => Get(ColorMath.Mix(Background, Foreground, 0.08), "lighter_background");
    public string DarkForeground => Get(ColorMath.Mix(Background, Foreground, 0.6), "dark_foreground");
    public string BrightForeground => Get(Foreground, "bright_foreground", "light_foreground");

    public string Red => Get("#f7768e", "red");
    public string Green => Get("#9ece6a", "green");
    public string Yellow => Get("#e0af68", "yellow");
    public string Blue => Get("#7aa2f7", "blue");
    public string Magenta => Get("#bb9af7", "magenta", "purple");
    public string Cyan => Get("#7dcfff", "cyan");
    public string BrightRed => Get(Red, "bright_red");
    public string BrightGreen => Get(Green, "bright_green");
    public string BrightYellow => Get(Yellow, "bright_yellow");
    public string BrightBlue => Get(Blue, "bright_blue");
    public string BrightMagenta => Get(Magenta, "bright_magenta");
    public string BrightCyan => Get(Cyan, "bright_cyan");

    /// <summary>Border of windows without focus: quiet, but visible.</summary>
    public string InactiveBorder => ColorMath.Mix(Background, Muted, 0.85);

    bool IsLightGuess => _theme.IsLight;
}

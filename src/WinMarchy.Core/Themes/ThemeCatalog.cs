using System.Text.RegularExpressions;

namespace WinMarchy.Core.Themes;

public sealed class ThemeAsset
{
    public string File { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}

public sealed class VsCodeThemeInfo
{
    public string Name { get; set; } = "";
    public string Extension { get; set; } = "";
}

public sealed class OmarchyTheme
{
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>"dark" or "light".</summary>
    public string Mode { get; set; } = "dark";
    public Dictionary<string, string> Colors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public VsCodeThemeInfo? Vscode { get; set; }
    public ThemeAsset? Preview { get; set; }
    public List<ThemeAsset> Backgrounds { get; set; } = new();

    public bool IsLight => Mode == "light";

    public Palette Palette => new(this);
}

public sealed class ThemeManifest
{
    public string Source { get; set; } = "";
    public string Tag { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string License { get; set; } = "";
    public List<OmarchyTheme> Themes { get; set; } = new();
}

/// <summary>
/// The Omarchy themes WinMarchy knows. The palettes are part of the exe (they work offline right away);
/// previews and wallpapers are downloaded on demand from the pinned Omarchy tag and checked by SHA-256.
/// </summary>
public static class ThemeCatalog
{
    static ThemeManifest? _manifest;

    public static ThemeManifest Manifest => _manifest ??= Load();

    public static IReadOnlyList<OmarchyTheme> All => Manifest.Themes;

    public static OmarchyTheme? Find(string? name) =>
        All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    public static OmarchyTheme Default => Find("tokyo-night") ?? All[0];

    static ThemeManifest Load()
    {
        using var stream = typeof(ThemeCatalog).Assembly.GetManifestResourceStream("WinMarchy.omarchy-themes.json")
            ?? throw new InvalidOperationException("omarchy-themes.json is not embedded");
        using var reader = new StreamReader(stream);
        return Json.Deserialize<ThemeManifest>(reader.ReadToEnd()) ?? new ThemeManifest();
    }

    /// <summary>Reads an Omarchy colors.toml (key = "value" lines) – for themes imported from a folder.</summary>
    public static OmarchyTheme ParseColorsToml(string name, string toml)
    {
        var theme = new OmarchyTheme { Name = name, Title = TitleOf(name) };
        foreach (var line in toml.Split('\n'))
        {
            var m = Regex.Match(line, "^\\s*([A-Za-z0-9_-]+)\\s*=\\s*[\"']([^\"']*)[\"']");
            if (!m.Success) continue;
            if (m.Groups[1].Value == "mode") theme.Mode = m.Groups[2].Value == "light" ? "light" : "dark";
            else theme.Colors[m.Groups[1].Value] = m.Groups[2].Value;
        }
        return theme;
    }

    public static string TitleOf(string name) =>
        string.Join(' ', name.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
}

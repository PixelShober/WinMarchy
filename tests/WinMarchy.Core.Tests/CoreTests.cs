using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using WinMarchy.Core.GlazeWm;
using WinMarchy.Core.Themes;
using WinMarchy.Core.Tools;
using WinMarchy.Core.Updates;

namespace WinMarchy.Core.Tests;

public class ColorMathTests
{
    [Fact]
    public void Contrast_follows_wcag()
    {
        Assert.Equal(21, ColorMath.Contrast("#000000", "#ffffff"), 1);
        Assert.Equal(1, ColorMath.Contrast("#7aa2f7", "#7aa2f7"), 3);
    }

    [Theory]
    [InlineData("#7aa2f7", "#ffffff", 3.0)]
    [InlineData("#e0af68", "#faf4ed", 4.5)]
    [InlineData("#1e66f5", "#101116", 4.5)]
    public void Legible_reaches_the_asked_contrast(string color, string background, double min) =>
        Assert.True(ColorMath.Contrast(ColorMath.Legible(color, background, min), background) >= min);

    [Fact]
    public void Mix_and_windows_formats()
    {
        Assert.Equal("#808080", ColorMath.Mix("#000000", "#ffffff", 0.5));
        Assert.Equal(unchecked((int)0xFFF7A27A), ColorMath.ToAbgr("#7aa2f7"));
        Assert.Equal("#FF7AA2F7", ColorMath.Argb("#7aa2f7"));
    }
}

public class ThemeCatalogTests
{
    [Fact]
    public void All_22_Omarchy_themes_with_valid_palettes()
    {
        Assert.Equal(22, ThemeCatalog.All.Count);
        Assert.Equal("tokyo-night", ThemeCatalog.Default.Name);
        foreach (var t in ThemeCatalog.All)
        {
            var p = t.Palette;
            foreach (var c in new[] { p.Background, p.Foreground, p.Accent, p.Red, p.Green, p.Blue })
                Assert.True(ColorMath.IsHex(c), $"{t.Name}: {c}");
            Assert.Equal(t.IsLight, p.IsLight);
        }
    }

    [Fact]
    public void Theme_assets_carry_a_checksum()
    {
        foreach (var t in ThemeCatalog.All)
        foreach (var a in t.Backgrounds.Append(t.Preview).OfType<ThemeAsset>())
        {
            Assert.Matches("^[0-9a-f]{64}$", a.Sha256);
            Assert.True(a.Size > 0);
        }
    }

    [Fact]
    public void Reads_colors_toml()
    {
        var theme = ThemeCatalog.ParseColorsToml("my-theme", "mode = \"light\"\naccent = \"#123456\"\nbackground = \"#FFFFFF\"\n");
        Assert.True(theme.IsLight);
        Assert.Equal("#123456", theme.Palette.Accent);
        Assert.Equal("My Theme", theme.Title);
    }
}

public class NetTests
{
    [Fact]
    public void Reads_digests_from_the_api()
    {
        const string json = """
            { "tag_name": "v3.10.0", "assets": [
              { "name": "standalone-glazewm-v3.10.0-x64.msi", "browser_download_url": "https://github.com/glzr-io/glazewm/releases/download/v3.10.0/standalone-glazewm-v3.10.0-x64.msi",
                "digest": "sha256:ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef0123456789" },
              { "name": "notes.txt", "browser_download_url": "https://example.invalid/notes.txt" } ] }
            """;
        var r = Net.ParseApiRelease(json);
        Assert.Equal("v3.10.0", r.Tag);
        Assert.Equal("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", r.Sha256["standalone-glazewm-v3.10.0-x64.msi"]);
        Assert.False(r.Sha256.ContainsKey("notes.txt"));
    }

    [Fact]
    public void Reads_digests_from_the_release_page()
    {
        const string html = """
            <li><a href="/AutoHotkey/AutoHotkey/releases/download/v2.0.19/AutoHotkey_2.0.19.zip" rel="nofollow">AutoHotkey_2.0.19.zip</a>
              <span>sha256:1111111111111111111111111111111111111111111111111111111111111111</span></li>
            <li><a href="/AutoHotkey/AutoHotkey/releases/download/v2.0.19/AutoHotkey_2.0.19_setup.exe">setup</a></li>
            """;
        var r = Net.ParseExpandedAssets(html, "AutoHotkey/AutoHotkey", "v2.0.19");
        Assert.Equal(2, r.Assets.Count);
        Assert.Equal(new string('1', 64), r.Sha256["AutoHotkey_2.0.19.zip"]);
        Assert.False(r.Sha256.ContainsKey("AutoHotkey_2.0.19_setup.exe"));
    }

    [Theory]
    [InlineData("glazewm", "standalone-glazewm-v3.10.0-x64.msi")]
    [InlineData("zebar", "zebar-v3.3.1-opt1-x64.msi")]
    [InlineData("autohotkey", "AutoHotkey_2.0.19.zip")]
    [InlineData("flow", "Flow-Launcher-Setup.exe")]
    [InlineData("pwsh", "PowerShell-7.5.4-win-x64.zip")]
    [InlineData("herdr", "herdr-windows-x86_64.zip")]
    [InlineData("pi", "pi-windows-x64.zip")]
    public void Tool_patterns_match_the_vendor_assets(string tool, string asset) =>
        Assert.Matches(ToolCatalog.Get(tool).Asset!, asset);

    [Fact]
    public void Tool_patterns_skip_the_wrong_variants()
    {
        Assert.DoesNotMatch(ToolCatalog.GlazeWm.Asset!, "standalone-glazewm-v3.10.0-arm64.msi");
        Assert.DoesNotMatch(ToolCatalog.PowerShell.Asset!, "PowerShell-7.5.4-win-arm64.zip");
        Assert.DoesNotMatch(ToolCatalog.AutoHotkey.Asset!, "AutoHotkey_1.1.37.02.zip");
        Assert.DoesNotMatch(ToolCatalog.Pi.Asset!, "pi-windows-arm64.zip");
        Assert.DoesNotMatch(ToolCatalog.Herdr.Asset!, "herdr-linux-x86_64");
    }
}

public class UpdateTests
{
    [Theory]
    [InlineData("1.2.0", "1.10.0", -1)]
    [InlineData("v2.0.0", "1.9.9", 1)]
    [InlineData("1.0.0-dev", "1.0.0", -1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    public void Compares_versions(string a, string b, int sign) =>
        Assert.Equal(sign, Math.Sign(AppInfo.CompareVersions(a, b)));

    [Fact]
    public void Reads_latest_json_as_the_release_writes_it()
    {
        // Shape of scripts/make-manifest.mjs
        const string json = """
            { "version": "1.3.0", "published": "2026-10-29T10:00:00.000Z",
              "exe": { "name": "WinMarchy.exe", "sha256": "abc", "size": 123 }, "notes": "https://github.com/PixelShober/WinMarchy/releases/tag/v1.3.0" }
            """;
        var m = Json.Deserialize<UpdateManifest>(json)!;
        Assert.Equal("1.3.0", m.Version);
        Assert.Equal("WinMarchy.exe", m.Exe.Name);
        Assert.Equal(123, m.Exe.Size);
    }

    [Fact]
    public void Next_version_script_matches_the_release_tags()
    {
        // The updater downloads releases/download/v<version>/WinMarchy.exe; the workflow must tag like that.
        var release = File.ReadAllText(Path.Combine(Repo.Root, ".github", "workflows", "release.yml"));
        Assert.Contains("gh release create \"v$VERSION\"", release);
        Assert.Contains("releases/download/v{manifest.Version}", File.ReadAllText(Path.Combine(Repo.Root, "src", "WinMarchy.Core", "Updates", "Updater.cs")));
    }
}

public class LayoutTests
{
    [Theory]
    [InlineData("dwindle", LayoutMode.Dwindle)]
    [InlineData("Columns", LayoutMode.Columns)]
    [InlineData("scrolling", LayoutMode.Columns)]
    [InlineData("floating", LayoutMode.Floating)]
    [InlineData("manual", LayoutMode.Manual)]
    [InlineData(null, LayoutMode.Dwindle)]
    public void Parses_layout_names(string? text, LayoutMode mode) => Assert.Equal(mode, LayoutEngine.Parse(text));
}

public class FileSafetyTests
{
    [Fact]
    public void Zip_entries_cannot_leave_the_target_folder()
    {
        using var tmp = new TempDir();
        var zip = tmp.File("evil.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            archive.CreateEntry("../outside.txt");
        Assert.Throws<WinMarchyException>(() => FileSafety.ExtractZip(zip, tmp.File("out"), CancellationToken.None));
        Assert.False(File.Exists(tmp.File("outside.txt")));
    }

    [Fact]
    public void Writes_atomically_without_leftovers()
    {
        using var tmp = new TempDir();
        var file = tmp.File("sub/config.json");
        FileSafety.WriteText(file, "{}");
        FileSafety.WriteText(file, "{ \"a\": 1 }");
        Assert.Equal("{ \"a\": 1 }", File.ReadAllText(file));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(file)!));
    }
}

public class LocaleTests
{
    static readonly Regex Placeholder = new(@"\{(\w+)\}");

    [Fact]
    public void German_and_English_have_the_same_keys_and_placeholders()
    {
        var de = L.Table("de");
        var en = L.Table("en");
        Assert.Empty(de.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(de.Keys));
        foreach (var (key, text) in de)
        {
            var a = Placeholder.Matches(text).Select(m => m.Groups[1].Value).OrderBy(x => x);
            var b = Placeholder.Matches(en[key]).Select(m => m.Groups[1].Value).OrderBy(x => x);
            Assert.True(a.SequenceEqual(b), key);
        }
    }

    [Fact]
    public void Every_hotkey_group_app_and_command_has_a_text()
    {
        var de = L.Table("de");
        foreach (var app in Hotkeys.HotkeyAction.Apps) Assert.True(de.ContainsKey("apps." + app), app);
        foreach (var cmd in Hotkeys.HotkeyAction.Commands) Assert.True(de.ContainsKey("commands." + cmd), cmd);
        foreach (var tool in ToolCatalog.All) Assert.True(de.ContainsKey("tools.roles." + tool.Id), tool.Id);
        foreach (var mode in Enum.GetNames<LayoutMode>()) Assert.True(de.ContainsKey("layouts." + mode.ToLowerInvariant()), mode);
    }

    [Theory]
    [InlineData("auto", "de-DE", "de", false)]
    [InlineData("auto", "de-CH", "de", true)]
    [InlineData("auto", "fr-FR", "en", false)]
    [InlineData("en", "de-DE", "en", false)]
    [InlineData("de-CH", "en-US", "de", true)]
    public void Resolves_the_language(string setting, string culture, string language, bool swiss) =>
        Assert.Equal((language, swiss), L.Resolve(setting, CultureInfo.GetCultureInfo(culture)));

    [Fact]
    public void Swiss_German_has_no_sharp_s() => Assert.Equal("Schliessen, Grösse", L.ToSwiss("Schließen, Größe"));
}

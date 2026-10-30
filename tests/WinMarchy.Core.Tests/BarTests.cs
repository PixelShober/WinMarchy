using System.Text.Json.Nodes;
using WinMarchy.Core.Backup;
using WinMarchy.Core.GlazeWm;
using WinMarchy.Core.Integrations;
using WinMarchy.Core.Themes;
using WinMarchy.Core.Tools;

namespace WinMarchy.Core.Tests;

/// <summary>YASB as the bar, Zebar until the user switches, and the extras around it.</summary>
public class BarTests
{
    const string Exe = @"C:\WinMarchy\App\WinMarchy.exe";
    static Palette Tokyo => ThemeCatalog.Default.Palette;

    /// <summary>The config without its two comment lines (YAML reads the rest as JSON).</summary>
    static JsonObject Parse(string yaml) =>
        Jsonc.ParseObject(string.Join('\n', yaml.Split('\n').Where(l => !l.StartsWith('#'))));

    static IEnumerable<string?> Side(JsonObject root, string side) =>
        root["bars"]![YasbBar.BarName]!["widgets"]![side]!.AsArray().Select(n => (string?)n);

    [Fact]
    public void Yasb_shows_omarchys_entries_unless_the_user_chose_others()
    {
        var root = Parse(YasbBar.Config(Exe, new UserSettings()));
        Assert.Equal(new[] { "logo", "workspaces" }, Side(root, "left"));
        Assert.Equal(new[] { "clock" }, Side(root, "center"));
        Assert.Equal(new[] { "tray", "network", "audio", "cpu", "battery" }, Side(root, "right"));
        Assert.Equal(Bars.Height, (int)root["bars"]![YasbBar.BarName]!["dimensions"]!["height"]!);
        // WinMarchy updates YASB; YASB's own updater would need admin rights.
        Assert.False((bool)root["update_check"]!);

        var chosen = Parse(YasbBar.Config(Exe, new UserSettings { BarModules = { ["cpu"] = false, ["memory"] = true, ["media"] = true } }));
        Assert.Equal(new[] { "media", "tray", "network", "audio", "memory", "battery" }, Side(chosen, "right"));
        Assert.Null(chosen["widgets"]!["cpu"]);
    }

    [Fact]
    public void Own_entries_start_winmarchy_directly_from_the_menu_and_by_number_from_the_bar()
    {
        var s = new UserSettings();
        s.BarShortcuts.Add(new BarShortcut { Title = "Tickets <neu> {heute}", Action = "web:https://example.com/?a=1&b=2", InBar = true });
        s.BarShortcuts.Add(new BarShortcut { Title = "Kaputt", Action = "nope" });
        s.BarShortcuts.Add(new BarShortcut { Title = "  ", Action = "app:terminal" });
        var root = Parse(YasbBar.Config(Exe, s));

        var menu = root["widgets"]!["logo"]!["options"]!["menu_list"]!.AsArray().OfType<JsonObject>().ToList();
        var own = menu.Single(i => ((string?)i["title"])?.StartsWith("Tickets") == true);
        // No cmd.exe in between: "&" in the action cannot do harm, and angle brackets cannot turn into markup.
        Assert.Equal(Exe, (string?)own["command"]);
        Assert.Equal(new[] { "--action", "web:https://example.com/?a=1&b=2" }, own["args"]!.AsArray().Select(a => (string?)a));
        Assert.DoesNotContain('<', (string?)own["title"] ?? "");
        Assert.DoesNotContain(menu, i => (string?)i["title"] == "Kaputt");

        // The button calls by its number; the action itself never reaches cmd.exe.
        Assert.Contains("shortcut1", Side(root, "right"));
        var button = root["widgets"]!["shortcut1"]!["options"]!;
        Assert.Equal($"exec \"{Exe}\" --action do:bar-shortcut 1", (string?)button["callbacks"]!["on_left"]);
        Assert.Equal("Tickets \u2039neu\u203A {{heute}}", (string?)button["label"]);
        Assert.Null(root["widgets"]!["shortcut2"]);
        Assert.Null(root["widgets"]!["shortcut3"]);
    }

    [Fact]
    public void Exec_callbacks_carry_only_plain_actions()
    {
        var yaml = YasbBar.Config(Exe, new UserSettings { BarModules = { ["memory"] = true, ["media"] = true, ["direction"] = true } });
        var root = Parse(yaml);
        foreach (var widget in root["widgets"]!.AsObject())
        {
            if (widget.Value!["options"]!["callbacks"] is not JsonObject callbacks) continue;
            foreach (var callback in callbacks)
            {
                var text = (string?)callback.Value ?? "";
                if (!text.StartsWith("exec ")) continue;
                var action = text[(text.IndexOf("--action ", StringComparison.Ordinal) + 9)..];
                Assert.Matches("^[a-z]+:[A-Za-z0-9:._-]+( [0-9]+)?$", action);
            }
        }
    }

    [Fact]
    public void Yasb_styles_suit_every_theme_and_hide_the_hidden_workspaces()
    {
        foreach (var theme in ThemeCatalog.All)
        {
            var css = YasbBar.Styles(theme.Palette);
            // YASB's CSS engine drops everything after "//".
            Assert.DoesNotContain("//", css);
            Assert.Equal(css.Count(c => c == '{'), css.Count(c => c == '}'));
            Assert.Contains(theme.Palette.Background, css);
        }
        var tokyo = YasbBar.Styles(Tokyo);
        Assert.Contains($".ws-btn.populated[text=\"{GlazeConfig.ScratchpadWorkspace}\"]", tokyo);
        Assert.Contains($".ws-btn.focused_empty[text=\"{GlazeConfig.StageWorkspace}\"]", tokyo);
    }

    [Fact]
    public void An_earlier_zebar_stays_until_the_user_switches()
    {
        using var temp = new TempDir();
        var loc = Locations.Under(temp.Path);
        Assert.Equal(Bars.Yasb, Bars.Chosen(new UserSettings(), loc));

        Directory.CreateDirectory(loc.ZebarPackDir);
        File.WriteAllText(Path.Combine(loc.ZebarPackDir, "zpack.json"), "{}");
        Assert.Equal(Bars.Zebar, Bars.Chosen(new UserSettings(), loc));
        Assert.Equal(Bars.Yasb, Bars.Chosen(new UserSettings { Bar = Bars.Yasb }, loc));
        // A new setup takes YASB unless Zebar was chosen on purpose.
        Assert.Equal(Bars.Yasb, Bars.ForSetup(new UserSettings()));
        Assert.Equal(Bars.Zebar, Bars.ForSetup(new UserSettings { Bar = Bars.Zebar }));
        Assert.Same(ToolCatalog.Yasb, Bars.Tool(Bars.Yasb));
    }

    [Fact]
    public void Removing_the_zebar_pack_can_be_undone()
    {
        using var temp = new TempDir();
        var loc = Locations.Under(temp.Path);
        Directory.CreateDirectory(loc.ZebarPackDir);
        File.WriteAllText(Path.Combine(loc.ZebarPackDir, "zpack.json"), "{}");
        File.WriteAllText(loc.ZebarSettings, ZebarPack.PatchSettings(null, enabled: true));
        var store = new BackupStore(temp.File("backups"), new MemoryUserRegistry(), null);
        var point = store.Begin("zebar-remove", "Zebar");

        ZebarPack.Uninstall(new IntegrationContext(loc, store, point, new UserSettings()));
        Assert.False(ZebarPack.IsInstalled(loc));
        Assert.DoesNotContain(ZebarPack.PackName, File.ReadAllText(loc.ZebarSettings));

        store.Restore(point);
        Assert.True(ZebarPack.IsInstalled(loc));
        Assert.Contains(ZebarPack.PackName, File.ReadAllText(loc.ZebarSettings));
    }

    [Fact]
    public void Glazewm_leaves_both_bars_alone()
    {
        var yaml = GlazeConfig.Generate(GlazeConfig.FromSettings(new UserSettings(), Tokyo, Bars.Height));
        Assert.Contains("window_process: { equals: 'yasb' }", yaml);
        Assert.Contains("window_process: { equals: 'zebar' }", yaml);
    }

    [Fact]
    public void Explorer_blur_is_registered_for_the_user_and_comes_out_again()
    {
        using var temp = new TempDir();
        var loc = Locations.Under(temp.Path);
        var dll = temp.File("ExplorerBlurMica.dll");
        File.WriteAllText(dll, "");
        var registry = new MemoryUserRegistry();
        var store = new BackupStore(temp.File("backups"), registry, null);
        var point = store.Begin("explorer-blur", "on");

        ExplorerBlur.Enable(new IntegrationContext(loc, store, point, new UserSettings()), dll, Tokyo);
        Assert.True(ExplorerBlur.IsOn(registry));
        Assert.Equal(dll, ExplorerBlur.RegisteredDll(registry));
        Assert.Equal(0xff, registry.Read(@"Software\Classes\Drive\shellex\FolderExtensions\" + ExplorerBlur.Clsid, "DriveMask")?.Number);
        var ini = File.ReadAllText(temp.File("config.ini"));
        var (r, g, b) = ColorMath.Rgb(Tokyo.Background);
        Assert.Contains($"r={r}\r\ng={g}\r\nb={b}", ini);

        // Undo takes both keys out as a whole.
        store.Restore(point);
        Assert.False(ExplorerBlur.IsOn(registry));
        Assert.False(registry.KeyExists(@"Software\Classes\CLSID\" + ExplorerBlur.Clsid));

        // Switching off is its own restore point; undoing that registers it again.
        ExplorerBlur.Enable(new IntegrationContext(loc, store, store.Begin("explorer-blur", "on"), new UserSettings()), dll, Tokyo);
        var off = store.Begin("explorer-blur", "off");
        ExplorerBlur.Disable(new IntegrationContext(loc, store, off, new UserSettings()));
        Assert.False(ExplorerBlur.IsOn(registry));
        store.Restore(off);
        Assert.True(ExplorerBlur.IsOn(registry));
    }

    [Fact]
    public void Starship_and_fastfetch_get_the_theme_and_load_only_where_they_can()
    {
        var toml = ShellExtras.StarshipToml(Tokyo);
        Assert.Contains($"success_symbol = \"[❯](bold {ColorMath.Legible(Tokyo.Accent, Tokyo.Background, 3.0)})\"", toml);
        Assert.Contains("ahead = \"⇡${count} \"", toml);

        var script = ShellExtras.ShellScript(@"C:\Win'Marchy\starship.exe", @"C:\WinMarchy\Tools\fastfetch\fastfetch.exe");
        // Starship only in full language mode (AppLocker), Fastfetch also there, but never in scripts.
        Assert.Contains("$winmarchyFull = $ExecutionContext.SessionState.LanguageMode -eq 'FullLanguage'", script);
        Assert.Contains(@"if ($winmarchyFull -and (Test-Path -LiteralPath 'C:\Win''Marchy\starship.exe'))", script);
        Assert.Contains(@"function fastfetch { & 'C:\WinMarchy\Tools\fastfetch\fastfetch.exe' --config", script);
        Assert.Contains("$Host.Name -eq 'ConsoleHost'", script);
        Assert.Contains("GetCommandLineArgs()", script);
        Assert.DoesNotContain("starship", ShellExtras.ShellScript(null, @"C:\ff.exe"));

        var hook = TerminalTheme.WithHook("", @"C:\WinMarchy\Runtime\colors.ps1", @"C:\WinMarchy\Runtime\shell-start.ps1");
        Assert.Contains(@"$winmarchyShell = 'C:\WinMarchy\Runtime\shell-start.ps1'", hook);
        Assert.Contains("if (Test-Path -LiteralPath $winmarchyShell)", hook);

        var fastfetch = Jsonc.ParseObject(ShellExtras.FastfetchConfig(Tokyo));
        var (r, g, b) = ColorMath.Rgb(ColorMath.Legible(Tokyo.Accent, Tokyo.Background, 3.0));
        Assert.Equal($"38;2;{r};{g};{b}", (string?)fastfetch["display"]!["color"]!["keys"]);
        Assert.Equal("colors", (string?)fastfetch["modules"]!.AsArray().Last());
        // The address in use and its MAC, for IT tickets.
        var ip = fastfetch["modules"]!.AsArray().OfType<JsonObject>().Single(m => (string?)m["type"] == "localip");
        Assert.True((bool)ip["showMac"]!);
        Assert.True((bool)ip["defaultRouteOnly"]!);
    }

    [Theory]
    [InlineData("yasb", "yasb-2.0.7-x64.msi")]
    [InlineData("starship", "starship-x86_64-pc-windows-msvc.zip")]
    [InlineData("fastfetch", "fastfetch-windows-amd64.zip")]
    [InlineData("explorerblurmica", "Release_x64.zip")]
    public void New_tool_patterns_match_the_vendor_assets(string tool, string asset) =>
        Assert.Matches(ToolCatalog.Get(tool).Asset!, asset);

    [Theory]
    [InlineData("yasb", "yasb-2.0.7-aarch64.msi")]
    [InlineData("starship", "starship-x86_64-pc-windows-msvc.zip.sha256")]
    [InlineData("fastfetch", "fastfetch-windows-amd64.7z")]
    public void New_tool_patterns_skip_other_files(string tool, string asset) =>
        Assert.DoesNotMatch(ToolCatalog.Get(tool).Asset!, asset);

    [Fact]
    public void Lunatask_is_found_but_never_downloaded()
    {
        Assert.Equal(InstallKind.None, ToolCatalog.Lunatask.Kind);
        Assert.Null(ToolCatalog.Lunatask.Repo);
        Assert.Contains("lunatask", Hotkeys.HotkeyAction.Apps);
        Assert.Contains(ToolCatalog.ExplorerBlurMica, ToolCatalog.Extras);
        Assert.True(ToolCatalog.ExplorerBlurMica.Experimental);
    }

    [Fact]
    public void Bar_menus_listen_for_keys_nobody_presses_and_wifi_falls_back_to_windows()
    {
        var root = Parse(YasbBar.Config(Exe, new UserSettings { BarModules = { ["memory"] = true, ["media"] = true } }));
        foreach (var (name, (module, keys, action)) in YasbBar.Menus)
        {
            var binding = root["widgets"]![module]!["options"]!["keybindings"]!.AsArray().OfType<JsonObject>().Single();
            Assert.Equal(keys.ToLowerInvariant(), (string?)binding["keys"]);
            Assert.Equal(action, (string?)binding["action"]);
            // WinMarchy sends exactly this combination.
            Assert.Equal(keys, Hotkeys.KeyCombo.Parse(keys).ToString());
        }
        Assert.Equal(YasbBar.Menus.Count, YasbBar.Menus.Values.Select(m => m.Keys).Distinct().Count());

        // Without the location for desktop apps Windows hands out no Wi-Fi list: Windows' own list instead.
        var noLocation = Parse(YasbBar.Config(Exe, new UserSettings(), new YasbOptions(WifiMenu: false)));
        var network = noLocation["widgets"]!["network"]!["options"]!;
        Assert.Equal($"exec \"{Exe}\" --action {YasbBar.NetworkListAction}", (string?)network["callbacks"]!["on_left"]);
        Assert.Empty(network["keybindings"]!.AsArray());
    }

    [Fact]
    public void Media_keys_can_be_sent_without_a_modifier()
    {
        Assert.True(Hotkeys.KeyCombo.TryParse("MediaPlayPause", out var play, out _));
        Assert.Equal("{Media_Play_Pause}", play.ToAhkSend());
        Assert.True(Hotkeys.HotkeyAction.TryParse("send:MediaPlayPause", out _, out _));
        Assert.False(Hotkeys.KeyCombo.TryParse("A", out _, out _));
    }

    [Theory]
    [InlineData("v2.0.8", "v2.0.7", null, false)]
    [InlineData("v2.0.7", "v2.0.7", null, true)]
    [InlineData("v2.0.7", null, "2.0.7", true)]
    [InlineData("v3.10.1-winmarchy.2", null, "3.10.1-winmarchy.1", false)]
    [InlineData("2.70.0", null, null, false)]
    public void A_program_is_updated_when_its_release_changed(string latest, string? installedTag, string? version, bool current) =>
        Assert.Equal(current, ToolInstaller.IsCurrent(latest, installedTag, version));

    [Fact]
    public void Windows_powershell_loads_profiles_only_under_remote_signed_or_looser()
    {
        Assert.True(PowerShellPolicy.LoadsProfiles("RemoteSigned"));
        Assert.True(PowerShellPolicy.LoadsProfiles("unrestricted"));
        Assert.False(PowerShellPolicy.LoadsProfiles("Restricted"));
        Assert.False(PowerShellPolicy.LoadsProfiles("AllSigned"));
    }
}

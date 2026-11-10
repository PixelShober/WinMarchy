using System.Text.Json;
using System.Text.Json.Nodes;
using WinMarchy.Core.Backup;
using WinMarchy.Core.Hotkeys;
using WinMarchy.Core.Integrations;
using WinMarchy.Core.Themes;
using WinMarchy.Core.Tools;

namespace WinMarchy.Core.Tests;

/// <summary>Fluent Search behind the Win key, its settings, and an old Flow Launcher retired.</summary>
public class LauncherTests
{
    static readonly Palette Tokyo = ThemeCatalog.Default.Palette;

    static JsonNode? Value(string page, string name) =>
        JsonNode.Parse(page)!.AsArray().OfType<JsonObject>().First(i => i["SettingName"]!.GetValue<string>() == name)["Value"];

    [Fact]
    public void Fluent_search_is_the_launcher_and_flow_is_gone()
    {
        Assert.Null(ToolCatalog.Find("flow"));
        Assert.Contains(ToolCatalog.FluentSearch, ToolCatalog.All);
        Assert.DoesNotContain(ToolCatalog.FluentSearch, ToolCatalog.Extras);
        Assert.Contains("FluentSearch.exe", AhkScript.Generate(HotkeySet.Build(new CustomHotkeys()),
            new AhkContext(@"C:\WinMarchy\App\WinMarchy.exe", @"C:\WinMarchy\Tools\glazewm\cli\glazewm.exe", true)));
    }

    [Fact]
    public void The_win_key_script_asks_for_the_chosen_launcher()
    {
        var script = AhkScript.Generate(HotkeySet.Build(new CustomHotkeys()),
            new AhkContext(@"C:\WinMarchy\App\WinMarchy.exe", @"C:\WinMarchy\Tools\glazewm\cli\glazewm.exe", true, LauncherProcess: "FluentSearch.exe"));
        Assert.Contains("global LauncherProcess := \"FluentSearch.exe\"", script);
        Assert.Contains("ProcessExist(LauncherProcess)", script);
    }

    [Fact]
    public void Fluent_search_comes_from_the_newest_nightly_and_keeps_its_data_on_updates()
    {
        var tool = ToolCatalog.FluentSearch;
        Assert.True(tool.PreRelease);
        Assert.Equal(new[] { "Blast" }, tool.KeepFolders);
        Assert.Matches(tool.Asset!, "fluent-search-portable-x64.zip");
    }

    [Fact]
    public void The_newest_release_may_be_a_nightly()
    {
        var json = """
        [ { "tag_name": "1.5.0.7", "draft": true, "assets": [] },
          { "tag_name": "1.5.0.6", "draft": false, "prerelease": true, "assets": [
            { "name": "fluent-search-portable-x64.zip", "browser_download_url": "https://example.com/fs.zip", "digest": "sha256:14F9" } ] } ]
        """;
        var release = Net.NewestOf(json)!;
        Assert.Equal("1.5.0.6", release.Tag);
        Assert.Equal("14f9", release.Sha256["fluent-search-portable-x64.zip"]);
        Assert.Equal("1.5.0.6", Net.NewestTagOnPage("<a href=\"/adirh3/Fluent-Search/releases/tag/1.5.0.6\">1.5.0.6 Nightly</a>", "adirh3/Fluent-Search"));
    }

    [Fact]
    public void The_templates_carry_no_personal_data()
    {
        var files = FluentSearchConfig.TemplateFiles();
        Assert.Contains("System.json", files);
        Assert.Contains("Appearance.json", files);
        foreach (var file in files)
        {
            var text = FluentSearchConfig.Template(file, @"C:\Users\Test");
            Assert.NotNull(JsonNode.Parse(text));
            foreach (var personal in new[] { "Mehring", "stadt-frankfurt", "Frankfurt", "\"UserId\"", "Z:\\\\", "{USERPROFILE}" })
                Assert.DoesNotContain(personal, text);
        }
        Assert.Contains(@"C:\\Users\\Test\\AppData\\Local\\Microsoft\\Edge", FluentSearchConfig.Template("Chromium.json", @"C:\Users\Test"));
    }

    [Fact]
    public void System_settings_get_the_hotkey_and_no_autostart()
    {
        var page = FluentSearchConfig.PatchSystem(FluentSearchConfig.Template("System.json", @"C:\Users\Test"));
        Assert.Equal(new[] { 0x12, 0x87 }, Value(page, "Search hotkey")!["HotKeys"]!.AsArray().Select(k => k!.GetValue<int>()));
        Assert.False(Value(page, "StartAtStartup")!.GetValue<bool>());
        Assert.False(Value(page, "ImprovedStartup")!.GetValue<bool>());
        // A setting the page does not have yet is added.
        Assert.Equal(0x87, Value(FluentSearchConfig.PatchSystem("[]"), "Search hotkey")!["HotKeys"]![1]!.GetValue<int>());
    }

    [Fact]
    public void Appearance_takes_the_theme_accent()
    {
        var page = FluentSearchConfig.PatchAppearance(FluentSearchConfig.Template("Appearance.json", @"C:\Users\Test"), Tokyo);
        Assert.Equal(FluentSearchConfig.Argb(Tokyo.Accent), Value(page, "ColorSetting")!["ColorValue"]!.GetValue<long>());
        Assert.True(Value(page, "Apply accent color on window borders")!.GetValue<bool>());
        Assert.Equal(1, Value(page, "WindowOpacity")!.GetValue<int>());
        Assert.Equal(0xFF7AA2F7L, FluentSearchConfig.Argb("#7aa2f7"));
    }

    [Fact]
    public async Task The_setup_writes_blast_settings_next_to_the_portable_exe_and_can_be_undone()
    {
        using var temp = new TempDir();
        var loc = Locations.Under(temp.Path);
        var exe = temp.File("fluent/FluentSearch.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, "");
        File.WriteAllText(temp.File("fluent/readme-portable.txt"), "");
        var dir = FluentSearchConfig.SettingsDir(loc, exe);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(exe)!, "Blast", "Settings"), dir);
        // A page the user changed stays theirs, apart from what WinMarchy needs.
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Web.json"), "[{\"SettingName\":\"OpenLinksInBrowser\",\"Value\":true}]");

        var store = new BackupStore(temp.File("backups"), new MemoryUserRegistry(), null);
        var point = store.Begin("launcher", "Fluent Search");
        await FluentSearchConfig.SetupAsync(new IntegrationContext(loc, store, point, new UserSettings()), Tokyo, exe, start: false, CancellationToken.None);

        // Every page twice: Fluent Search falls back to "<page>.backup.json".
        Assert.Equal(2 * FluentSearchConfig.TemplateFiles().Count, Directory.GetFiles(dir, "*.json").Length);
        Assert.Equal(File.ReadAllText(Path.Combine(dir, "System.json")), File.ReadAllText(Path.Combine(dir, "System.backup.json")));
        Assert.Contains("\"OpenLinksInBrowser\",\"Value\":true", File.ReadAllText(Path.Combine(dir, "Web.json")));
        Assert.Equal(0x87, Value(File.ReadAllText(Path.Combine(dir, "System.json")), "Search hotkey")!["HotKeys"]![1]!.GetValue<int>());

        store.Restore(point);
        Assert.Equal(new[] { "Web.json" }, Directory.GetFiles(dir, "*.json").Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_theme_change_only_sets_the_accent_colour()
    {
        using var temp = new TempDir();
        var loc = Locations.Under(temp.Path);
        var exe = temp.File("fluent/FluentSearch.exe");
        var dir = Path.Combine(Path.GetDirectoryName(exe)!, "Blast", "Settings");
        Directory.CreateDirectory(dir);
        File.WriteAllText(exe, "");
        // The user's own hotkey and look stay; only the colour follows the theme.
        File.WriteAllText(Path.Combine(dir, "System.json"), "[{\"SettingName\":\"StartAtStartup\",\"Value\":true}]");
        File.WriteAllText(Path.Combine(dir, "Appearance.json"), "[{\"SettingName\":\"WindowOpacity\",\"Value\":0.8}]");
        var store = new BackupStore(temp.File("backups"), new MemoryUserRegistry(), null);
        var point = store.Begin("theme", "Theme");

        await FluentSearchConfig.ApplyThemeAsync(new IntegrationContext(loc, store, point, new UserSettings()), Tokyo, exe, start: false, CancellationToken.None);

        Assert.Equal("[{\"SettingName\":\"StartAtStartup\",\"Value\":true}]", File.ReadAllText(Path.Combine(dir, "System.json")));
        var appearance = File.ReadAllText(Path.Combine(dir, "Appearance.json"));
        Assert.Equal(FluentSearchConfig.Argb(Tokyo.Accent), Value(appearance, "ColorSetting")!["ColorValue"]!.GetValue<long>());
        Assert.Equal(appearance, File.ReadAllText(Path.Combine(dir, "Appearance.backup.json")));
        Assert.Equal(new[] { "Appearance.backup.json", "Appearance.json", "System.json" }, Directory.GetFiles(dir).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task An_old_flow_launcher_no_longer_starts_with_windows_and_undo_brings_that_back()
    {
        using var temp = new TempDir();
        var loc = Locations.Under(temp.Path);
        var registry = new MemoryUserRegistry();
        registry.Write(LegacyFlow.RunKey, LegacyFlow.RunValue, new RegistryData("string", @"C:\Users\Test\AppData\Local\FlowLauncher\Flow.Launcher.exe", 0));
        var settings = LegacyFlow.SettingsFile(loc);
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        File.WriteAllText(settings, "{ \"Hotkey\": \"Alt + F24\", \"StartFlowLauncherOnSystemStartup\": true }");
        Assert.True(LegacyFlow.Present(loc, registry));

        var store = new BackupStore(temp.File("backups"), registry, null);
        var point = store.Begin("update", "Update");
        Assert.True(await LegacyFlow.RetireAsync(new IntegrationContext(loc, store, point, new UserSettings()), CancellationToken.None));
        Assert.Null(registry.Read(LegacyFlow.RunKey, LegacyFlow.RunValue));
        Assert.False(LegacyFlow.StartsWithWindows(File.ReadAllText(settings)));
        Assert.Contains("\"Hotkey\": \"Alt + F24\"", File.ReadAllText(settings));
        Assert.False(LegacyFlow.Present(loc, registry));

        store.Restore(point);
        Assert.NotNull(registry.Read(LegacyFlow.RunKey, LegacyFlow.RunValue));
        Assert.True(LegacyFlow.StartsWithWindows(File.ReadAllText(settings)));
    }

    [Fact]
    public void The_installed_build_keeps_its_settings_in_roaming_app_data()
    {
        var loc = Locations.Under("/tmp/x");
        Assert.Equal(Path.Combine(loc.AppData, "Blast", "Settings"), FluentSearchConfig.SettingsDir(loc, null));
    }

    [Fact]
    public void A_new_version_writes_the_generated_files_again()
    {
        Assert.False(Services.Engine.GeneratedFilesOutdated(new AppState { SetupDone = false }));
        Assert.True(Services.Engine.GeneratedFilesOutdated(new AppState { SetupDone = true, GeneratedBy = "0.0.0-ci.1" }));
        Assert.False(Services.Engine.GeneratedFilesOutdated(new AppState { SetupDone = true, GeneratedBy = AppInfo.Version }));
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using WinMarchy.Core.Backup;
using WinMarchy.Core.Hotkeys;
using WinMarchy.Core.Integrations;
using WinMarchy.Core.Themes;
using WinMarchy.Core.Tools;

namespace WinMarchy.Core.Tests;

/// <summary>Flow Launcher or Fluent Search behind the Win key, and Fluent Search's settings.</summary>
public class LauncherTests
{
    static readonly Palette Tokyo = ThemeCatalog.Default.Palette;

    static JsonNode? Value(string page, string name) =>
        JsonNode.Parse(page)!.AsArray().OfType<JsonObject>().First(i => i["SettingName"]!.GetValue<string>() == name)["Value"];

    [Fact]
    public void Flow_is_the_launcher_unless_fluent_search_is_chosen()
    {
        Assert.Equal(Launchers.Flow, Launchers.Of(new UserSettings()));
        Assert.Equal(Launchers.Flow, Launchers.Of(new UserSettings { Launcher = "something" }));
        var fluent = new UserSettings { Launcher = Launchers.FluentSearch };
        Assert.Same(ToolCatalog.FluentSearch, Launchers.Tool(fluent));
        Assert.Equal("FluentSearch", Launchers.Process(fluent));
        Assert.Equal(Launchers.Flow, Launchers.Other(Launchers.FluentSearch));
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
    public void Fluent_search_comes_from_a_pinned_release_with_its_own_checksum()
    {
        var tool = ToolCatalog.FluentSearch;
        Assert.Equal("1.0.1.4", tool.PinnedTag);
        Assert.Matches("^[0-9a-f]{64}$", tool.PinnedSha256!);
        Assert.Matches(tool.Asset!, "fluent-search-portable-x64.zip");
        Assert.True(tool.Extra);
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
    public async Task Settings_land_in_blast_settings_next_to_the_portable_exe_and_can_be_undone()
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
        // What an earlier version put a level too high goes; Fluent Search's own files there stay.
        var tooHigh = Path.Combine(Path.GetDirectoryName(exe)!, "Blast");
        File.WriteAllText(Path.Combine(tooHigh, "System.json"), "[]");
        File.WriteAllText(Path.Combine(tooHigh, "other.dat"), "x");
        var roaming = Path.Combine(loc.AppData, "Blast");
        Directory.CreateDirectory(roaming);
        File.WriteAllText(Path.Combine(roaming, "Appearance.json"), "[]");

        var store = new BackupStore(temp.File("backups"), new MemoryUserRegistry(), null);
        var point = store.Begin("launcher", "Fluent Search");
        await FluentSearchConfig.ApplyAsync(new IntegrationContext(loc, store, point, new UserSettings()), Tokyo, exe, start: false, CancellationToken.None);

        Assert.Equal(FluentSearchConfig.TemplateFiles().Count, Directory.GetFiles(dir, "*.json").Length);
        Assert.Contains("\"OpenLinksInBrowser\",\"Value\":true", File.ReadAllText(Path.Combine(dir, "Web.json")));
        Assert.Equal(0x87, Value(File.ReadAllText(Path.Combine(dir, "System.json")), "Search hotkey")!["HotKeys"]![1]!.GetValue<int>());
        Assert.False(File.Exists(Path.Combine(tooHigh, "System.json")));
        Assert.True(File.Exists(Path.Combine(tooHigh, "other.dat")));
        Assert.False(File.Exists(Path.Combine(roaming, "Appearance.json")));

        store.Restore(point);
        Assert.Equal(new[] { "Web.json" }, Directory.GetFiles(dir, "*.json").Select(Path.GetFileName));
        Assert.True(File.Exists(Path.Combine(tooHigh, "System.json")));
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

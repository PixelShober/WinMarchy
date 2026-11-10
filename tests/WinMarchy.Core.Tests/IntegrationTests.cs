using System.Text.Json.Nodes;
using WinMarchy.Core.GlazeWm;
using WinMarchy.Core.Hotkeys;
using WinMarchy.Core.Integrations;
using WinMarchy.Core.Themes;

namespace WinMarchy.Core.Tests;

public class IntegrationTests
{
    static Palette Tokyo => ThemeCatalog.Default.Palette;

    [Fact]
    public void Terminal_gets_one_scheme_and_uses_it_by_default()
    {
        const string original = """
            // Windows Terminal settings
            {
              "profiles": { "defaults": {}, "list": [ { "name": "PowerShell", "colorScheme": "Campbell" }, { "name": "cmd" } ] },
              "schemes": [ { "name": "Campbell" } ],
            }
            """;
        var scheme = TerminalTheme.Build(Tokyo);
        var once = TerminalTheme.Patch(original, scheme);
        var twice = TerminalTheme.Patch(once, scheme);
        var root = Jsonc.ParseObject(twice);

        Assert.Equal(2, root["schemes"]!.AsArray().Count);
        Assert.Equal(TerminalTheme.SchemeName, (string?)root["profiles"]!["defaults"]!["colorScheme"]);
        Assert.Equal(TerminalTheme.SchemeName, (string?)root["profiles"]!["list"]![0]!["colorScheme"]);
        Assert.Null(root["profiles"]!["list"]![1]!["colorScheme"]);
    }

    [Fact]
    public void PowerShell_profile_hook_is_added_once()
    {
        var profile = "Set-Alias ll Get-ChildItem\r\n";
        var once = TerminalTheme.WithHook(profile, @"C:\WinMarchy\Runtime\colors.ps1");
        var twice = TerminalTheme.WithHook(once, @"C:\WinMarchy\Runtime\colors.ps1");
        Assert.Equal(once, twice);
        Assert.StartsWith("Set-Alias ll Get-ChildItem", once);
    }

    [Fact]
    public void VsCode_falls_back_to_built_in_themes_and_takes_its_colours_back()
    {
        var theme = ThemeCatalog.Default;
        var fallback = Jsonc.ParseObject(VsCodeTheme.Patch("""{ "editor.fontSize": 14 }""", theme, extensionAvailable: false));
        Assert.Equal("Default Dark Modern", (string?)fallback["workbench.colorTheme"]);
        Assert.NotNull(fallback["workbench.colorCustomizations"]);
        Assert.Equal(14, (int?)fallback["editor.fontSize"]);

        if (theme.Vscode is { Name.Length: > 0 } vs)
        {
            var extension = Jsonc.ParseObject(VsCodeTheme.Patch(fallback.ToJsonString(), theme, extensionAvailable: true));
            Assert.Equal(vs.Name, (string?)extension["workbench.colorTheme"]);
            Assert.Null(extension["workbench.colorCustomizations"]);
        }
    }

    [Fact]
    public void GlazeWM_config_has_the_omarchy_look()
    {
        var yaml = GlazeConfig.Generate(GlazeConfig.FromSettings(new UserSettings(), Tokyo, Bars.Height));
        Assert.Contains("workspaces:", yaml);
        Assert.Contains("'" + GlazeConfig.ScratchpadWorkspace + "'", yaml);
        Assert.Contains(Tokyo.Accent, yaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(GlazeConfig.OverlayTitle, yaml);
        // AutoHotkey owns the keys by default: GlazeWM gets no keybindings of its own.
        Assert.DoesNotContain("lwin+enter", yaml);
    }
}

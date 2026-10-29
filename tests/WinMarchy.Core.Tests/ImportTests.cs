using WinMarchy.Core.GlazeWm;
using WinMarchy.Core.Hotkeys;

namespace WinMarchy.Core.Tests;

public class ImportTests
{
    static HotkeySet Current() => HotkeySet.Build(personal: false, new CustomHotkeys(), "autohotkey");

    static IReadOnlyList<ImportCandidate> Read(string name, string text)
    {
        using var temp = new TempDir();
        var file = temp.File(name);
        File.WriteAllText(file, text);
        var result = HotkeyImport.Read(file, Current());
        Assert.Null(result.Export);
        return result.Candidates;
    }

    [Fact]
    public void Omarchy_bindings_lua_is_read_and_the_last_line_of_the_same_keys_wins()
    {
        var found = Read("bindings.lua", """
            o.bind("SUPER + ALT + G", "Signal", "omarchy-launch-or-focus signal \"uwsm-app -- signal-desktop\"")
            o.bind("SUPER + SHIFT + T", "Telegram", "uwsm-app -- Telegram")
            o.bind("SUPER + ALT + Y", "YouTube", "omarchy-launch-webapp \"https://youtube.com/\"")
            -- o.bind("SUPER + ALT + X", "Commented out", "spotify")
            o.bind("SUPER + ALT + Q", "Leave Hyprland", "hyprctl dispatch exit")
            o.bind("SUPER + ALT + G", "Signal again", "signal-desktop")
            """);
        Assert.Equal(new[] { "Win+Alt+G", "Win+Shift+T", "Win+Alt+Y", "Win+Alt+Q" }, found.Select(c => c.Keys));

        var signal = found.Single(c => c.Keys == "Win+Alt+G");
        Assert.Equal(("app:signal", "Signal again", true), (signal.Do, signal.Label, signal.Suggested));

        // Win+Shift+T opens the Task Manager: offered, but not ticked.
        var telegram = found.Single(c => c.Keys == "Win+Shift+T");
        Assert.Equal("app:telegram", telegram.Do);
        Assert.NotNull(telegram.Replaces);
        Assert.False(telegram.Suggested);

        Assert.Equal("web:https://youtube.com/", found.Single(c => c.Keys == "Win+Alt+Y").Do);
        var exit = found.Single(c => c.Keys == "Win+Alt+Q");
        Assert.NotNull(exit.Problem);
        Assert.False(exit.Suggested);
    }

    [Fact]
    public void Hyprland_conf_reads_exec_and_dispatchers()
    {
        var found = Read("bindings.conf", """
            # Apps
            bindd = SUPER SHIFT, RETURN, Browser, exec, omarchy-launch-browser
            bindd = SUPER ALT, M, Music, exec, omarchy-launch-or-focus spotify
            bind = SUPER ALT, U, exec, xdg-open spotify:playlist:37i9dQZF1DXcBWIGoYBM5M
            bindd = SUPER, W, Close window, killactive,
            bindd = SUPER ALT, LEFT, Focus left, movefocus, l
            bindd = SUPER ALT, 4, To workspace 4, movetoworkspace, 4
            bindd = SUPER ALT, S, Silently somewhere, movetoworkspacesilent, 3
            bindd = SUPER, code:10, Odd key, workspace, 1
            """);

        var browser = found.Single(c => c.Keys == "Win+Shift+Enter");
        Assert.Equal("app:browser", browser.Do);
        Assert.NotNull(browser.Replaces);
        Assert.Equal("app:spotify", found.Single(c => c.Keys == "Win+Alt+M").Do);
        Assert.Equal("open:spotify:playlist:37i9dQZF1DXcBWIGoYBM5M", found.Single(c => c.Keys == "Win+Alt+U").Do);

        var close = found.Single(c => c.Keys == "Win+W");
        Assert.Equal("wm:close", close.Do);
        Assert.True(close.Same);
        Assert.False(close.Suggested);

        Assert.Equal("do:focus left", found.Single(c => c.Keys == "Win+Alt+Left").Do);
        Assert.Equal("do:to-workspace 4", found.Single(c => c.Keys == "Win+Alt+4").Do);
        // Only the scratchpad is moved to silently in WinMarchy.
        Assert.DoesNotContain(found, c => c.Keys == "Win+Alt+S");
        Assert.NotNull(Assert.Single(found, c => c.Origin.Contains("workspace 1")).Problem);
    }

    [Fact]
    public void A_winmarchy_export_comes_back_with_its_favourites()
    {
        using var temp = new TempDir();
        var file = temp.File("hotkeys.json");
        var export = new CustomHotkeys { Favorites = { ["Win+J"] = true } };
        export.Bindings.Add(new HotkeyBinding { Keys = "Win+Alt+Y", Do = "web:https://youtube.com/", Group = "apps", Label = LocalizedText.Same("YouTube") });
        export.Save(file);

        var result = HotkeyImport.Read(file, Current());
        var only = Assert.Single(result.Candidates);
        Assert.True(only.Suggested);
        Assert.True(result.Export!.Favorites["Win+J"]);

        var binding = only.ToBinding();
        Assert.Equal(("Win+Alt+Y", "web:https://youtube.com/", "apps"), (binding.Keys, binding.Do, binding.Group));
    }

    [Theory]
    [InlineData("omarchy-launch-webapp \"https://app.stackfield.com/\"", "web:https://app.stackfield.com/")]
    [InlineData("xdg-open spotify:album:4aawyAB9vmqN3uQ7FjRGTy", "open:spotify:album:4aawyAB9vmqN3uQ7FjRGTy")]
    [InlineData("uwsm-app -- xdg-terminal-exec", "app:terminal")]
    [InlineData("omarchy-menu-keybindings", "do:keybindings")]
    [InlineData("hyprctl dispatch exit", null)]
    public void Linux_commands_get_their_windows_counterpart(string command, string? expected) =>
        Assert.Equal(expected, HotkeyImport.Map(command));

    [Fact]
    public void Greenshot_takes_win_shift_s_unless_the_keys_are_used()
    {
        var extra = new[] { new HotkeyBinding { Keys = "Win+Shift+S", Do = "do:screenshot", Group = "utilities", Label = LocalizedText.Same("Greenshot") } };
        var ctx = new AhkContext(@"C:\WinMarchy\App\WinMarchy.exe", "", true);
        var set = Current();
        Assert.DoesNotContain("Hotkey(\"$#+s\"", AhkScript.Generate(set, ctx));
        Assert.Contains("Hotkey(\"$#+s\"", AhkScript.Generate(set, ctx with { Extra = extra }));
        Assert.Contains(GlazeConfig.Keybindings(set, KeyboardLayouts.Current, "WinMarchy.exe", null, extra), k => k.Comment == "Greenshot");

        var custom = new CustomHotkeys();
        custom.Bindings.Add(new HotkeyBinding { Keys = "Win+Shift+S", Do = "app:notes", Group = "apps", Label = LocalizedText.Same("Notes") });
        var own = HotkeySet.Build(personal: false, custom, "autohotkey");
        var script = AhkScript.Generate(own, ctx with { Extra = extra });
        Assert.Single(script.Split('\n'), l => l.StartsWith("Hotkey(\"$#+s\""));
        Assert.DoesNotContain("; Greenshot", script);
    }
}

using WinMarchy.Core.GlazeWm;
using WinMarchy.Core.Hotkeys;
using WinMarchy.Core.Integrations;
using WinMarchy.Core.Platform;
using WinMarchy.Core.Themes;

namespace WinMarchy.Core.Tests;

public class FavoriteTests
{
    [Fact]
    public void There_are_no_favourites_until_the_user_stars_some()
    {
        var set = HotkeySet.Build(personal: false, new CustomHotkeys(), "autohotkey");
        Assert.DoesNotContain(set.Active, b => b.Favorite);
        Assert.True(Assert.Single(set.Active, b => b.Keys == "Win+Shift+Backspace").Optional);

        var custom = new CustomHotkeys { Favorites = { ["win+shift+backspace"] = true, ["Win+W"] = false } };
        set = HotkeySet.Build(personal: false, custom, "autohotkey");
        Assert.True(Assert.Single(set.Active, b => b.Keys == "Win+Shift+Backspace").Favorite);
        Assert.False(Assert.Single(set.Active, b => b.Keys == "Win+W").Favorite);
    }

    [Fact]
    public void Favourites_are_saved_with_the_own_shortcuts()
    {
        using var temp = new TempDir();
        var file = temp.File("hotkeys.json");
        new CustomHotkeys { Favorites = { ["Win+J"] = true } }.Save(file);
        var back = CustomHotkeys.Load(file);
        Assert.True(back.Favorites["Win+J"]);
        Assert.True(Assert.Single(HotkeySet.Build(false, back, "autohotkey").Active, b => b.Keys == "Win+J").Favorite);
    }

    [Fact]
    public void The_business_profile_follows_the_wish_list()
    {
        var set = HotkeySet.Build(personal: false, new CustomHotkeys(), "autohotkey");
        Assert.Empty(set.Problems);
        // Gone: resizing, transparency, the silent move, monitors by direction, layout back, Teams, Copilot.
        Assert.DoesNotContain(set.Active, b => b.Do.StartsWith("wm:resize") || b.Do is "do:transparency-toggle" or "do:layout-prev" or "app:teams");
        Assert.DoesNotContain(set.Active, b => b.Do.StartsWith("do:move-window-monitor") || b.Do.StartsWith("wm:move-workspace"));
        Assert.DoesNotContain(set.Active, b => b.Keys == "Win+Shift+A" || (b.Keys.StartsWith("Win+Alt+Shift+") && char.IsDigit(b.Keys[^1])));
        Assert.Equal("do:restore-minimized", Assert.Single(set.Active, b => b.Keys == "Win+M").Do);
        // Win+Alt+L never reached WinMarchy on the test PC; the layout sits next to Win+J.
        Assert.Equal("do:layout-next", Assert.Single(set.Active, b => b.Keys == "Win+Ctrl+J").Do);
        Assert.DoesNotContain(set.Active, b => b.Keys == "Win+Alt+L");
        Assert.Equal("do:menu", Assert.Single(set.Active, b => b.Keys == "Win+Space").Do);
        Assert.DoesNotContain(set.Active, b => b.Keys == "Win+Alt+Space");
        Assert.Single(set.Active, b => b.Do == "do:theme-menu");
        Assert.Equal("web:https://open.spotify.com/", Assert.Single(set.Active, b => b.Keys == "Win+Shift+M").Do);
        Assert.Equal("app:browser2", Assert.Single(set.Active, b => b.Keys == "Win+Shift+Enter").Do);
        Assert.Equal("do:to-workspace 3", Assert.Single(set.Active, b => b.Keys == "Win+Shift+3").Do);
        Assert.Equal(10, set.Active.Count(b => b.Template == "Win+{n}"));
    }

    [Fact]
    public void Greenshot_keeps_its_print_keys()
    {
        var set = HotkeySet.Build(personal: false, new CustomHotkeys(), "autohotkey");
        var ctx = new AhkContext(@"C:\WinMarchy\App\WinMarchy.exe", "", true);
        Assert.Contains("Hotkey(\"$PrintScreen\"", AhkScript.Generate(set, ctx));
        var greenshot = AhkScript.Generate(set, ctx with { LeaveAlone = Screenshots.GreenshotKeys.ToHashSet() });
        Assert.DoesNotContain("Hotkey(\"$PrintScreen\"", greenshot);
        Assert.DoesNotContain("Hotkey(\"$!PrintScreen\"", greenshot);
        Assert.Contains("do:screenshot", greenshot);
    }

    [Theory]
    [InlineData(null, "region", "PrintScreen")]
    [InlineData("[Core]\nRegionHotkey=Ctrl + Shift + PrintScreen\n", "region", "Ctrl+Shift+PrintScreen")]
    [InlineData("[Other]\nWindowHotkey=F9\n[Core]\nWindowHotkey=Alt + PrintScreen\n", "window", "Alt+PrintScreen")]
    [InlineData("[Core]\nFullscreenHotkey=nonsense\n", "screen", "Ctrl+PrintScreen")]
    public void Greenshot_hotkeys_come_from_its_ini(string? ini, string mode, string expected) =>
        Assert.Equal(expected, Screenshots.GreenshotHotkey(ini, mode));
}

public class ScrollingTests
{
    static ScrollStrip Strip(int count, int focus)
    {
        var s = new ScrollStrip { Focus = focus };
        for (int i = 0; i < count; i++) s.Ids.Add("w" + i);
        return s;
    }

    [Fact]
    public void One_window_takes_the_whole_width()
    {
        var placed = Strip(1, 0).Place(x: 0, width: 1000, visible: 2, gap: 10);
        var (id, x, width, visible) = Assert.Single(placed);
        Assert.Equal(("w0", 0, 1000, true), (id, x, width, visible));
    }

    [Fact]
    public void The_focused_column_is_always_on_the_screen()
    {
        var strip = Strip(4, 3);
        var placed = strip.Place(0, 1000, 2, 10);
        Assert.Equal(2, strip.Start);
        Assert.Equal(new[] { false, false, true, true }, placed.Select(p => p.Visible));
        // The two on the screen share it; nobody sits behind them.
        Assert.Equal((0, 495), (placed[2].X, placed[2].Width));
        Assert.Equal(505, placed[3].X);

        strip.Focus = 0;
        placed = strip.Place(0, 1000, 2, 10);
        Assert.Equal(0, strip.Start);
        Assert.Equal(new[] { true, true, false, false }, placed.Select(p => p.Visible));
    }

    [Fact]
    public void Moving_the_focus_scrolls_only_as_far_as_needed()
    {
        var strip = Strip(5, 1);
        strip.Reveal(2);
        Assert.Equal(0, strip.Start);
        strip.Focus = 2;
        strip.Reveal(2);
        Assert.Equal(1, strip.Start);
        strip.Focus = 1;
        strip.Reveal(2);
        Assert.Equal(1, strip.Start);
    }
}

public class NeighborTests
{
    static WindowInfo W(string id, int x, int y) => new(id, 0, "1", "floating", false, false, x, y, 100, 100);

    [Fact]
    public void Finds_the_window_that_way_on_the_screen()
    {
        var from = W("a", 500, 500);
        var all = new[] { from, W("left", 200, 500), W("farLeft", 0, 500), W("up", 500, 100), W("leftUp", 300, 100) };
        Assert.Equal("left", GlazeTree.Neighbor(from, all, "left")?.Id);
        Assert.Equal("up", GlazeTree.Neighbor(from, all, "up")?.Id);
        Assert.Null(GlazeTree.Neighbor(from, all, "right"));
        Assert.Null(GlazeTree.Neighbor(from, all, "down"));
    }
}

public class MonitorBindingTests
{
    [Fact]
    public void Pinned_workspaces_get_their_monitor_and_both_hidden_workspaces_exist()
    {
        var o = GlazeConfig.FromSettings(new UserSettings { WorkspaceMonitors = { ["1"] = 0, ["6"] = 1 } }, ThemeCatalog.Default.Palette, 26);
        var yaml = GlazeConfig.Generate(o);
        Assert.Contains("  - name: '1'\n    bind_to_monitor: 0\n", yaml);
        Assert.Contains("  - name: '6'\n    bind_to_monitor: 1\n", yaml);
        Assert.Contains("  - name: '2'\n  - name: '3'", yaml);
        Assert.Contains($"  - name: '{GlazeConfig.ScratchpadWorkspace}'", yaml);
        Assert.Contains($"  - name: '{GlazeConfig.StageWorkspace}'", yaml);
        Assert.False(GlazeConfig.IsRegular(GlazeConfig.StageWorkspace));
        Assert.False(GlazeConfig.IsRegular(GlazeConfig.ScratchpadWorkspace));
        Assert.True(GlazeConfig.IsRegular("4"));
    }
}

public class StartAppsTests
{
    static readonly StartApp[] Apps =
    {
        new("Microsoft Outlook", "a"), new("Outlook (new)", "b"), new("Visual Studio Code", "c"), new("Code Writer", "d"), new("Notepad", "e"),
    };

    [Fact]
    public void Names_that_start_with_the_text_come_first()
    {
        Assert.Equal(new[] { "Outlook (new)", "Microsoft Outlook" }, StartApps.Search(Apps, "out").Select(a => a.Name));
        Assert.Equal(new[] { "Code Writer", "Visual Studio Code" }, StartApps.Search(Apps, "code").Select(a => a.Name));
        Assert.Empty(StartApps.Search(Apps, "  "));
        Assert.Empty(StartApps.Search(Apps, "zzz"));
    }
}

/// <summary>Points C:\WinMarchy at a temporary folder; alone, because the root is global.</summary>
[Collection("Language")]
public class OwnWallpaperTests
{
    [Fact]
    public void Own_pictures_come_first_and_a_converted_copy_is_not_counted()
    {
        using var temp = new TempDir();
        var before = Paths.Root;
        Paths.OverrideRoot(temp.File("root"));
        try { Check(temp); }
        finally { Paths.OverrideRoot(before); }
    }

    static void Check(TempDir temp)
    {
        var theme = ThemeCatalog.Default;
        var source = Path.Combine(temp.Path, "in");
        Directory.CreateDirectory(source);
        File.WriteAllBytes(Path.Combine(source, "beach.webp"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(source, "notes.txt"), new byte[] { 1 });
        Assert.Equal(1, ThemeAssets.AddOwn(theme, Directory.GetFiles(source)));
        Assert.Equal(1, ThemeAssets.AddOwn(theme, new[] { Path.Combine(source, "beach.webp") }));
        File.WriteAllBytes(Path.Combine(ThemeAssets.OwnDir(theme), "beach.jpg"), new byte[] { 1 });

        var own = ThemeAssets.OwnBackgrounds(theme).Select(Path.GetFileName).ToList();
        Assert.Equal(new[] { "beach-2.webp", "beach.webp" }, own);
        Assert.Equal(own, ThemeAssets.Backgrounds(theme).Take(2).Select(Path.GetFileName));
    }
}

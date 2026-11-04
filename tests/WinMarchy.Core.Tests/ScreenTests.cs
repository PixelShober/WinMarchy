using System.Text.Json;
using WinMarchy.Core.GlazeWm;
using WinMarchy.Core.Platform;
using WinMarchy.Core.Tools;

namespace WinMarchy.Core.Tests;

/// <summary>Screens on the Tiling page: names, the main screen's workspaces, and the screen next door.</summary>
public class ScreenTests
{
    [Fact]
    public void The_main_screen_gets_one_and_two_and_the_others_share_the_rest()
    {
        Assert.Equal(new[] { Enumerable.Range(1, 10).ToList() }, ScreenPlan.Split(1, 0));
        Assert.Equal(new[] { new List<int> { 1, 2 }, Enumerable.Range(3, 8).ToList() }, ScreenPlan.Split(2, 0));
        Assert.Equal(new[] { Enumerable.Range(3, 8).ToList(), new List<int> { 1, 2 } }, ScreenPlan.Split(2, 1));
        Assert.Equal(new[] { new List<int> { 3, 4, 5, 6 }, new List<int> { 1, 2 }, new List<int> { 7, 8, 9, 10 } }, ScreenPlan.Split(3, 1));
        // Not even: the left ones get one more.
        Assert.Equal(new[] { new List<int> { 1, 2 }, new List<int> { 3, 4, 5 }, new List<int> { 6, 7, 8 }, new List<int> { 9, 10 } }, ScreenPlan.Split(4, 0));
        Assert.Empty(ScreenPlan.Split(0, 0));
        // A main screen that is gone counts as the last row.
        Assert.Equal(new List<int> { 1, 2 }, ScreenPlan.Split(2, 5)[1]);
    }

    [Fact]
    public void Screens_are_named_like_windows_names_them()
    {
        var names = ScreenNames.Label(new (string?, bool)[] { (null, true), ("DELL U2720Q", false), (null, false) });
        Assert.Equal(new[] { L.T("screens.builtIn"), "DELL U2720Q", L.T("tiling.monitorName", ("number", 3)) }, names);
    }

    [Fact]
    public void Two_screens_of_one_model_are_numbered()
    {
        var names = ScreenNames.Label(new (string?, bool)[] { ("DELL P2419H", false), (null, true), ("DELL P2419H", false) });
        Assert.Equal(new[] { "DELL P2419H (1)", L.T("screens.builtIn"), "DELL P2419H (2)" }, names);
    }

    /// <summary>Three screens side by side, as in the log: 3 on the left, 5 in the middle, 1 on the right.</summary>
    static JsonElement Monitors() => JsonDocument.Parse("""
        { "monitors": [
          { "id": "m0", "x": 0, "y": 0, "width": 1920, "height": 1080, "children": [
            { "type": "workspace", "name": "3", "isDisplayed": true, "children": [] },
            { "type": "workspace", "name": "4", "isDisplayed": false, "children": [] } ] },
          { "id": "m1", "x": 1920, "y": 0, "width": 2560, "height": 1440, "children": [
            { "type": "workspace", "name": "5", "isDisplayed": true, "children": [] } ] },
          { "id": "m2", "x": 4480, "y": 0, "width": 1920, "height": 1080, "children": [
            { "type": "workspace", "name": "scratchpad", "isDisplayed": true, "children": [] },
            { "type": "workspace", "name": "1", "isDisplayed": true, "children": [] } ] } ] }
        """).RootElement;

    [Theory]
    [InlineData("5", "left", "3")]
    [InlineData("5", "right", "1")]
    [InlineData("3", "right", "5")]
    [InlineData("4", "right", "5")]
    [InlineData("3", "left", null)]
    [InlineData("5", "up", null)]
    public void The_workspace_on_the_screen_next_door_whatever_its_number(string from, string direction, string? expected) =>
        Assert.Equal(expected, GlazeTree.WorkspaceInDirection(Monitors(), from, direction));

    [Fact]
    public void Coming_in_from_the_left_the_focus_lands_on_the_leftmost_window()
    {
        var windows = new[]
        {
            new WindowInfo("a", 1, "5", "floating", false, false, 3200, 0, 1280, 1440),
            new WindowInfo("b", 2, "5", "floating", false, false, 1920, 0, 1280, 1440),
        };
        Assert.Equal("b", GlazeTree.EntryWindow(windows, "right")?.Id);
        Assert.Equal("a", GlazeTree.EntryWindow(windows, "left")?.Id);
        Assert.Null(GlazeTree.EntryWindow(Array.Empty<WindowInfo>(), "right"));
    }

    [Fact]
    public void A_newer_release_shows_only_once_it_was_looked_up()
    {
        var tool = ToolCatalog.Yasb;
        var status = new ToolStatus(tool, @"C:\WinMarchy\Tools\yasb\yasb.exe", "1.8.0", false, true);
        var state = new AppState { InstalledTools = { tool.Id }, ToolTags = { [tool.Id] = "v1.8.0" } };
        Assert.Null(ToolUpdates.NewerTag(status, state));
        Assert.False(ToolUpdates.IsCurrent(status, state));

        state.LatestReleases[tool.Id] = new LatestRelease("v1.8.0", DateTime.UtcNow);
        Assert.Null(ToolUpdates.NewerTag(status, state));
        Assert.True(ToolUpdates.IsCurrent(status, state));

        state.LatestReleases[tool.Id] = new LatestRelease("v1.8.1", DateTime.UtcNow);
        Assert.Equal("v1.8.1", ToolUpdates.NewerTag(status, state));
        Assert.False(ToolUpdates.IsCurrent(status, state));
    }

    [Fact]
    public void Only_programs_winmarchy_installed_from_a_zip_or_msi_are_updated()
    {
        var state = new AppState { InstalledTools = { ToolCatalog.Yasb.Id, ToolCatalog.Flow.Id } };
        var tools = ToolUpdates.Updatable(state);
        Assert.Contains(ToolCatalog.Yasb, tools);
        Assert.DoesNotContain(ToolCatalog.Flow, tools);
        Assert.DoesNotContain(ToolCatalog.AutoHotkey, tools);
    }

    [Fact]
    public void The_lookups_survive_in_state_json()
    {
        var state = new AppState();
        state.LatestReleases["yasb"] = new LatestRelease("v1.8.1", new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc));
        var back = Json.Deserialize<AppState>(Json.Serialize(state, indented: true))!;
        Assert.Equal("v1.8.1", back.LatestReleases["yasb"].Tag);
        Assert.Equal(state.LatestReleases["yasb"].CheckedUtc, back.LatestReleases["yasb"].CheckedUtc.ToUniversalTime());
    }
}

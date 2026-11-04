using System.Text.Json;
using WinMarchy.Core.GlazeWm;
using WinMarchy.Core.Hotkeys;
using WinMarchy.Core.Platform;

namespace WinMarchy.Core.Tests;

public class WorkModeTests
{
    static readonly string?[] ThreeApps = { "app:browser https://a.example/", "app:mail", "app:teams" };

    /// <summary>Screens showing the given workspaces; "" means an empty one, null a non-regular one.</summary>
    static DesktopState Desktop(params string?[] shown) => Desktop(shown, Array.Empty<string>());

    static DesktopState Desktop(string?[] shown, IEnumerable<string> alsoOpen)
    {
        var screens = shown.Select((name, i) => new ScreenState(i, name is "" ? $"e{i}" : name, name is "", i == 0)).ToList();
        var active = new HashSet<string>(screens.Select(s => s.Shown).OfType<string>().Concat(alsoOpen));
        return new DesktopState(screens, active, Array.Empty<PlacedWindow>());
    }

    static PlacedWindow At(string workspace, int monitor) => new(Guid.NewGuid().ToString(), 1, "x", workspace, monitor);

    static readonly Dictionary<int, PlacedWindow> None = new();

    [Fact]
    public void One_screen_gets_all_programs_on_one_fresh_workspace()
    {
        var plan = WorkModePlan.Make(Desktop("1"), ThreeApps, None, null);
        Assert.False(plan.Back);
        Assert.Equal(new[] { ScreenTarget.Fresh }, plan.Targets);
        Assert.Equal(new[] { (0, 0), (1, 0), (2, 0) }, plan.Start);
    }

    [Fact]
    public void Three_screens_get_a_program_each()
    {
        var plan = WorkModePlan.Make(Desktop("1", "4", "7"), ThreeApps, None, null);
        Assert.All(plan.Targets, t => Assert.Equal(ScreenGoal.Fresh, t.Goal));
        Assert.Equal(new[] { (0, 0), (1, 1), (2, 2) }, plan.Start);
    }

    [Fact]
    public void Two_screens_send_the_third_program_to_the_last_one()
    {
        var plan = WorkModePlan.Make(Desktop("1", "6"), ThreeApps, None, null);
        Assert.Equal(new[] { (0, 0), (1, 1), (2, 1) }, plan.Start);
    }

    [Fact]
    public void Slots_without_a_program_start_nothing()
    {
        var plan = WorkModePlan.Make(Desktop("1", "6"), new[] { null, "app:mail" }, None, null);
        Assert.Equal(new[] { (1, 1) }, plan.Start);
        Assert.Equal(2, plan.Targets.Count);
    }

    [Fact]
    public void Running_mode_comes_back_and_starts_only_what_was_closed()
    {
        var alive = new Dictionary<int, PlacedWindow> { [0] = At("4", 0), [2] = At("8", 2) };
        var plan = WorkModePlan.Make(Desktop("1", "5", "9"), ThreeApps, alive, null);
        Assert.False(plan.Back);
        Assert.Equal(new[] { ScreenTarget.Show("4"), ScreenTarget.Fresh, ScreenTarget.Show("8") }, plan.Targets);
        Assert.Equal(new[] { (1, 1) }, plan.Start);
    }

    [Fact]
    public void Running_mode_on_one_screen_starts_the_closed_program_on_its_workspace()
    {
        var alive = new Dictionary<int, PlacedWindow> { [0] = At("4", 0), [1] = At("4", 0) };
        var plan = WorkModePlan.Make(Desktop("1"), ThreeApps, alive, null);
        Assert.Equal(new[] { ScreenTarget.Show("4") }, plan.Targets);
        Assert.Equal(new[] { (2, 0) }, plan.Start);
    }

    [Fact]
    public void Pressing_again_goes_back_to_what_was_shown_before()
    {
        var alive = new Dictionary<int, PlacedWindow> { [0] = At("4", 0), [1] = At("5", 1), [2] = At("8", 2) };
        var state = Desktop(new[] { "4", "5", "8" }, new[] { "1", "6" });
        var plan = WorkModePlan.Make(state, ThreeApps, alive, new[] { "1", "6", "7" });
        Assert.True(plan.Back);
        Assert.Empty(plan.Start);
        // Workspace 7 closed in the meantime: that screen gets an empty one instead.
        Assert.Equal(new[] { ScreenTarget.Show("1"), ScreenTarget.Show("6"), ScreenTarget.Fresh }, plan.Targets);
    }

    [Fact]
    public void Nothing_to_go_back_to_means_nothing_to_do()
    {
        var alive = new Dictionary<int, PlacedWindow> { [0] = At("4", 0), [1] = At("4", 0), [2] = At("4", 0) };
        var plan = WorkModePlan.Make(Desktop("4"), ThreeApps, alive, null);
        Assert.True(plan.Nothing);
        Assert.True(WorkModePlan.Make(Desktop("4"), ThreeApps, alive, new[] { "4" }).Nothing);
    }

    [Fact]
    public void Mode_without_programs_is_a_fresh_desktop_and_toggles_back()
    {
        var empty = Array.Empty<string?>();
        var first = WorkModePlan.Make(Desktop("1", "6"), empty, None, null);
        Assert.Equal(new[] { ScreenTarget.Fresh, ScreenTarget.Fresh }, first.Targets);
        Assert.Empty(first.Start);

        var again = WorkModePlan.Make(Desktop(new[] { "", "" }, new[] { "1", "6" }), empty, None, new[] { "1", "6" });
        Assert.True(again.Back);
        Assert.Equal(new[] { ScreenTarget.Show("1"), ScreenTarget.Show("6") }, again.Targets);
    }

    [Fact]
    public void Window_in_the_scratchpad_keeps_its_slot_but_shows_no_workspace()
    {
        var alive = new Dictionary<int, PlacedWindow> { [0] = At(GlazeConfig.ScratchpadWorkspace, 0) };
        var plan = WorkModePlan.Make(Desktop("1"), new[] { "app:notes" }, alive, null);
        Assert.Equal(new[] { ScreenTarget.Fresh }, plan.Targets);
        Assert.Empty(plan.Start);
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(2, 1, 0)]
    [InlineData(2, 2, 1)]
    [InlineData(1, 3, 1)]
    [InlineData(3, 3, 2)]
    public void Slot_goes_to_its_screen_or_the_last_one(int slot, int screens, int expected) =>
        Assert.Equal(expected, WorkModePlan.ScreenFor(slot, screens));

    [Fact]
    public void Free_workspace_prefers_the_screen_s_own_then_unpinned_or_pinned_to_a_missing_screen()
    {
        var pinned = new Dictionary<string, int> { ["1"] = 0, ["2"] = 0, ["3"] = 1, ["4"] = 2, ["5"] = 2 };
        var taken = new HashSet<string> { "1" };
        Assert.Equal("2", WorkModePlan.FreeWorkspace(10, taken, pinned, 0, 3));
        Assert.Equal("6", WorkModePlan.FreeWorkspace(10, new HashSet<string> { "1", "2" }, pinned, 0, 3));
        // Only one screen connected: workspaces of screens 2 and 3 are free for it.
        Assert.Equal("3", WorkModePlan.FreeWorkspace(10, new HashSet<string> { "1", "2" }, pinned, 0, 1));
        var all = Enumerable.Range(1, 10).Select(n => n.ToString()).ToHashSet();
        Assert.Null(WorkModePlan.FreeWorkspace(10, all, pinned, 0, 3));
    }

    [Theory]
    [InlineData("app:browser https://intranet.example/ https://mail.example/", true)]
    [InlineData("web:https://app.example/", true)]
    [InlineData("run:C:\\Tools\\fach.lnk", true)]
    [InlineData("open:ms-settings:display", true)]
    [InlineData("do:theme-menu", false)]
    [InlineData("send:Win+V", false)]
    [InlineData("app:launcher", false)]
    [InlineData("app:nothing", false)]
    public void Only_actions_that_open_a_window_can_be_a_mode_s_program(string action, bool ok)
    {
        var result = WorkModePlan.Startable(action, out var error);
        Assert.Equal(ok, result != null);
        Assert.Equal(ok, error == null);
    }

    [Fact]
    public void Empty_slot_is_neither_a_program_nor_an_error()
    {
        Assert.Null(WorkModePlan.Startable("  ", out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("do:work-mode 2", 2)]
    [InlineData("do:work-mode", 1)]
    [InlineData("do:desktop-new", 1)]
    [InlineData("do:work-mode 4", null)]
    [InlineData("do:desktop-next", null)]
    [InlineData("app:browser", null)]
    public void Mode_number_of_an_action(string action, int? expected) => Assert.Equal(expected, WorkMode.NumberOf(action));

    [Fact]
    public void Old_programs_per_monitor_become_the_first_mode()
    {
        var s = Json.Deserialize<UserSettings>("""{ "desktopApps": { "0": "app:mail", "2": "app:teams" } }""")!.Upgraded();
        Assert.Null(s.DesktopApps);
        Assert.Equal(new[] { "app:mail", "", "app:teams" }, Assert.Single(s.WorkModes).Apps);
        Assert.DoesNotContain("desktopApps", Json.Serialize(s));
    }

    [Fact]
    public void Existing_modes_are_not_overwritten_by_old_settings()
    {
        var s = Json.Deserialize<UserSettings>("""{ "workModes": [ { "name": "Recherche", "apps": [ "app:browser" ] } ], "desktopApps": { "0": "app:mail" } }""")!.Upgraded();
        Assert.Equal("Recherche", Assert.Single(s.WorkModes).Name);
        Assert.Equal("Recherche", s.WorkModeOf(1).Title(1));
        Assert.Empty(s.WorkModeOf(3).Apps);
    }

    [Fact]
    public void Desktop_state_reads_screens_workspaces_and_windows()
    {
        var json = """
        { "monitors": [
          { "type": "monitor", "hasFocus": false, "children": [
            { "type": "workspace", "name": "1", "isDisplayed": true, "children": [
              { "type": "window", "id": "a", "handle": 100, "processName": "msedge" } ] },
            { "type": "workspace", "name": "2", "isDisplayed": false, "children": [] } ] },
          { "type": "monitor", "hasFocus": true, "children": [
            { "type": "workspace", "name": "6", "isDisplayed": true, "children": [] },
            { "type": "workspace", "name": "scratchpad", "isDisplayed": false, "children": [
              { "type": "window", "id": "b", "handle": 200, "processName": "notepad" } ] } ] } ] }
        """;
        var state = DesktopState.From(JsonDocument.Parse(json).RootElement);
        Assert.Equal(new ScreenState(0, "1", false, false), state.Screens[0]);
        Assert.Equal(new ScreenState(1, "6", true, true), state.Screens[1]);
        Assert.Equal(new[] { "1", "2", "6", "scratchpad" }, state.Active.OrderBy(n => n));
        Assert.Contains(state.Windows, w => w is { Id: "a", Handle: 100, Process: "msedge", Workspace: "1", Monitor: 0 });
        Assert.Contains(state.Windows, w => w is { Id: "b", Workspace: "scratchpad", Monitor: 1 });
    }

    [Fact]
    public void A_work_modes_layout_survives_a_restart()
    {
        var store = new LayoutStore { Overrides = { ["4"] = LayoutMode.Dwindle }, Workspaces = { ["4"] = LayoutMode.Dwindle, ["5"] = LayoutMode.Scrolling } };
        store.Columns["5"] = new List<long> { 11, 12 };
        var back = LayoutStore.Parse(Json.Serialize(store));
        Assert.Equal(LayoutMode.Dwindle, back.Overrides["4"]);
        Assert.Equal(LayoutMode.Scrolling, back.Workspaces["5"]);
        Assert.Equal(new long[] { 11, 12 }, back.Columns["5"]);
    }

    [Fact]
    public void Layouts_json_of_older_versions_is_read()
    {
        // "untilClosed" held the layout from before a work mode; the one it used was in "workspaces".
        const string old = """{ "workspaces": { "2": "floating", "3": "manual", "4": "dwindle", "5": "scrolling" }, "columns": { "5": [7] }, "untilClosed": { "4": null } }""";
        var store = LayoutStore.Parse(old);
        Assert.Equal(new[] { "4", "5" }, store.Workspaces.Keys.OrderBy(k => k));
        Assert.Equal(LayoutMode.Dwindle, store.Overrides["4"]);
        Assert.Contains("2", store.FormerlyFloating);
        Assert.Equal(new long[] { 7 }, store.Columns["5"]);
        Assert.Empty(LayoutStore.Parse("[]").Workspaces);
    }

    [Fact]
    public void Each_screen_keeps_its_layout()
    {
        var s = new UserSettings { DefaultLayout = "columns" };
        Assert.Equal(LayoutMode.Columns, s.ScreenLayoutOf(2));
        s.SetScreenLayout(2, LayoutMode.Scrolling);
        Assert.Equal(new[] { "columns", "columns", "scrolling" }, s.ScreenLayouts);
        Assert.Equal(LayoutMode.Scrolling, s.ScreenLayoutOf(2));
        Assert.Equal(LayoutMode.Dwindle, LayoutEngine.Next(LayoutMode.Scrolling));
        Assert.Equal(LayoutMode.Columns, LayoutEngine.Next(LayoutMode.Dwindle));
    }

    [Fact]
    public void Older_settings_lose_the_floating_layout_and_lunatask()
    {
        const string old = """
            { "defaultLayout": "floating", "screenLayouts": ["manual", "scrolling"], "hotkeyEngine": "glazewm",
              "barShortcuts": [ { "title": "Aufgaben", "action": "app:lunatask" } ], "workModes": [ { "apps": ["app:lunatask", "app:mail"] } ] }
            """;
        var s = Json.Deserialize<UserSettings>(old)!.Upgraded();
        Assert.Equal("dwindle", s.DefaultLayout);
        Assert.Equal(new[] { "dwindle", "scrolling" }, s.ScreenLayouts);
        Assert.Empty(s.BarShortcuts);
        Assert.Equal(new[] { "", "app:mail" }, s.WorkModeOf(1).Apps);
    }

    [Theory]
    [InlineData("Failed to set position for Window(id=1, process=msedge, class=Chrome_WidgetWin_1, title=Angebot Müller (1...): Zugriff verweigert",
        "Failed to set position for Window(id=1, process=msedge, class=Chrome_WidgetWin_1, title=…): Zugriff verweigert")]
    [InlineData("Window(id=2, process=notepad, class=Notepad, title=a.txt)", "Window(id=2, process=notepad, class=Notepad, title=…)")]
    [InlineData("Another instance of the application is already running.", "Another instance of the application is already running.")]
    public void Glazewm_errors_lose_their_window_titles(string line, string expected) =>
        Assert.Equal(expected, Services.Components.WithoutTitles(line));

    [Theory]
    // A wide tile floats at 70 % of the screen, a narrow one at 40 %; the corner takes a third.
    [InlineData(1900, 1000, false, 270, 100, 1260, 800)]
    [InlineData(300, 300, false, 540, 250, 720, 500)]
    [InlineData(800, 600, true, 1193, 660, 600, 333)]
    public void A_floating_window_gets_a_sensible_place(int width, int height, bool corner, int x, int y, int w, int h)
    {
        var area = new WorkspaceArea("1", "id", 0, 0, 1800, 1000, 1.0, true, true);
        Assert.Equal((x, y, w, h), LayoutEngine.FloatRect(area, width, height, corner, 7));
    }
}

public class BrowserUrlTests
{
    [Fact]
    public void Browser_takes_addresses_as_tabs()
    {
        var a = HotkeyAction.Parse("app:browser https://intranet.example/start https://mail.example/");
        Assert.Equal("browser", a.AppId);
        Assert.Equal(new[] { "https://intranet.example/start", "https://mail.example/" }, a.AppUrls);
        Assert.Empty(HotkeyAction.Parse("app:terminal").AppUrls);
    }

    [Theory]
    [InlineData("app:terminal https://example.com/")]
    [InlineData("app:browser-private https://example.com/")]
    [InlineData("app:browser ftp://example.com/")]
    [InlineData("app:browser https://")]
    [InlineData("app:browser intranet")]
    public void Addresses_only_for_the_browser_and_only_web_ones(string text) =>
        Assert.False(HotkeyAction.TryParse(text, out _, out _));

    [Fact]
    public void Quotes_in_an_address_cannot_break_out_of_the_argument()
    {
        var a = HotkeyAction.Parse("app:browser https://example.com/?q=\"x\"");
        Assert.DoesNotContain("\"", a.AppUrls[0]);
    }

    [Theory]
    [InlineData("msedge.exe", false, new[] { "https://a/", "https://b/" }, "--new-window \"https://a/\" \"https://b/\"")]
    [InlineData("chrome.exe", false, new string[0], "--new-window")]
    [InlineData("msedge.exe", true, new string[0], "--inprivate")]
    [InlineData("firefox.exe", false, new[] { "https://a/" }, "-new-window \"https://a/\"")]
    [InlineData("firefox.exe", false, new[] { "https://a/", "https://b/" }, "\"https://a/\" \"https://b/\"")]
    [InlineData("firefox.exe", true, new string[0], "-private-window")]
    [InlineData("opera.exe", false, new[] { "https://a/" }, "--new-window \"https://a/\"")]
    public void Browser_arguments(string exe, bool privateWindow, string[] urls, string expected) =>
        Assert.Equal(expected, AppLauncher.BrowserArgs(exe, privateWindow, urls));
}

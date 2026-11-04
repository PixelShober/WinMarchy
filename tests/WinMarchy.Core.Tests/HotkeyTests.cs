using WinMarchy.Core.Hotkeys;

namespace WinMarchy.Core.Tests;

public class KeyComboTests
{
    [Theory]
    [InlineData("Win+Shift+Enter", "Win+Shift+Enter")]
    [InlineData("shift+win+enter", "Win+Shift+Enter")]
    [InlineData("Super+Return", "Win+Enter")]
    [InlineData("Strg+Umschalt+Esc", "Ctrl+Shift+Escape")]
    [InlineData("Win+Ctrl+Alt+Shift+F5", "Win+Ctrl+Alt+Shift+F5")]
    [InlineData("Win+minus", "Win+Minus")]
    public void Parses_and_normalizes(string input, string expected) =>
        Assert.Equal(expected, KeyCombo.Parse(input).ToString());

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("Win+Win+A")]
    [InlineData("Hyper+A")]
    [InlineData("Win+NoSuchKey")]
    public void Rejects_invalid_combinations(string input)
    {
        Assert.False(KeyCombo.TryParse(input, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void A_key_without_modifier_is_only_allowed_for_print_and_high_function_keys()
    {
        Assert.True(KeyCombo.TryParse("PrintScreen", out _, out _));
        Assert.True(KeyCombo.TryParse("F24", out _, out _));
        Assert.False(KeyCombo.TryParse("F5", out _, out _));
    }

    [Fact]
    public void Win_L_and_ctrl_alt_del_are_reserved()
    {
        Assert.NotNull(KeyCombo.Parse("Win+L").ReservedReason());
        Assert.NotNull(KeyCombo.Parse("Ctrl+Alt+Delete").ReservedReason());
        Assert.Null(KeyCombo.Parse("Win+Alt+L").ReservedReason());
    }

    [Fact]
    public void AutoHotkey_names_use_the_hook_and_scan_codes_for_punctuation()
    {
        Assert.Equal("$#+Enter", KeyCombo.Parse("Win+Shift+Enter").ToAhk());
        // "Minus" is the key right of 0 on any layout ("ß" on German keyboards).
        Assert.Equal("$#SC00C", KeyCombo.Parse("Win+Minus").ToAhk());
        // Mouse wheel hotkeys cannot use the keyboard hook prefix.
        Assert.Equal("#WheelDown", KeyCombo.Parse("Win+WheelDown").ToAhk());
        Assert.Equal("^+{t}", KeyCombo.Parse("Ctrl+Shift+T").ToAhkSend());
    }

}

[Collection("Language")]
public class KeyComboDisplayTests
{
    [Fact]
    public void German_labels_use_the_printed_key_names()
    {
        L.Configure("de");
        Assert.Equal("Win + Umschalt + Eingabe", KeyCombo.Parse("Win+Shift+Enter").Display(new GermanKeyboardLayout()));
        Assert.Equal("Win + Strg + ß", KeyCombo.Parse("Win+Ctrl+Minus").Display(new GermanKeyboardLayout()));
    }

    [Fact]
    public void English_labels()
    {
        L.Configure("en");
        try
        {
            Assert.Equal("Win + Ctrl + -", KeyCombo.Parse("Win+Ctrl+Minus").Display(new UsKeyboardLayout()));
        }
        finally
        {
            L.Configure("de");
        }
    }
}

public class HotkeyActionTests
{
    [Theory]
    [InlineData("wm:focus --direction left")]
    [InlineData("wm:move --workspace 3;focus --workspace 3")]
    [InlineData("app:terminal")]
    [InlineData("web:https://m365.cloud.microsoft/chat")]
    [InlineData("open:ms-settings:display")]
    [InlineData("send:Win+Period")]
    [InlineData("do:menu system")]
    [InlineData("run:C:\\Tools\\login.ps1")]
    public void Accepts_the_allowed_actions(string text) =>
        Assert.True(HotkeyAction.TryParse(text, out _, out var error), error);

    [Theory]
    [InlineData("wm:shell-exec cmd.exe")]
    [InlineData("app:cmd")]
    [InlineData("web:http://example.com")]
    [InlineData("open:file:///C:/Windows/System32/cmd.exe")]
    [InlineData("do:format-c")]
    [InlineData("nothing")]
    [InlineData("run:")]
    public void Rejects_everything_else(string text) =>
        Assert.False(HotkeyAction.TryParse(text, out _, out _));

    [Fact]
    public void Window_commands_split_on_semicolons()
    {
        HotkeyAction.TryParse("wm:move --workspace 3; focus --workspace 3", out var a, out _);
        Assert.Equal(new[] { "move --workspace 3", "focus --workspace 3" }, a.WmCommands);
    }
}

public class HotkeySetTests
{
    [Fact]
    public void Business_profile_has_no_problems()
    {
        var set = HotkeySet.Build(new CustomHotkeys());
        Assert.Empty(set.Problems);
        Assert.Contains(set.Active, b => b.Keys == "Win+Enter" && b.Do == "app:terminal");
        Assert.Contains(set.Active, b => b.Keys == "Win+Shift+G" && b.Do == "app:stackfield");
        Assert.Contains(set.Active, b => b.Keys == "Win+N" && b.Do == "app:notepad");
        Assert.Contains(set.Active, b => b.Keys == "Win+J" && b.Do == "do:toggle-split");
        Assert.Contains(set.Active, b => b.Keys == "Win+Ctrl+D" && b.Do == "do:work-mode 1");
        Assert.Contains(set.Active, b => b.Keys == "Win+Ctrl+2" && b.Do == "do:work-mode 2");
        Assert.Contains(set.Active, b => b.Keys == "Win+Ctrl+3" && b.Do == "do:work-mode 3");
    }

    [Fact]
    public void Workspace_bindings_expand_to_ten_workspaces()
    {
        var set = HotkeySet.Build(new CustomHotkeys());
        Assert.Contains(set.Active, b => b.Keys == "Win+1" && b.Do == "wm:focus --workspace 1");
        Assert.Contains(set.Active, b => b.Keys == "Win+0" && b.Do == "wm:focus --workspace 10");
    }

    [Fact]
    public void Custom_bindings_win_and_disabled_ones_disappear()
    {
        var custom = new CustomHotkeys
        {
            Bindings = { new HotkeyBinding { Keys = "Win+Enter", Do = "app:editor" } },
            Disabled = { "Win+W" },
        };
        var set = HotkeySet.Build(custom);
        Assert.Equal("app:editor", Assert.Single(set.Active, b => b.Keys == "Win+Enter").Do);
        Assert.DoesNotContain(set.Active, b => b.Keys == "Win+W");
    }

    [Fact]
    public void A_broken_custom_binding_blocks_applying()
    {
        var custom = new CustomHotkeys { Bindings = { new HotkeyBinding { Keys = "Win+L", Do = "app:terminal" } } };
        Assert.True(HotkeySet.Build(custom).HasBlockingProblems);
    }
}

public class AhkScriptTests
{
    static string Script(bool winAlone = true) =>
        AhkScript.Generate(HotkeySet.Build(new CustomHotkeys()),
            new AhkContext(@"C:\WinMarchy\App\WinMarchy.exe", @"C:\WinMarchy\Tools\glazewm\cli\glazewm.exe", winAlone));

    [Fact]
    public void Has_the_header_and_the_runtime()
    {
        var s = Script();
        Assert.StartsWith("; Generated by WinMarchy", s);
        Assert.Contains("#Requires AutoHotkey v2.0", s);
        Assert.Contains("AgentSend(message)", s);
        Assert.Contains("global WinMarchyExe := \"C:\\WinMarchy\\App\\WinMarchy.exe\"", s);
    }

    [Fact]
    public void Shortcuts_call_the_agent_or_send_keys()
    {
        var s = Script();
        Assert.Contains("Hotkey(\"$#Enter\", (*) => Act(\"app:terminal\"))", s);
        Assert.Contains("Hotkey(\"$#Space\", (*) => Act(\"do:menu\"))", s);
        Assert.Contains("Hotkey(\"$#c\", (*) => UniversalCopy())", s);
    }

    [Fact]
    public void The_win_key_alone_is_optional()
    {
        Assert.Contains("Hotkey(\"~LWin Up\", WinUp)", Script(winAlone: true));
        Assert.DoesNotContain("~LWin Up", Script(winAlone: false));
    }

    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("say \"hi\"", "\"say `\"hi`\"\"")]
    [InlineData("back`tick", "\"back``tick\"")]
    public void Quotes_strings_for_AutoHotkey(string value, string expected) =>
        Assert.Equal(expected, AhkScript.Quote(value));
}

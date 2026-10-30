using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinMarchy.App.Ui;
using WinMarchy.Core;
using WinMarchy.Core.GlazeWm;
using WinMarchy.Core.Hotkeys;
using WinMarchy.Core.Integrations;
using WinMarchy.Core.Themes;

namespace WinMarchy.App.Startup;

/// <summary>
/// "WinMarchy.exe --selftest &lt;folder&gt;" for CI: every page in light and dark as PNG, the generated
/// AutoHotkey script and GlazeWM configs for the real validators, and a list of texts that came out as
/// a bare locale key. Works in a temporary root and never touches the machine's settings.
/// </summary>
static class SelfTest
{
    static readonly string[] Routes = { "start", "setup", "themes", "hotkeys", "tiling", "restore", "settings", "log" };
    static readonly Regex BareKey = new(@"^[a-z][A-Za-z0-9]*(\.[A-Za-z0-9]+)+$");

    public static async Task<int> RunAsync(string outDir)
    {
        var problems = new List<string>();
        try
        {
            outDir = Path.GetFullPath(outDir);
            Directory.CreateDirectory(outDir);
            Paths.OverrideRoot(Path.Combine(outDir, "root"));
            L.Configure("de");
            Motion.ForceOff = true;
            AppModel.Initialize(new WicImageConverter());
            ThemeManager.Initialize("dark", AppModel.CurrentTheme.Palette.Accent);

            WriteGenerated(outDir);
            FakeWallpapers();
            await ShootPagesAsync(outDir, problems);
            if (OperatingSystem.IsWindows()) WindowsChecks(outDir, problems);
        }
        catch (Exception ex)
        {
            problems.Add("crash: " + ex);
        }
        File.WriteAllLines(Path.Combine(outDir, "selftest.txt"), problems.Count == 0 ? new[] { "ok" } : problems, new UTF8Encoding(true));
        return problems.Count == 0 ? 0 : 1;
    }

    static void WriteGenerated(string outDir)
    {
        var bom = new UTF8Encoding(true);
        var exe = @"C:\WinMarchy\App\WinMarchy.exe";
        var cli = @"C:\WinMarchy\Tools\glazewm\cli\glazewm.exe";
        var settings = new UserSettings();
        var palette = ThemeCatalog.Default.Palette;
        foreach (var personal in new[] { false, true })
        {
            var set = HotkeySet.Build(personal, new CustomHotkeys(), "autohotkey");
            var name = personal ? "winmarchy-hotkeys-personal.ahk" : "winmarchy-hotkeys.ahk";
            File.WriteAllText(Path.Combine(outDir, name), AhkScript.Generate(set, new AhkContext(exe, cli, WinKeyOpensLauncher: true)), bom);
        }
        File.WriteAllText(Path.Combine(outDir, "glazewm-config.yaml"), GlazeConfig.Generate(GlazeConfig.FromSettings(settings, palette, Bars.Height)));
        var glazeKeys = GlazeConfig.FromSettings(settings, palette, Bars.Height);
        glazeKeys.Keybindings = GlazeConfig.Keybindings(HotkeySet.Build(true, new CustomHotkeys(), "glazewm"), new UsKeyboardLayout(), exe);
        File.WriteAllText(Path.Combine(outDir, "glazewm-config-glazewm-engine.yaml"), GlazeConfig.Generate(glazeKeys));
        File.WriteAllText(Path.Combine(outDir, "zpack.json"), ZebarPack.Manifest(exe));
        File.WriteAllText(Path.Combine(outDir, "theme.css"), ZebarPack.ThemeCss(palette));
        File.WriteAllText(Path.Combine(outDir, "config.js"), ZebarPack.ConfigJs(exe, settings));
        // YASB's bar with a shortcut of the user's own in the menu and as a button.
        var yasb = settings.Clone();
        yasb.BarShortcuts.Add(new BarShortcut { Title = "Ticket & Co. {neu}", Action = "web:https://example.com/?a=1&b=2", InBar = true });
        File.WriteAllText(Path.Combine(outDir, "yasb-config.yaml"), YasbBar.Config(exe, yasb, fastfetch: true));
        File.WriteAllText(Path.Combine(outDir, "yasb-styles.css"), YasbBar.Styles(palette));
        File.WriteAllText(Path.Combine(outDir, "starship.toml"), ShellExtras.StarshipToml(palette));
        File.WriteAllText(Path.Combine(outDir, "fastfetch.jsonc"), ShellExtras.FastfetchConfig(palette));
        File.WriteAllText(Path.Combine(outDir, "starship-prompt.ps1"), ShellExtras.PromptScript(@"C:\WinMarchy\Tools\starship\starship.exe", @"C:\WinMarchy\Runtime\starship.toml"), bom);
        // Greenshot owns the Print keys when it is chosen; the script must still validate.
        var greenshot = AhkScript.Generate(HotkeySet.Build(false, new CustomHotkeys(), "autohotkey"),
            new AhkContext(exe, cli, WinKeyOpensLauncher: true, LeaveAlone: WinMarchy.Core.Platform.Screenshots.GreenshotKeys.ToHashSet()));
        File.WriteAllText(Path.Combine(outDir, "winmarchy-hotkeys-greenshot.ahk"), greenshot, bom);
    }

    static async Task ShootPagesAsync(string outDir, List<string> problems)
    {
        var window = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0, Width = 1180, Height = 820, ShowActivated = false };
        window.Show();
        await SettleAsync();
        foreach (var scheme in new[] { "dark", "light" })
        {
            ThemeManager.Set(scheme);
            foreach (var route in Routes)
            {
                window.Navigate(route);
                await SettleAsync();
                Save(window.Root, Path.Combine(outDir, $"{scheme}-{route}.png"));
                foreach (var text in Texts(window.Root).Where(t => BareKey.IsMatch(t)).Distinct())
                    problems.Add($"{route}: missing text '{text}'");
            }
        }

        // The Omarchy menu, drawn on its own (it is a separate window on the desktop). The search
        // preview needs the Start menu's program list first.
        await WinMarchy.Core.Platform.StartApps.Refresh(force: true);
        var menu = new Overlays.MenuPreview();
        foreach (var (name, build) in new (string, Action<Overlays.MenuPreview>)[]
                 {
                     ("menu", m => m.Root()), ("menu-system", m => m.Sub("system")), ("menu-style", m => m.Sub("style")),
                     ("themes-menu", m => m.Themes()), ("wallpaper-menu", m => m.Wallpapers()), ("keybindings", m => m.Bindings()),
                     ("menu-search", m => m.Search("note")),
                 })
        {
            build(menu);
            var card = menu.Card;
            card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            card.Arrange(new Rect(card.DesiredSize));
            card.UpdateLayout();
            Save(card, Path.Combine(outDir, $"overlay-{name}.png"));
            foreach (var text in Texts(card).Where(t => BareKey.IsMatch(t)).Distinct())
                problems.Add($"overlay {name}: missing text '{text}'");
        }
        window.Close();
    }

    /// <summary>Two plain pictures as the default theme's wallpapers, so the picker has something to show.</summary>
    static void FakeWallpapers()
    {
        var theme = ThemeCatalog.Default;
        var p = theme.Palette;
        foreach (var (asset, color) in theme.Backgrounds.Take(2).Zip(new[] { p.Accent, p.Magenta }))
        {
            var file = ThemeAssets.BackgroundFile(theme, asset);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush((Color)ColorConverter.ConvertFromString(p.Background)), null, new Rect(0, 0, 320, 180));
                dc.DrawEllipse(new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)), null, new Point(200, 90), 70, 70);
            }
            var bitmap = new RenderTargetBitmap(320, 180, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            BitmapEncoder encoder = file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? new PngBitmapEncoder() : new JpegBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(file);
            encoder.Save(stream);
        }
    }

    /// <summary>
    /// What only Windows can answer: the lock screen calls (straight COM, so a wrong slot would crash
    /// here and not on a user's PC) and the policy check. The CI runner's own lock screen may change;
    /// the machine is thrown away afterwards.
    /// </summary>
    static void WindowsChecks(string outDir, List<string> problems)
    {
        var lines = new List<string>();
        var lockScreen = new WinMarchy.Core.Platform.WindowsLockScreen();
        try
        {
            var before = lockScreen.Current();
            lines.Add($"lock screen read: {before?.Length ?? 0} bytes");
            var picture = ThemeAssets.DownloadedBackgrounds(ThemeCatalog.Default).FirstOrDefault();
            if (picture != null)
            {
                lockScreen.Set(picture);
                lines.Add($"lock screen set: {Path.GetFileName(picture)}, now {lockScreen.Current()?.Length ?? 0} bytes");
            }
        }
        catch (Exception ex)
        {
            // Windows Server may not offer the API; a wrong interface would not get this far.
            lines.Add("lock screen: " + ex.GetType().Name + ": " + ex.Message);
        }
        try
        {
            var findings = WinMarchy.Core.Platform.PolicyCheck.Run();
            lines.Add($"policies: {findings.Count}");
            foreach (var f in findings)
            {
                lines.Add($"  {f.Id}: {f.Location} = {f.Value} → {f.Effect}");
                if (BareKey.IsMatch(f.Effect)) problems.Add($"policy {f.Id}: missing text");
            }
        }
        catch (Exception ex)
        {
            problems.Add("policy check: " + ex);
        }
        File.WriteAllLines(Path.Combine(outDir, "windows-checks.txt"), lines, new UTF8Encoding(true));
    }

    static async Task SettleAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(350);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    static IEnumerable<string> Texts(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            if (d is TextBlock { Text.Length: > 0 } t) yield return t.Text;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) stack.Push(VisualTreeHelper.GetChild(d, i));
        }
    }

    static void Save(FrameworkElement element, string file)
    {
        var dpi = VisualTreeHelper.GetDpi(element);
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        // Without a background the transparent parts would come out black.
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(ThemeManager.ColorOf("Bg")), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }
}

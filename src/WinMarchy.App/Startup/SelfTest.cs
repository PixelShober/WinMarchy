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
            await ShootPagesAsync(outDir, problems);
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
        File.WriteAllText(Path.Combine(outDir, "glazewm-config.yaml"), GlazeConfig.Generate(GlazeConfig.FromSettings(settings, palette, ZebarPack.BarHeight)));
        var glazeKeys = GlazeConfig.FromSettings(settings, palette, ZebarPack.BarHeight);
        glazeKeys.Keybindings = GlazeConfig.Keybindings(HotkeySet.Build(true, new CustomHotkeys(), "glazewm"), new UsKeyboardLayout(), exe);
        File.WriteAllText(Path.Combine(outDir, "glazewm-config-glazewm-engine.yaml"), GlazeConfig.Generate(glazeKeys));
        File.WriteAllText(Path.Combine(outDir, "zpack.json"), ZebarPack.Manifest(exe));
        File.WriteAllText(Path.Combine(outDir, "theme.css"), ZebarPack.ThemeCss(palette));
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

        // The Omarchy menu, drawn on its own (it is a separate window on the desktop).
        var menu = new Overlays.MenuPreview();
        foreach (var (name, build) in new (string, Action<Overlays.MenuPreview>)[] { ("menu", m => m.Root()), ("menu-system", m => m.Sub("system")), ("themes-menu", m => m.Themes()), ("keybindings", m => m.Bindings()) })
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

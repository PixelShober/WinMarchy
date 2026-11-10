using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WinMarchy.Core;
using WinMarchy.Core.Themes;

namespace WinMarchy.App.Ui;

/// <summary>
/// Light and dark for WinMarchy's own window. Neutral grounds, one accent – the accent of the Omarchy
/// theme in use, lifted to readable contrast for each scheme. "system" follows Windows live.
/// Every brush is a DynamicResource, so a switch repaints without rebuilding a page.
/// </summary>
public static class ThemeManager
{
    public static string Scheme { get; private set; } = "system";

    public static bool IsDark { get; private set; } = true;

    public static string AccentSource { get; private set; } = "#7aa2f7";

    public static event Action? Changed;

    static readonly Dictionary<string, (string Light, string Dark)> Neutrals = new()
    {
        ["Bg"] = ("#f6f6f8", "#101116"),
        ["Sidebar"] = ("#ebebef", "#0b0c10"),
        ["Surface"] = ("#ffffff", "#16171d"),
        ["Surface2"] = ("#f0f0f4", "#1d1f26"),
        ["Border"] = ("#dfe0e6", "#262832"),
        ["BorderStrong"] = ("#c6c8d1", "#3a3d4b"),
        ["Text"] = ("#17181d", "#e6e7ee"),
        ["TextMuted"] = ("#585b6b", "#a1a4b6"),
        ["Ok"] = ("#1d7343", "#79d49b"),
        ["Warn"] = ("#8a5600", "#e8bd62"),
        ["Error"] = ("#b02430", "#f3848b"),
        ["Backdrop"] = ("#66101116", "#99000000"),
    };

    public static void Initialize(string scheme, string accent)
    {
        Scheme = scheme;
        AccentSource = ColorMath.IsHex(accent) ? accent : "#7aa2f7";
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && Scheme == "system")
                Application.Current?.Dispatcher.Invoke(Apply);
        };
        Apply();
    }

    public static bool WindowsUsesDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light ? light == 0 : true;
        }
        catch
        {
            return true;
        }
    }

    public static void Set(string scheme, string? accent = null)
    {
        Scheme = scheme;
        if (accent != null && ColorMath.IsHex(accent)) AccentSource = accent;
        Apply();
    }

    static void Apply()
    {
        IsDark = Scheme == "dark" || (Scheme == "system" && WindowsUsesDark());
        var r = Application.Current.Resources;
        string Pick((string Light, string Dark) v) => IsDark ? v.Dark : v.Light;
        foreach (var (key, v) in Neutrals) r[key] = Brush(Pick(v));

        var bg = Pick(Neutrals["Bg"]);
        var surface = Pick(Neutrals["Surface"]);
        var accent = ColorMath.Legible(AccentSource, surface, 3.0);
        var accentFg = ColorMath.Legible(AccentSource, bg, 4.5);
        var accentText = ColorMath.Contrast("#ffffff", accent) >= ColorMath.Contrast("#0b0c10", accent) ? "#ffffff" : "#0b0c10";
        r["Accent"] = Brush(accent);
        r["AccentHover"] = Brush(ColorMath.Mix(accent, IsDark ? "#ffffff" : "#000000", 0.12));
        r["AccentText"] = Brush(accentText);
        r["AccentFg"] = Brush(accentFg);
        r["AccentSoft"] = Brush(ColorMath.Mix(bg, accent, IsDark ? 0.16 : 0.12));
        r["AccentSoftHover"] = Brush(ColorMath.Mix(bg, accent, IsDark ? 0.08 : 0.06));
        r["Selection"] = Brush(ColorMath.Mix(surface, accent, 0.35));
        r["OkSoft"] = Brush(ColorMath.Mix(bg, Pick(Neutrals["Ok"]), 0.14));
        r["WarnSoft"] = Brush(ColorMath.Mix(bg, Pick(Neutrals["Warn"]), 0.14));
        r["ErrorSoft"] = Brush(ColorMath.Mix(bg, Pick(Neutrals["Error"]), 0.14));
        r["AccentColor"] = (Color)ColorConverter.ConvertFromString(accent);

        foreach (Window w in Application.Current.Windows) ApplyFrame(w);
        Changed?.Invoke();
    }

    static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public static Color ColorOf(string key) => ((SolidColorBrush)Application.Current.Resources[key]).Color;

    /// <summary>Title bar in the colours of the sidebar (Windows 11), dark title bar on Windows 10.</summary>
    public static void ApplyFrame(Window w)
    {
        var handle = new WindowInteropHelper(w).Handle;
        if (handle == IntPtr.Zero) return;
        int dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
        var caption = ColorOf("Sidebar");
        int colorref = caption.R | caption.G << 8 | caption.B << 16;
        DwmSetWindowAttribute(handle, 35 /* DWMWA_CAPTION_COLOR */, ref colorref, sizeof(int));
        var text = ColorOf("Text");
        int textref = text.R | text.G << 8 | text.B << 16;
        DwmSetWindowAttribute(handle, 36 /* DWMWA_TEXT_COLOR */, ref textref, sizeof(int));
        w.Background = new SolidColorBrush(ColorOf("Bg"));
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // ------------------------------------------------------------------ reveal

    /// <summary>
    /// The new look grows as a circle out of the control that was pressed, as in Ember: the old look is
    /// a snapshot on top whose clip loses a growing circle. Without animations (Windows setting) it switches directly.
    /// </summary>
    public static void SwitchWithReveal(FrameworkElement root, System.Windows.Controls.Panel overlayHost, FrameworkElement origin, Action apply)
    {
        if (!Motion.Enabled || root.ActualWidth < 1 || root.ActualHeight < 1)
        {
            apply();
            return;
        }
        var dpi = VisualTreeHelper.GetDpi(root);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();

        var snapshot = new System.Windows.Controls.Image
        {
            Source = bitmap,
            Width = root.ActualWidth,
            Height = root.ActualHeight,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
        };
        var center = origin.TranslatePoint(new Point(origin.ActualWidth / 2, origin.ActualHeight / 2), root);
        double reach = new[]
        {
            (center - new Point(0, 0)).Length, (center - new Point(root.ActualWidth, 0)).Length,
            (center - new Point(0, root.ActualHeight)).Length, (center - new Point(root.ActualWidth, root.ActualHeight)).Length,
        }.Max();
        var circle = new EllipseGeometry(center, 0, 0);
        snapshot.Clip = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, root.ActualWidth, root.ActualHeight)), circle);
        overlayHost.Children.Add(snapshot);

        apply();

        var grow = new DoubleAnimation(0, reach, TimeSpan.FromMilliseconds(500)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        grow.Completed += (_, _) => overlayHost.Children.Remove(snapshot);
        circle.BeginAnimation(EllipseGeometry.RadiusXProperty, grow);
        circle.BeginAnimation(EllipseGeometry.RadiusYProperty, grow.Clone());
    }
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WinMarchy.Core.Themes;

namespace WinMarchy.App.Overlays;

/// <summary>
/// Menus and notifications look like Omarchy's, not like WinMarchy's own window: square, a 2 px
/// border in the theme's accent, the theme's background and a monospace font.
/// </summary>
sealed class OverlayLook
{
    public required SolidColorBrush Background { get; init; }
    public required SolidColorBrush Foreground { get; init; }
    public required SolidColorBrush Muted { get; init; }
    public required SolidColorBrush Accent { get; init; }
    public required SolidColorBrush Selection { get; init; }
    public required SolidColorBrush Divider { get; init; }
    public required SolidColorBrush Scrim { get; init; }

    public static readonly FontFamily Mono = new("Cascadia Mono, Cascadia Code, Consolas");

    public static OverlayLook Current()
    {
        var p = AppModel.CurrentTheme.Palette;
        var bg = p.Background;
        var accent = ColorMath.Legible(p.Accent, bg, 3.0);
        return new OverlayLook
        {
            Background = Brush(bg),
            Foreground = Brush(p.Foreground),
            Muted = Brush(ColorMath.Legible(p.Muted, bg, 4.5)),
            Accent = Brush(accent),
            Selection = Brush(ColorMath.Mix(bg, accent, 0.2)),
            Divider = Brush(ColorMath.Mix(bg, p.Foreground, 0.2)),
            Scrim = Brush("#000000", 0x59),
        };
    }

    static SolidColorBrush Brush(string hex, byte alpha = 0xff)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        c.A = alpha;
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

/// <summary>Where overlays go: the monitor under the mouse, in physical pixels (each monitor can have its own scale).</summary>
static class Placement
{
    public static System.Drawing.Rectangle CursorWorkArea() =>
        System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;

    public static void MovePixels(Window w, int x, int y, int width, int height, bool activate)
    {
        var hwnd = new WindowInteropHelper(w).EnsureHandle();
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, width, height, SWP_NOOWNERZORDER | (activate ? 0 : SWP_NOACTIVATE));
    }

    public static void MoveTopLeftPixels(Window w, int x, int y)
    {
        var hwnd = new WindowInteropHelper(w).EnsureHandle();
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    /// <summary>Tool window, never in Alt+Tab; optionally click-through and never focused (notifications).</summary>
    public static void MakeOverlay(Window w, bool clickThrough)
    {
        var hwnd = new WindowInteropHelper(w).EnsureHandle();
        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        style |= WS_EX_TOOLWINDOW;
        if (clickThrough) style |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
    }

    /// <summary>
    /// A hotkey arrives while another program is in front; Windows only lets the program the user
    /// last typed into hand over the focus. AutoHotkey allows it before it sends the action, this is
    /// the fallback.
    /// </summary>
    public static void ForceForeground(Window w)
    {
        var hwnd = new WindowInteropHelper(w).EnsureHandle();
        if (GetForegroundWindow() == hwnd) return;
        var foreground = GetForegroundWindow();
        var fgThread = GetWindowThreadProcessId(foreground, out _);
        var ownThread = GetCurrentThreadId();
        bool attached = fgThread != ownThread && AttachThreadInput(ownThread, fgThread, true);
        try
        {
            SetForegroundWindow(hwnd);
            w.Activate();
        }
        finally
        {
            if (attached) AttachThreadInput(ownThread, fgThread, false);
        }
    }

    static readonly IntPtr HWND_TOPMOST = new(-1);
    const uint SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200;
    const int GWL_EXSTYLE = -20;
    const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint attach, uint to, bool on);
}

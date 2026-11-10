using System.Runtime.InteropServices;

namespace WinMarchy.Core.Platform;

/// <summary>
/// Omarchy's scratchpad glides in from the bottom edge and back out. GlazeWM has no animations, so
/// WinMarchy moves the windows itself for about a tenth of a second (straight Win32, every frame),
/// then hands the final place to GlazeWM. Off when Windows' own animations are off.
/// </summary>
public static class WindowSlide
{
    const int Frames = 8;
    const int FrameMs = 14;

    /// <summary>Windows' "Animation effects" setting (Settings → Accessibility → Visual effects).</summary>
    public static bool Enabled
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            return !SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, out var on, 0) || on;
        }
    }

    /// <summary>Windows that stand <paramref name="dy"/> pixels below their place move up into it.</summary>
    public static Task InAsync(IReadOnlyCollection<long> windows, int dy, CancellationToken ct) =>
        RunAsync(windows, t => -(int)Math.Round(dy * (1 - Math.Pow(1 - t, 3))), ct);

    /// <summary>Windows move <paramref name="dy"/> pixels down, speeding up, before they are hidden.</summary>
    public static Task OutAsync(IReadOnlyCollection<long> windows, int dy, CancellationToken ct) =>
        RunAsync(windows, t => (int)Math.Round(dy * t * t), ct);

    static async Task RunAsync(IReadOnlyCollection<long> windows, Func<double, int> offset, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows() || windows.Count == 0) return;
        var start = new Dictionary<long, RECT>();
        foreach (var w in windows)
            if (GetWindowRect(new IntPtr(w), out var r)) start[w] = r;
        for (int f = 1; f <= Frames; f++)
        {
            var dy = offset(f / (double)Frames);
            foreach (var (w, r) in start)
                SetWindowPos(new IntPtr(w), IntPtr.Zero, r.Left, r.Top + dy, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            await Task.Delay(FrameMs, ct);
        }
    }

    const uint SPI_GETCLIENTAREAANIMATION = 0x1042;
    const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200;

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, out bool value, uint winIni);
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinMarchy.Core;
using WinMarchy.Core.Platform;

namespace WinMarchy.App.Overlays;

/// <summary>
/// Greenshot puts every capture straight into the clipboard (WinMarchy sets it up that way). Right
/// after, this opens WinMarchy's menu for the picture: save it, save it elsewhere, edit it in
/// Greenshot. Copying a picture in Greenshot's own editor opens nothing: the user is busy there.
/// </summary>
sealed class ScreenshotWatch : IDisposable
{
    const int WM_CLIPBOARDUPDATE = 0x031D;
    /// <summary>Greenshot's selection overlay; it may still be in front while the capture goes out.</summary>
    const string CaptureForm = "Greenshot capture form";

    readonly HwndSource _window;
    readonly Func<UserSettings> _settings;
    DateTime _lastOffer;

    public ScreenshotWatch(Func<UserSettings> settings)
    {
        _settings = settings;
        // Message-only: never on the screen, never in Alt+Tab.
        _window = new HwndSource(new HwndSourceParameters("WinMarchy clipboard") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
        _window.AddHook(WndProc);
        if (!AddClipboardFormatListener(_window.Handle)) Log.Debug("clipboard listener: error " + Marshal.GetLastWin32Error());
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_CLIPBOARDUPDATE || _settings().ScreenshotTool != Screenshots.Greenshot || !FromGreenshot()) return IntPtr.Zero;
        // One capture may announce itself more than once.
        if (DateTime.Now - _lastOffer < TimeSpan.FromSeconds(1)) return IntPtr.Zero;
        _lastOffer = DateTime.Now;
        Application.Current?.Dispatcher.InvokeAsync(Offer, DispatcherPriority.Background);
        return IntPtr.Zero;
    }

    /// <summary>Greenshot wrote the clipboard, and not while its editor is in front.</summary>
    static bool FromGreenshot()
    {
        var owner = GetClipboardOwner();
        if (owner == IntPtr.Zero || !IsGreenshot(owner)) return false;
        var front = GetForegroundWindow();
        return !IsGreenshot(front) || Title(front) == CaptureForm;
    }

    static void Offer()
    {
        if (Read() is { } png) MenuWindow.ShowScreenshot(png);
    }

    /// <summary>The picture as PNG: Greenshot's own PNG copy if it put one there, else the bitmap, encoded.</summary>
    static byte[]? Read()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Clipboard.GetData("PNG") is MemoryStream stream) return stream.ToArray();
                if (Clipboard.GetImage() is not { } image) return null;
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                return ms.ToArray();
            }
            catch (Exception ex) when (ex is COMException or ExternalException)
            {
                // Someone else still holds the clipboard open.
                Log.Debug("clipboard: " + ex.Message);
                Thread.Sleep(100);
            }
        }
        return null;
    }

    static bool IsGreenshot(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName.Equals("Greenshot", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    static string Title(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public void Dispose()
    {
        RemoveClipboardFormatListener(_window.Handle);
        _window.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)] static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
}

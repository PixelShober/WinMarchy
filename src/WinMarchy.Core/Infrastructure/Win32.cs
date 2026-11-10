using System.Runtime.InteropServices;
using System.Text;
using WinMarchy.Core.Backup;

namespace WinMarchy.Core;

/// <summary>The few Windows APIs the core needs. Every call is a no-op outside Windows.</summary>
public static class Win32
{
    const int SPI_GETDESKWALLPAPER = 0x0073;
    const int SPI_SETDESKWALLPAPER = 0x0014;
    const int SPIF_UPDATEINIFILE = 0x01;
    const int SPIF_SENDCHANGE = 0x02;
    const int WM_SETTINGCHANGE = 0x001A;
    static readonly IntPtr HWND_BROADCAST = new(0xffff);

    /// <summary>Tells running programs that colours or the theme changed (title bars, Explorer, Office).</summary>
    public static void BroadcastSettingChange(string area)
    {
        if (!OperatingSystem.IsWindows()) return;
        SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, area, 0x0002 /* SMTO_ABORTIFHUNG */, 2000, out _);
    }

    /// <summary>Explorer re-reads file and folder associations (after the default file manager changed).</summary>
    public static void AssociationsChanged()
    {
        if (OperatingSystem.IsWindows()) SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
    }

    public static string? GetWallpaper()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var sb = new StringBuilder(1024);
        return SystemParametersInfo(SPI_GETDESKWALLPAPER, sb.Capacity, sb, 0) ? sb.ToString() : null;
    }

    public static bool SetWallpaper(string path)
    {
        if (!OperatingSystem.IsWindows()) return false;
        return SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
    }

    public static void LockWorkstation()
    {
        if (OperatingSystem.IsWindows()) LockWorkStation();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SystemParametersInfo(int action, int param, StringBuilder value, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SystemParametersInfo(int action, int param, string value, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SendMessageTimeout(IntPtr hwnd, int msg, IntPtr wParam, string lParam, int flags, int timeout, out IntPtr result);

    [DllImport("user32.dll")]
    static extern bool LockWorkStation();

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}

/// <summary>The real wallpaper of the signed-in user.</summary>
public sealed class WindowsWallpaper : IWallpaperAccess
{
    public string? Current() => Win32.GetWallpaper();
    public void Set(string path) => Win32.SetWallpaper(path);
}

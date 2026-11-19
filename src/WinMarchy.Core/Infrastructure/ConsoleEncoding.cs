using System.Globalization;
using System.Text;
using Microsoft.Win32;

namespace WinMarchy.Core;

/// <summary>
/// Console tools such as powercfg, gpupdate or whoami write their output in the OEM code page
/// (CP850 on a German Windows), not in UTF-8. Decoding it as UTF-8 turns "für" into "f", U+FFFD, "r".
/// This class provides the right decoder.
/// </summary>
public static class ConsoleEncoding
{
    static ConsoleEncoding() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    static Encoding? _oem;

    /// <summary>The OEM code page of this machine, which new consoles (and so our child processes) start with.</summary>
    public static Encoding Oem => _oem ??= ForCodePage(SystemOemCodePage());

    public static Encoding ForCodePage(int codePage) => Encoding.GetEncoding(codePage);

    public static string Decode(byte[] bytes, int codePage) => ForCodePage(codePage).GetString(bytes);

    /// <summary>
    /// GetOEMCP() is no help here: WinMarchy's manifest sets the process code page to UTF-8, and then
    /// GetOEMCP() answers 65001 for our process, while powercfg &amp; co. still write CP850.
    /// The system value is in the registry; the culture's OEM code page is the fallback.
    /// </summary>
    internal static int SystemOemCodePage()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Nls\CodePage");
                if (key?.GetValue("OEMCP") is string value && int.TryParse(value, out var cp) && cp > 0 && cp != 65001)
                    return cp;
            }
            catch
            {
                // fall through
            }
        }
        var fromCulture = CultureInfo.InstalledUICulture.TextInfo.OEMCodePage;
        return fromCulture is > 0 and not 65001 ? fromCulture : 850;
    }
}

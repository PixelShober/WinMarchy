using System.Windows.Media.Imaging;
using WinMarchy.Core;
using WinMarchy.Core.Integrations;

namespace WinMarchy.App.Startup;

/// <summary>
/// Some Omarchy wallpapers are WebP, which Windows cannot always use as a background. WPF decodes them
/// through WIC (the WebP codec ships with Windows 10 1809 and later) and writes a JPEG.
/// </summary>
sealed class WicImageConverter : IImageConverter
{
    public bool ToJpeg(string source, string target)
    {
        try
        {
            BitmapFrame frame;
            using (var input = File.OpenRead(source))
                frame = BitmapFrame.Create(input, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
            encoder.Frames.Add(frame);
            var tmp = target + ".tmp";
            using (var output = File.Create(tmp)) encoder.Save(output);
            File.Move(tmp, target, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or System.Runtime.InteropServices.COMException)
        {
            Log.Debug("WIC: " + ex.Message);
            return false;
        }
    }
}

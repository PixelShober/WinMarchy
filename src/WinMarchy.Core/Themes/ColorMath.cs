using System.Globalization;

namespace WinMarchy.Core.Themes;

/// <summary>sRGB helpers: luminance, WCAG contrast, mixing, and making a colour readable on a background.</summary>
public static class ColorMath
{
    public static bool IsHex(string? hex) =>
        hex != null && hex.Length == 7 && hex[0] == '#' && hex.Skip(1).All(Uri.IsHexDigit);

    public static (int R, int G, int B) Rgb(string hex) => (
        int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber),
        int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber),
        int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber));

    public static string Hex(int r, int g, int b) =>
        $"#{Math.Clamp(r, 0, 255):x2}{Math.Clamp(g, 0, 255):x2}{Math.Clamp(b, 0, 255):x2}";

    public static double Luminance(string hex)
    {
        var (r, g, b) = Rgb(hex);
        static double Channel(int v)
        {
            var x = v / 255.0;
            return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    public static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static bool IsDark(string hex) => Luminance(hex) < 0.18;

    /// <summary>a mixed with b; amount 0 = a, 1 = b.</summary>
    public static string Mix(string a, string b, double amount)
    {
        var (ar, ag, ab) = Rgb(a);
        var (br, bg, bb) = Rgb(b);
        int M(int x, int y) => (int)Math.Round(x * (1 - amount) + y * amount);
        return Hex(M(ar, br), M(ag, bg), M(ab, bb));
    }

    /// <summary>
    /// The colour itself if it reaches <paramref name="min"/> contrast on the background; otherwise it is
    /// moved towards white (dark background) or black (light background) until it does.
    /// </summary>
    public static string Legible(string hex, string background, double min = 4.5)
    {
        if (!IsHex(hex)) hex = IsDark(background) ? "#f2f4fa" : "#161b22";
        if (Contrast(hex, background) >= min) return hex;
        var target = IsDark(background) ? "#ffffff" : "#000000";
        for (int i = 1; i <= 20; i++)
        {
            var candidate = Mix(hex, target, i / 20.0);
            if (Contrast(candidate, background) >= min) return candidate;
        }
        return target;
    }

    /// <summary>Windows stores colours as 0xAABBGGRR.</summary>
    public static int ToAbgr(string hex, byte alpha = 0xff)
    {
        var (r, g, b) = Rgb(hex);
        return unchecked((int)((uint)alpha << 24 | (uint)b << 16 | (uint)g << 8 | (uint)r));
    }

    /// <summary>DWM's ColorizationColor is 0xAARRGGBB.</summary>
    public static int ToArgbInt(string hex, byte alpha = 0xff)
    {
        var (r, g, b) = Rgb(hex);
        return unchecked((int)((uint)alpha << 24 | (uint)r << 16 | (uint)g << 8 | (uint)b));
    }

    /// <summary>"#rrggbb" → "#AARRGGBB" as Files and XAML expect it.</summary>
    public static string Argb(string hex, byte alpha = 0xff) => $"#{alpha:X2}{hex[1..].ToUpperInvariant()}";
}

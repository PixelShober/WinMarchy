using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinMarchy.Core.Hotkeys;

namespace WinMarchy.App.Ui;

/// <summary>Small building blocks the pages put together in code.</summary>
public static class UiKit
{
    public static Style S(string key) => (Style)Application.Current.FindResource(key);

    public static TextBlock Text(string text, string style = "Body", Thickness? margin = null) =>
        new() { Text = text, Style = S(style), Margin = margin ?? new Thickness(0) };

    /// <summary>"ok", "warn", "error", "accent" or "muted".</summary>
    public static Border Badge(string text, string kind)
    {
        var (bg, fg) = kind switch
        {
            "ok" => ("OkSoft", "Ok"),
            "warn" => ("WarnSoft", "Warn"),
            "error" => ("ErrorSoft", "Error"),
            "accent" => ("AccentSoft", "AccentFg"),
            _ => ("Surface2", "TextMuted"),
        };
        var t = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.Medium };
        t.SetResourceReference(TextBlock.ForegroundProperty, fg);
        var b = new Border { Style = S("Badge"), Child = t };
        b.SetResourceReference(Border.BackgroundProperty, bg);
        return b;
    }

    /// <summary>"Win + Shift + Enter" as key caps, with the character of the active keyboard layout.</summary>
    public static StackPanel Keycaps(string keys)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (!KeyCombo.TryParse(keys, out var combo, out _))
        {
            panel.Children.Add(new TextBlock { Text = keys, Style = S("Mono") });
            return panel;
        }
        foreach (var part in combo.Display(KeyboardLayouts.Current).Split(" + "))
            panel.Children.Add(new Border { Style = S("Keycap"), Child = new TextBlock { Text = part, Style = S("Mono"), FontSize = 12 } });
        return panel;
    }

    public static Border Swatch(string hex, double height = 14) => new()
    {
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
        Height = height,
        ToolTip = hex,
    };

    /// <summary>Loads an image without keeping the file open (themes are re-downloaded in place).</summary>
    public static BitmapImage? LoadImage(string? path, int decodeWidth = 0)
    {
        if (path == null || !File.Exists(path)) return null;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (decodeWidth > 0) img.DecodePixelWidth = decodeWidth;
            img.UriSource = new Uri(path);
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch
        {
            return null;
        }
    }

    public static Button IconTextButton(string icon, string text, string style = "SmallButton")
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Icon { Kind = icon, Size = 13, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = text, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return new Button { Style = S(style), Content = row };
    }

    public static CheckBox Switch(string label, bool value, string? hint = null)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = label, Style = S("Body") });
        if (hint != null) content.Children.Add(new TextBlock { Text = hint, Style = S("Small") });
        return new CheckBox { Style = S("Switch"), Content = content, IsChecked = value, Margin = new Thickness(0, 0, 0, 14) };
    }

    public static void OpenFolder(string path)
    {
        try
        {
            if (File.Exists(path)) System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"")?.Dispose();
            else
            {
                Directory.CreateDirectory(path);
                System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\"")?.Dispose();
            }
        }
        catch
        {
            // Explorer blocked by policy: nothing to do.
        }
    }
}

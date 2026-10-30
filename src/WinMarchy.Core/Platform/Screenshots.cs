using System.Diagnostics;
using WinMarchy.Core.Hotkeys;
using WinMarchy.Core.Tools;

namespace WinMarchy.Core.Platform;

/// <summary>
/// Print and Win+Ctrl+Alt+C: Windows' Snipping Tool, or Greenshot if the user picked it. Greenshot
/// listens on its own keys (Print, Alt+Print, Ctrl+Print …); WinMarchy presses the one Greenshot's
/// settings name, and leaves those keys to Greenshot in the hotkey script.
/// </summary>
public static class Screenshots
{
    public const string Windows = "windows";
    public const string Greenshot = "greenshot";

    /// <summary>The keys Greenshot takes by default; with Greenshot on, AutoHotkey does not grab them.</summary>
    public static readonly IReadOnlyList<string> GreenshotKeys = new[]
    {
        "PrintScreen", "Alt+PrintScreen", "Ctrl+PrintScreen", "Shift+PrintScreen", "Ctrl+Shift+PrintScreen",
    };

    /// <summary>Greenshot.ini: portable next to the exe (Data\Settings), else in %APPDATA%\Greenshot.</summary>
    public static string? GreenshotIni(string? exe, Locations loc)
    {
        var candidates = new List<string>();
        if (exe != null && Path.GetDirectoryName(exe) is { } dir)
        {
            candidates.Add(Path.Combine(dir, "Data", "Settings", "Greenshot.ini"));
            candidates.Add(Path.Combine(dir, "Greenshot.ini"));
        }
        candidates.Add(Path.Combine(loc.AppData, "Greenshot", "Greenshot.ini"));
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>The key Greenshot listens on for a capture ("region" by default), as "Ctrl+PrintScreen".</summary>
    public static string GreenshotHotkey(string? iniText, string mode)
    {
        var (name, fallback) = mode switch
        {
            "window" => ("WindowHotkey", "Alt+PrintScreen"),
            "screen" => ("FullscreenHotkey", "Ctrl+PrintScreen"),
            _ => ("RegionHotkey", "PrintScreen"),
        };
        if (iniText == null) return fallback;
        bool core = false;
        foreach (var raw in iniText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) core = line.Equals("[Core]", StringComparison.OrdinalIgnoreCase);
            else if (core && line.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                var value = line[(name.Length + 1)..].Replace(" ", "");
                return KeyCombo.TryParse(value, out var combo, out _) ? combo.ToString() : fallback;
            }
        }
        return fallback;
    }

    public static async Task TakeAsync(UserSettings settings, string? mode, Action<string> notify, CancellationToken ct)
    {
        if (settings.ScreenshotTool == Greenshot)
        {
            var exe = ToolCatalog.Detect(ToolCatalog.Greenshot, Locations.Current, settings, AppState.Load()).Path;
            if (exe != null)
            {
                if (Process.GetProcessesByName("Greenshot").Length == 0)
                {
                    AppLauncher.Start(exe);
                    // Greenshot registers its keys a moment after it starts.
                    await Task.Delay(2500, ct);
                }
                var ini = GreenshotIni(exe, Locations.Current);
                KeySender.Send(KeyCombo.Parse(GreenshotHotkey(ini != null ? FileSafety.ReadShared(ini) : null, mode ?? "region")));
                return;
            }
            notify(L.T("screenshot.greenshotMissing"));
        }
        KeySender.Send(KeyCombo.Parse("Win+Shift+S"));
    }
}

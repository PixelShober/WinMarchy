using System.Diagnostics;
using WinMarchy.Core.Hotkeys;
using WinMarchy.Core.Tools;

namespace WinMarchy.Core.Platform;

/// <summary>
/// Print and Win+Ctrl+Alt+C: Greenshot once it is installed, else Windows' Snipping Tool. Greenshot
/// listens on its own keys (Print, Alt+Print, Ctrl+Print …); WinMarchy presses the one Greenshot's
/// settings name, and leaves those keys to Greenshot in the hotkey script. Every Greenshot capture
/// goes straight to the clipboard; WinMarchy then offers to save it.
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

    /// <summary>
    /// Greenshot-fixed.ini: settings Greenshot reads on top of its own and does not change. Greenshot
    /// looks for it in Data\Settings in the PortableApps layout, next to the exe if one is there, else
    /// in %APPDATA%\Greenshot.
    /// </summary>
    public static string GreenshotFixedIni(string exe, Locations loc)
    {
        var dir = Path.GetDirectoryName(exe) ?? "";
        if (Directory.Exists(Path.Combine(dir, "App", "Greenshot"))) return Path.Combine(dir, "Data", "Settings", "Greenshot-fixed.ini");
        var beside = Path.Combine(dir, "Greenshot-fixed.ini");
        return File.Exists(beside) ? beside : Path.Combine(loc.AppData, "Greenshot", "Greenshot-fixed.ini");
    }

    /// <summary>
    /// The fixed settings with every capture going to the clipboard (<paramref name="clipboard"/>), or
    /// without WinMarchy's line again. Other lines stay. Null: nothing is left, the file can go.
    /// </summary>
    public static string? GreenshotFixed(string? original, bool clipboard)
    {
        var lines = (original ?? "").Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        int core = lines.FindIndex(l => l.Trim().Equals("[Core]", StringComparison.OrdinalIgnoreCase));
        int end = core < 0 ? -1 : lines.FindIndex(core + 1, l => l.TrimStart().StartsWith('['));
        if (end < 0) end = lines.Count;
        for (int i = end - 1; i > core && core >= 0; i--)
            if (lines[i].TrimStart().StartsWith("Destinations=", StringComparison.OrdinalIgnoreCase)) lines.RemoveAt(i);
        if (clipboard)
        {
            if (core < 0)
            {
                lines.Add("[Core]");
                core = lines.Count - 1;
            }
            lines.Insert(core + 1, "Destinations=Clipboard");
        }
        bool empty = lines.All(l => l.Trim().Length == 0 || l.TrimStart().StartsWith(';') || l.TrimStart().StartsWith('['));
        return empty ? null : string.Join("\r\n", lines) + "\r\n";
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

    /// <summary>Pictures\Screenshots, where Windows' Snipping Tool saves too.</summary>
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    /// <summary>A free file name for a screenshot taken at <paramref name="at"/> in <paramref name="folder"/>.</summary>
    public static string NewFile(string folder, DateTime at)
    {
        var name = L.T("screenshot.fileName", ("date", at.ToString("yyyy-MM-dd HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture)));
        var path = Path.Combine(folder, name + ".png");
        for (int n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{name} ({n}).png");
        return path;
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

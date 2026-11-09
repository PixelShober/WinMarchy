using System.Text.Json.Nodes;
using WinMarchy.Core.Themes;

namespace WinMarchy.Core.Integrations;

/// <summary>
/// Fluent Search as the launcher behind the Win key. Its settings are one JSON file per settings
/// page (a list of { "SettingName", "Value" }) in a folder named "Blast": next to FluentSearch.exe
/// for the portable build, %AppData%\Blast for the installed one. A page that is missing comes from
/// WinMarchy's template (the settings the user handed over, without search history, pinned results,
/// paths and user id); WinMarchy then sets what it relies on: the hotkey Alt+F24, no autostart of its
/// own (WinMarchy starts it), and the "Omarchy" look in the theme's accent colour. Fluent Search is
/// ended before its files change, so it does not write its old values back.
/// </summary>
public static class FluentSearchConfig
{
    /// <summary>The process name, without ".exe".</summary>
    public const string Process = "FluentSearch";

    /// <summary>Alt+F24 as Fluent Search stores a hotkey: the virtual-key codes (VK_MENU, VK_F24).</summary>
    static readonly int[] Hotkey = { 0x12, 0x87 };

    const string ResourcePrefix = "WinMarchy.FluentSearch.";

    /// <summary>
    /// The folders WinMarchy writes Fluent Search's settings to. Where Fluent Search really reads them
    /// is learnt once on this PC (<see cref="LearnSettingsDirAsync"/>, kept in state.json): then that
    /// folder and the portable one. Until then every place it may use: next to the exe (its
    /// documentation), %LocalAppData%\Blast and %AppData%\Blast.
    /// </summary>
    public static IReadOnlyList<string> SettingsDirs(Locations loc, string? exe)
    {
        var dirs = new List<string> { SettingsDir(loc, exe) };
        if (AppState.Load().FluentSearchSettingsDir is { } learnt) dirs.Add(learnt);
        else dirs.AddRange(new[] { Path.Combine(loc.LocalAppData, "Blast"), Path.Combine(loc.AppData, "Blast") });
        return dirs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Folders that may hold Fluent Search's settings: the known places, and any folder up to two levels down in %LocalAppData% or %AppData% with an Appearance.json.</summary>
    internal static IEnumerable<string> CandidateDirs(Locations loc, string exe)
    {
        yield return SettingsDir(loc, exe);
        yield return Path.Combine(loc.LocalAppData, "Blast");
        yield return Path.Combine(loc.AppData, "Blast");
        foreach (var root in new[] { loc.LocalAppData, loc.AppData, Path.GetDirectoryName(exe)! })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in SafeDirs(root).Concat(SafeDirs(root).SelectMany(SafeDirs)))
                if (File.Exists(Path.Combine(dir, "Appearance.json"))) yield return dir;
        }
    }

    static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.GetDirectories(dir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    /// <summary>Every settings page in the candidate folders, with when it was last written.</summary>
    static Dictionary<string, DateTime> Snapshot(Locations loc, string exe)
    {
        var files = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in CandidateDirs(loc, exe).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*.json")) files[f] = File.GetLastWriteTimeUtc(f);
        }
        return files;
    }

    /// <summary>
    /// Finds out where this Fluent Search keeps its settings: starts it, and the folder whose settings
    /// pages it creates or writes within half a minute is the one. Remembered in state.json; null if
    /// it wrote nothing (then all likely folders get the settings).
    /// </summary>
    public static async Task<string?> LearnSettingsDirAsync(Locations loc, string exe, CancellationToken ct)
    {
        if (AppState.Load().FluentSearchSettingsDir is { } known && Directory.Exists(known)) return known;
        if (!OperatingSystem.IsWindows()) return null;
        await StopAsync(ct);
        var before = Snapshot(loc, exe);
        Platform.AppLauncher.Start(exe);
        string? found = null;
        for (int i = 0; i < 30 && found == null; i++)
        {
            await Task.Delay(1000, ct);
            found = Snapshot(loc, exe)
                .Where(f => !before.TryGetValue(f.Key, out var was) || was != f.Value)
                .Select(f => Path.GetDirectoryName(f.Key))
                .FirstOrDefault();
        }
        await StopAsync(ct);
        Log.Info(found != null ? L.T("integrations.fluentSearch.learnt", ("path", found)) : L.T("integrations.fluentSearch.notLearnt"));
        if (found != null) AppState.Update(s => s.FluentSearchSettingsDir = found);
        return found;
    }

    /// <summary>The settings folder of the Fluent Search at <paramref name="exe"/>.</summary>
    public static string SettingsDir(Locations loc, string? exe)
    {
        if (exe != null && Path.GetDirectoryName(exe) is { } dir &&
            (dir.StartsWith(Paths.ToolsDir, StringComparison.OrdinalIgnoreCase) || Directory.Exists(Path.Combine(dir, "Blast")) ||
             File.Exists(Path.Combine(dir, "readme-portable.txt"))))
            return Path.Combine(dir, "Blast");
        return Path.Combine(loc.AppData, "Blast");
    }

    /// <summary>The settings pages WinMarchy carries a template for, e.g. "System.json".</summary>
    public static IReadOnlyList<string> TemplateFiles() => typeof(FluentSearchConfig).Assembly.GetManifestResourceNames()
        .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
        .Select(n => n[ResourcePrefix.Length..])
        .OrderBy(n => n, StringComparer.Ordinal)
        .ToList();

    /// <summary>A template page, with the signed-in user's profile folder where the template has "{USERPROFILE}".</summary>
    public static string Template(string file, string userProfile)
    {
        using var stream = typeof(FluentSearchConfig).Assembly.GetManifestResourceStream(ResourcePrefix + file)
            ?? throw new InvalidOperationException($"Fluent Search template {file} is not embedded");
        using var reader = new StreamReader(stream);
        // The paths sit in JSON strings, so their backslashes are escaped.
        return reader.ReadToEnd().Replace("{USERPROFILE}", userProfile.Replace("\\", "\\\\"));
    }

    /// <summary>System.json: the hotkey WinMarchy presses, and no start with Windows of its own.</summary>
    public static string PatchSystem(string json) => Patch(json, page =>
    {
        Set(page, "Search hotkey", new JsonObject { ["Name"] = "Keyboard shortcut to start searching", ["HotKeys"] = new JsonArray(Hotkey.Select(k => (JsonNode)k).ToArray()) });
        Set(page, "StartAtStartup", false);
        Set(page, "ImprovedStartup", false);
        Set(page, "StartMinimized", true);
    });

    /// <summary>
    /// Appearance.json, the "Omarchy" look: the theme's accent as manual accent colour, also on the
    /// window border; no transparency, fully opaque. Fluent Search keeps its own background colours.
    /// </summary>
    public static string PatchAppearance(string json, Palette palette) => Patch(json, page =>
    {
        Set(page, "Accent color", 0);
        Set(page, "ColorSetting", new JsonObject { ["ColorValue"] = Argb(palette.Accent) });
        Set(page, "Apply accent color on window borders", true);
        Set(page, "Apply accent color on background", false);
        Set(page, "Transparency effect", 0);
        Set(page, "WindowOpacity", 1);
    });

    /// <summary>"#7aa2f7" → 0xFF7AA2F7, the way Fluent Search stores a colour.</summary>
    public static long Argb(string hex) => 0xFF000000L | Convert.ToInt64(hex.TrimStart('#'), 16);

    /// <summary>
    /// Writes the settings (missing pages from the template, then WinMarchy's values) into the folder
    /// of the Fluent Search at <paramref name="exe"/>; every file goes into the restore point first.
    /// Afterwards it runs if <paramref name="start"/> is set.
    /// </summary>
    public static async Task ApplyAsync(IntegrationContext ctx, Palette palette, string? exe, bool start, CancellationToken ct)
    {
        if (exe == null || !File.Exists(exe))
        {
            ctx.Report(L.T("integrations.fluentSearch.notFound"));
            return;
        }
        await LearnSettingsDirAsync(ctx.Loc, exe, ct);
        await StopAsync(ct);
        var dirs = SettingsDirs(ctx.Loc, exe);
        foreach (var dir in dirs)
        {
            int written = 0;
            foreach (var file in TemplateFiles())
            {
                var path = Path.Combine(dir, file);
                var text = File.Exists(path) ? FileSafety.ReadShared(path) : Template(file, ctx.Loc.UserProfile);
                text = file switch
                {
                    "System.json" => PatchSystem(text),
                    "Appearance.json" => PatchAppearance(text, palette),
                    _ => text,
                };
                if (File.Exists(path) && FileSafety.ReadShared(path) == text) continue;
                ctx.WriteFile(path, text);
                written++;
            }
            Log.Info(L.T("integrations.fluentSearch.written", ("count", written), ("path", dir)));
        }
        ctx.Report(L.T("integrations.fluentSearch.done", ("path", string.Join(" · ", dirs))));
        if (start) Platform.AppLauncher.Start(exe);
    }

    /// <summary>Ends Fluent Search, so it neither holds its files nor writes its old values back.</summary>
    public static async Task StopAsync(CancellationToken ct)
    {
        foreach (var p in System.Diagnostics.Process.GetProcessesByName(Process))
        {
            using (p)
            {
                try
                {
                    p.Kill();
                    await p.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
                {
                    Log.Debug("Fluent Search: " + ex.Message);
                }
            }
        }
    }

    static string Patch(string json, Action<JsonArray> change)
    {
        var page = JsonNode.Parse(json.Length == 0 ? "[]" : json) as JsonArray ?? throw new WinMarchyException("jsonNotObject");
        change(page);
        return page.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>Sets one setting of a page, adding it if the page does not have it yet.</summary>
    static void Set(JsonArray page, string name, JsonNode? value)
    {
        foreach (var item in page.OfType<JsonObject>())
        {
            if (item["SettingName"]?.GetValue<string>() != name) continue;
            item["Value"] = value;
            return;
        }
        page.Add(new JsonObject { ["SettingName"] = name, ["Value"] = value });
    }
}

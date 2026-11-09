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
    /// The folder Fluent Search reads its settings from: "Blast\Settings" next to the portable build's
    /// exe (where Fluent Search 1.0.1.4 keeps them; seen on a real PC), "%AppData%\Blast\Settings"
    /// for the installed one.
    /// </summary>
    public static string SettingsDir(Locations loc, string? exe)
    {
        if (exe != null && Path.GetDirectoryName(exe) is { } dir &&
            (dir.StartsWith(Paths.ToolsDir, StringComparison.OrdinalIgnoreCase) || Directory.Exists(Path.Combine(dir, "Blast")) ||
             File.Exists(Path.Combine(dir, "readme-portable.txt"))))
            return Path.Combine(dir, "Blast", "Settings");
        return Path.Combine(loc.AppData, "Blast", "Settings");
    }

    /// <summary>
    /// Folders earlier WinMarchy versions wrote the settings to by mistake (a level too high, or the
    /// wrong AppData). Only the pages WinMarchy put there are taken away again.
    /// </summary>
    internal static IEnumerable<string> FormerGuesses(Locations loc, string exe) => new[]
    {
        Path.Combine(Path.GetDirectoryName(exe)!, "Blast"), Path.Combine(loc.AppData, "Blast"), Path.Combine(loc.LocalAppData, "Blast"),
    };

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
        await StopAsync(ct);
        // Pages earlier versions put a level too high or into the wrong AppData: only WinMarchy's own names.
        var names = TemplateFiles();
        foreach (var guess in FormerGuesses(ctx.Loc, exe).Where(Directory.Exists))
            foreach (var file in names.Select(n => Path.Combine(guess, n)).Where(File.Exists))
            {
                ctx.Backups.CaptureFile(ctx.Point, file);
                File.Delete(file);
            }
        var dir = SettingsDir(ctx.Loc, exe);
        int written = 0;
        foreach (var file in names)
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
        ctx.Report(L.T("integrations.fluentSearch.done", ("path", dir)));
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

using System.Text.Json.Nodes;
using WinMarchy.Core.Themes;

namespace WinMarchy.Core.Integrations;

/// <summary>
/// Fluent Search, the launcher behind the Win key. Its settings are one JSON file per settings page
/// (a list of { "SettingName", "Value" }) in "Blast\Settings": next to FluentSearch.exe for the
/// portable build, in %AppData% for the installed one. At the setup a page that is missing comes
/// from WinMarchy's template (the settings the user handed over, without search history, pinned
/// results, paths and user id), and WinMarchy sets what it relies on: the hotkey Alt+F24, no
/// autostart of its own (WinMarchy starts it), and the theme's accent colour. After that the
/// settings are the user's: updates leave them alone, a theme change only sets the accent colour.
/// Fluent Search is ended before its files change, so it does not write its old values back.
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
    /// The setup: every page WinMarchy has a template for and Fluent Search does not have yet comes
    /// from the template; then the hotkey, no autostart of its own and the theme's accent colour.
    /// Pages Fluent Search already has keep everything else. Every file goes into the restore point
    /// first. Afterwards it runs if <paramref name="start"/> is set.
    /// </summary>
    public static Task SetupAsync(IntegrationContext ctx, Palette palette, string? exe, bool start, CancellationToken ct) =>
        WriteAsync(ctx, palette, exe, start, TemplateFiles(), ct);

    /// <summary>
    /// A theme change: only the accent colour on the "Appearance" page changes; all other settings stay
    /// as the user left them. Afterwards Fluent Search runs if <paramref name="start"/> is set.
    /// </summary>
    public static Task ApplyThemeAsync(IntegrationContext ctx, Palette palette, string? exe, bool start, CancellationToken ct) =>
        WriteAsync(ctx, palette, exe, start, new[] { "Appearance.json" }, ct);

    static async Task WriteAsync(IntegrationContext ctx, Palette palette, string? exe, bool start, IReadOnlyList<string> pages, CancellationToken ct)
    {
        if (exe == null || !File.Exists(exe))
        {
            ctx.Report(L.T("integrations.fluentSearch.notFound"));
            return;
        }
        await StopAsync(ct);
        var dir = SettingsDir(ctx.Loc, exe);
        int written = 0;
        foreach (var file in pages)
        {
            var path = Path.Combine(dir, file);
            var text = File.Exists(path) ? FileSafety.ReadShared(path) : Template(file, ctx.Loc.UserProfile);
            text = file switch
            {
                "System.json" => PatchSystem(text),
                "Appearance.json" => PatchAppearance(text, palette),
                _ => text,
            };
            // Fluent Search keeps a "<page>.backup.json" next to every page and falls back to it; both
            // carry the same settings, or the old ones come back.
            var backup = Path.Combine(dir, Path.GetFileNameWithoutExtension(file) + ".backup.json");
            foreach (var target in new[] { path, backup })
            {
                if (File.Exists(target) && FileSafety.ReadShared(target) == text) continue;
                ctx.WriteFile(target, text);
                written++;
            }
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

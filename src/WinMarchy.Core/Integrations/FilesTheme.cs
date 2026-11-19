using System.Diagnostics;
using WinMarchy.Core.Themes;

namespace WinMarchy.Core.Integrations;

/// <summary>
/// Files (files.community) takes its background colours from its own settings JSON. It only changes
/// while Files is closed, because Files writes the file back when it exits. Windows Explorer and the
/// default file manager stay untouched.
/// </summary>
public static class FilesTheme
{
    static readonly string[] Keys =
    {
        "AppThemeBackgroundColor", "AppThemeAddressBarBackgroundColor", "AppThemeToolbarBackgroundColor",
        "AppThemeSidebarBackgroundColor", "AppThemeFileAreaBackgroundColor", "AppThemeInfoPaneBackgroundColor",
    };

    public static IEnumerable<string> Candidates(Locations loc, string? pinned)
    {
        if (!string.IsNullOrWhiteSpace(pinned)) yield return pinned;
        var packages = Path.Combine(loc.LocalAppData, "Packages");
        if (!Directory.Exists(packages)) yield break;
        foreach (var pkg in Directory.EnumerateDirectories(packages))
        {
            var name = Path.GetFileName(pkg);
            if (!name.StartsWith("Files", StringComparison.OrdinalIgnoreCase) && !name.Contains("49306atecsolution", StringComparison.OrdinalIgnoreCase))
                continue;
            yield return Path.Combine(pkg, "LocalState", "settings", "user_settings.json");
            yield return Path.Combine(pkg, "LocalState", "settings.json");
        }
    }

    public static bool LooksLikeFilesSettings(string text) =>
        text.Contains("AppTheme", StringComparison.Ordinal) || text.Contains("\"PreferencesSettings", StringComparison.Ordinal);

    public static string Patch(string original, string background)
    {
        var root = Jsonc.ParseObject(original);
        var argb = ColorMath.Argb(background);
        foreach (var k in Keys) root[k] = argb;
        return Json.WriteLike(root, original);
    }

    public static void Apply(IntegrationContext ctx, Palette palette)
    {
        var file = Candidates(ctx.Loc, ctx.Settings.FilesSettingsPath).FirstOrDefault(f => File.Exists(f) && LooksLikeFilesSettings(FileSafety.ReadShared(f)));
        if (file == null)
        {
            ctx.Report(L.T("integrations.files.notFound"));
            return;
        }
        if (Process.GetProcessesByName("Files").Length > 0)
        {
            ctx.Report(L.T("integrations.files.running"));
            return;
        }
        ctx.WriteFile(file, Patch(FileSafety.ReadShared(file), palette.Background));
        ctx.Report(L.T("integrations.files.done", ("path", file)));
    }
}

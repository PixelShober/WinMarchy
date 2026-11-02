using System.Text.Json.Nodes;

namespace WinMarchy.Core.Integrations;

/// <summary>
/// What WinMarchy versions before YASB left in Zebar: the widget pack in
/// %USERPROFILE%\.glzr\zebar\winmarchy and its entry in Zebar's startup list. WinMarchy no longer
/// installs Zebar; Settings removes these leftovers (with a restore point).
/// </summary>
public static class ZebarPack
{
    public const string PackName = "winmarchy";

    /// <summary>The pack is there (an older WinMarchy installed Zebar as its bar).</summary>
    public static bool IsInstalled(Locations loc) => File.Exists(Path.Combine(loc.ZebarPackDir, "zpack.json"));

    /// <summary>Takes WinMarchy's bar out of Zebar's startup list, keeping all other entries.</summary>
    public static string RemoveFromStartup(string original)
    {
        var root = string.IsNullOrWhiteSpace(original) ? new JsonObject() : Jsonc.ParseObject(original);
        if (root["startupConfigs"] is not JsonArray list) return original;
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i] is JsonObject o && (string?)o["pack"] == PackName) list.RemoveAt(i);
        return Json.WriteLike(root, original);
    }

    /// <summary>
    /// Takes the pack out of Zebar: its folder and its entry in Zebar's startup list. Both are in the
    /// restore point; the program itself is removed by the caller.
    /// </summary>
    public static void Uninstall(IntegrationContext ctx)
    {
        var dir = ctx.Loc.ZebarPackDir;
        if (Directory.Exists(dir))
        {
            ctx.Backups.CaptureDirectory(ctx.Point, dir);
            Directory.Delete(dir, true);
        }
        var settings = ctx.Loc.ZebarSettings;
        if (File.Exists(settings)) ctx.WriteFile(settings, RemoveFromStartup(FileSafety.ReadShared(settings)));
    }
}

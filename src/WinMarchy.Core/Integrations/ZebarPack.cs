using System.Text.Json.Nodes;
using WinMarchy.Core.Themes;

namespace WinMarchy.Core.Integrations;

/// <summary>
/// The WinMarchy bar as a Zebar widget pack in %USERPROFILE%\.glzr\zebar\winmarchy, registered as
/// startup widget. The Zebar client library is bundled in the pack, so the bar needs no CDN.
/// </summary>
public static class ZebarPack
{
    public const string PackName = "winmarchy";
    public const string Widget = "bar";
    public const int BarHeight = 26;

    static readonly string[] StaticFiles = { "bar.html", "bar.css", "bar.js", "zebar.js" };

    public static string ThemeCss(Palette p) =>
        $":root {{\n  --bg: {p.Background};\n  --fg: {p.Foreground};\n  --accent: {p.Accent};\n  --muted: {p.DarkForeground};\n  --warn: {ColorMath.Legible(p.Yellow, p.Background)};\n  --border: {p.InactiveBorder};\n}}\n";

    public static string ConfigJs(string winMarchyExe)
    {
        var config = new JsonObject
        {
            ["exe"] = winMarchyExe,
            ["locale"] = L.Culture.Name,
            ["clock"] = L.Language == "de" ? "cccc HH:mm" : "cccc h:mm a",
            ["text"] = new JsonObject
            {
                ["paused"] = L.T("bar.paused"),
                ["direction"] = L.T("bar.direction"),
                ["offline"] = L.T("bar.offline"),
            },
        };
        return "// Written by WinMarchy.\nexport default " + Json.Format(config) + ";\n";
    }

    public static string Manifest(string winMarchyExe)
    {
        var widget = new JsonObject
        {
            ["name"] = Widget,
            ["htmlPath"] = "./bar.html",
            ["zOrder"] = "normal",
            ["shownInTaskbar"] = false,
            ["focused"] = false,
            ["resizable"] = false,
            ["transparent"] = true,
            ["includeFiles"] = new JsonArray("*.html", "*.css", "*.js"),
            ["caching"] = new JsonObject { ["defaultDuration"] = 0, ["rules"] = new JsonArray() },
            ["privileges"] = new JsonObject
            {
                ["shellCommands"] = new JsonArray(new JsonObject
                {
                    ["program"] = winMarchyExe,
                    ["argsRegex"] = "^--action (do|send|app|open):[A-Za-z0-9 :+._/-]+$",
                }),
            },
            ["presets"] = new JsonArray(new JsonObject
            {
                ["name"] = "default",
                ["anchor"] = "top_left",
                ["offsetX"] = "0px",
                ["offsetY"] = "0px",
                ["width"] = "100%",
                ["height"] = BarHeight + "px",
                ["monitorSelection"] = new JsonObject { ["type"] = "all" },
                // GlazeWM leaves room through its top gap, as in its own sample config; no appbar reservation.
                ["dockToEdge"] = new JsonObject { ["enabled"] = false, ["edge"] = "top", ["windowMargin"] = "0px" },
            }),
        };
        var pack = new JsonObject
        {
            ["$schema"] = "https://github.com/glzr-io/zebar/raw/v3.0.0/resources/zpack-schema.json",
            ["name"] = PackName,
            ["version"] = "1.0.0",
            ["description"] = "Omarchy-style bar by WinMarchy",
            ["tags"] = new JsonArray("topbar", "glazewm"),
            ["widgets"] = new JsonArray(widget),
        };
        return Json.Format(pack) + "\n";
    }

    /// <summary>Adds the bar to Zebar's startup widgets (or removes it), keeping all other entries.</summary>
    public static string PatchSettings(string? original, bool enabled)
    {
        var root = string.IsNullOrWhiteSpace(original) ? new JsonObject() : Jsonc.ParseObject(original);
        var list = root["startupConfigs"] as JsonArray ?? new JsonArray();
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i] is JsonObject o && (string?)o["pack"] == PackName) list.RemoveAt(i);
        if (enabled) list.Insert(0, new JsonObject { ["pack"] = PackName, ["widget"] = Widget, ["preset"] = "default" });
        root["startupConfigs"] = list;
        return original == null ? Json.Format(root) + "\n" : Json.WriteLike(root, original);
    }

    public static void Install(IntegrationContext ctx, Palette palette, string winMarchyExe)
    {
        var dir = ctx.Loc.ZebarPackDir;
        ctx.Backups.CaptureDirectory(ctx.Point, dir);
        Directory.CreateDirectory(dir);
        foreach (var name in StaticFiles) FileSafety.WriteText(Path.Combine(dir, name), Resource(name));
        FileSafety.WriteText(Path.Combine(dir, "config.js"), ConfigJs(winMarchyExe));
        FileSafety.WriteText(Path.Combine(dir, "zpack.json"), Manifest(winMarchyExe));
        FileSafety.WriteText(Path.Combine(dir, "theme.css"), ThemeCss(palette));

        var settings = ctx.Loc.ZebarSettings;
        var current = File.Exists(settings) ? FileSafety.ReadShared(settings) : null;
        ctx.WriteFile(settings, PatchSettings(current, ctx.Settings.ShowBar));
        ctx.Report(L.T("integrations.zebar.done", ("path", dir)));
    }

    /// <summary>Theme change only: the bar reloads theme.css by itself.</summary>
    public static void ApplyTheme(IntegrationContext ctx, Palette palette)
    {
        var dir = ctx.Loc.ZebarPackDir;
        if (!File.Exists(Path.Combine(dir, "zpack.json"))) return;
        ctx.Backups.CaptureDirectory(ctx.Point, dir);
        FileSafety.WriteText(Path.Combine(dir, "theme.css"), ThemeCss(palette));
    }

    static string Resource(string name)
    {
        using var stream = typeof(ZebarPack).Assembly.GetManifestResourceStream("WinMarchy.Zebar." + name)
            ?? throw new InvalidOperationException("Zebar resource missing: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

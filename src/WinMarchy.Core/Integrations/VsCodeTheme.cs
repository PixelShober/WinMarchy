using System.Text.Json.Nodes;
using WinMarchy.Core.Themes;

namespace WinMarchy.Core.Integrations;

/// <summary>
/// VS Code gets the colour theme Omarchy names for the theme (vscode.json), as Omarchy does it.
/// If that extension is not installed and cannot be installed (no marketplace access in many
/// companies), the matching built-in light or dark theme plus a few colour overrides are used.
/// </summary>
public static class VsCodeTheme
{
    /// <summary>The overrides WinMarchy sets when the extension is missing. They are removed again later.</summary>
    static readonly string[] OverrideKeys =
    {
        "editor.background", "editor.foreground", "sideBar.background", "activityBar.background", "statusBar.background",
        "titleBar.activeBackground", "panel.background", "terminal.background", "terminal.foreground", "focusBorder",
        "list.activeSelectionBackground", "statusBar.foreground", "activityBar.foreground", "sideBar.foreground",
    };

    public static string Patch(string original, OmarchyTheme theme, bool extensionAvailable)
    {
        var root = Jsonc.ParseObject(original);
        var p = theme.Palette;
        var custom = root["workbench.colorCustomizations"] as JsonObject;

        if (extensionAvailable && theme.Vscode is { Name.Length: > 0 } vs)
        {
            root["workbench.colorTheme"] = vs.Name;
            // Take back only what WinMarchy itself put there.
            if (custom != null)
                foreach (var k in OverrideKeys) custom.Remove(k);
        }
        else
        {
            root["workbench.colorTheme"] = p.IsLight ? "Default Light Modern" : "Default Dark Modern";
            custom ??= new JsonObject();
            var bg = p.Background;
            var fg = p.Foreground;
            foreach (var k in new[] { "editor.background", "sideBar.background", "activityBar.background", "statusBar.background", "titleBar.activeBackground", "panel.background", "terminal.background" })
                custom[k] = bg;
            foreach (var k in new[] { "editor.foreground", "terminal.foreground", "statusBar.foreground", "activityBar.foreground", "sideBar.foreground" })
                custom[k] = fg;
            custom["focusBorder"] = p.Accent;
            custom["list.activeSelectionBackground"] = p.Selection;
            root["workbench.colorCustomizations"] = custom;
        }
        if (custom is { Count: 0 }) root.Remove("workbench.colorCustomizations");
        return Json.WriteLike(root, original);
    }

    public static string ExtensionsDir(Locations loc) => Path.Combine(loc.UserProfile, ".vscode", "extensions");

    public static bool IsInstalled(Locations loc, string extensionId)
    {
        var dir = ExtensionsDir(loc);
        return Directory.Exists(dir) &&
               Directory.EnumerateDirectories(dir).Any(d => Path.GetFileName(d).StartsWith(extensionId + "-", StringComparison.OrdinalIgnoreCase));
    }

    public static string? CodeCli(Locations loc)
    {
        var candidates = new[]
        {
            Path.Combine(loc.LocalAppData, "Programs", "Microsoft VS Code", "bin", "code.cmd"),
            Path.Combine(loc.ProgramFiles, "Microsoft VS Code", "bin", "code.cmd"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static async Task ApplyAsync(IntegrationContext ctx, OmarchyTheme theme, CancellationToken ct)
    {
        var files = ctx.Loc.VsCodeSettingsCandidates.Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            ctx.Report(L.T("integrations.vscode.notFound"));
            return;
        }

        bool available = false;
        if (theme.Vscode is { Extension.Length: > 0 } vs)
        {
            available = IsInstalled(ctx.Loc, vs.Extension);
            if (!available && CodeCli(ctx.Loc) is { } cli)
            {
                ctx.Report(L.T("integrations.vscode.installing", ("extension", vs.Extension)));
                try
                {
                    var r = await ProcessRunner.RunCmdAsync($"\"{cli}\" --install-extension {vs.Extension}", ct, timeoutSec: 120);
                    available = r.Ok && IsInstalled(ctx.Loc, vs.Extension);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Warn(ex.Message);
                }
                if (!available) ctx.Report(L.T("integrations.vscode.installFailed", ("extension", vs.Extension)));
            }
        }

        foreach (var file in files)
        {
            try
            {
                ctx.WriteFile(file, Patch(FileSafety.ReadShared(file), theme, available));
                ctx.Report(L.T("integrations.vscode.done", ("path", file)));
            }
            catch (Exception ex)
            {
                Log.Warn(L.T("integrations.vscode.failed", ("path", file), ("detail", ex.Message)));
            }
        }
    }
}

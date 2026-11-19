using System.Text.Json;
using System.Text.Json.Nodes;
using WinMarchy.Core.Backup;

namespace WinMarchy.Core.Integrations;

/// <summary>Turns WebP wallpapers into JPEG (WIC lives in the WPF app, not in the core).</summary>
public interface IImageConverter
{
    /// <returns>false when Windows has no decoder for the format.</returns>
    bool ToJpeg(string source, string target);
}

/// <summary>Everything an integration needs to change a program's settings safely.</summary>
public sealed class IntegrationContext
{
    public IntegrationContext(Locations loc, BackupStore backups, RestorePoint point, UserSettings settings, Action<string>? report = null)
    {
        Loc = loc;
        Backups = backups;
        Point = point;
        Settings = settings;
        Report = report ?? (_ => { });
    }

    public Locations Loc { get; }
    public BackupStore Backups { get; }
    public RestorePoint Point { get; }
    public UserSettings Settings { get; }
    public Action<string> Report { get; }
    public IUserRegistry Registry => Backups.Registry;
    public IImageConverter? Images { get; set; }
    public IWallpaperAccess? Wallpaper => Backups.Wallpaper;

    /// <summary>Captures the file, then writes it.</summary>
    public void WriteFile(string path, string text, bool bom = false)
    {
        Backups.CaptureFile(Point, path);
        FileSafety.WriteText(path, text, bom);
    }

    public void WriteRegistry(string key, string name, RegistryData data)
    {
        Backups.CaptureRegistry(Point, key, name);
        Registry.Write(key, name, data);
    }
}

/// <summary>JSON with comments and trailing commas (Windows Terminal, VS Code), read into an editable tree.</summary>
public static class Jsonc
{
    static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static JsonObject ParseObject(string text) =>
        JsonNode.Parse(text.Length == 0 ? "{}" : text, documentOptions: Options) as JsonObject
        ?? throw new WinMarchyException("jsonNotObject");

    /// <summary>The object at <paramref name="key"/>, created if missing or not an object.</summary>
    public static JsonObject Obj(JsonObject parent, string key)
    {
        if (parent[key] is JsonObject o) return o;
        var created = new JsonObject();
        parent[key] = created;
        return created;
    }
}

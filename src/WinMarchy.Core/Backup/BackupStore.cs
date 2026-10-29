using System.IO.Compression;
using System.Security.Cryptography;

namespace WinMarchy.Core.Backup;

/// <summary>What one item looked like before WinMarchy changed it.</summary>
public sealed class CapturedItem
{
    /// <summary>"file", "dir", "registry" or "wallpaper".</summary>
    public string Kind { get; set; } = "";

    /// <summary>File or folder path, "key|name" for registry values, "wallpaper".</summary>
    public string Target { get; set; } = "";

    public bool Existed { get; set; }

    /// <summary>Name of the copy below blobs/ (file, zipped folder, wallpaper image).</summary>
    public string? Blob { get; set; }

    public RegistryData? Registry { get; set; }

    /// <summary>Wallpaper path Windows had set.</summary>
    public string? OriginalPath { get; set; }

    public DateTime Captured { get; set; }
}

public sealed class RestorePoint
{
    public string Id { get; set; } = "";

    /// <summary>"setup", "theme", "hotkeys", "tiling", "tools", "restore" – or "original".</summary>
    public string Kind { get; set; } = "";

    public string Title { get; set; } = "";
    public DateTime Created { get; set; }
    public List<CapturedItem> Items { get; set; } = new();

    /// <summary>When this point was restored, if ever.</summary>
    public DateTime? Restored { get; set; }

    public bool IsOriginal => Kind == "original";
}

public sealed record RestoredItem(CapturedItem Item, bool Ok, string? Error);

/// <summary>Reads and writes the current wallpaper (SystemParametersInfo on Windows).</summary>
public interface IWallpaperAccess
{
    string? Current();
    void Set(string path);
}

/// <summary>
/// Restore points under C:\WinMarchy\Backups. Every change WinMarchy makes to a file, folder,
/// registry value or the wallpaper is captured beforehand into the restore point of that change.
/// The very first capture of each target also goes into "original", so everything can be put back
/// to the state before WinMarchy, no matter how many changes happened in between.
/// </summary>
public sealed class BackupStore
{
    public const string OriginalId = "original";
    const string PointFile = "restore-point.json";
    readonly object _sync = new();

    public BackupStore(string root, IUserRegistry registry, IWallpaperAccess? wallpaper)
    {
        Root = root;
        Registry = registry;
        Wallpaper = wallpaper;
    }

    public string Root { get; }
    public IUserRegistry Registry { get; }
    public IWallpaperAccess? Wallpaper { get; }

    string BlobDir => Path.Combine(Root, "blobs");

    /// <summary>Starts a new restore point. Captures are saved right away, so a crash loses nothing.</summary>
    public RestorePoint Begin(string kind, string title)
    {
        lock (_sync)
        {
            var id = $"{DateTime.Now:yyyyMMdd-HHmmss}-{kind}";
            for (int n = 2; Directory.Exists(Path.Combine(Root, id)); n++) id = $"{DateTime.Now:yyyyMMdd-HHmmss}-{kind}-{n}";
            var point = new RestorePoint { Id = id, Kind = kind, Title = title, Created = DateTime.Now };
            Save(point);
            return point;
        }
    }

    /// <summary>
    /// The newest point of this kind while it is younger than <paramref name="within"/>, else a new one.
    /// Cycling wallpapers would otherwise leave a restore point per key press.
    /// </summary>
    public RestorePoint Reuse(string kind, string title, TimeSpan within)
    {
        lock (_sync)
        {
            var last = List().FirstOrDefault(p => p.Kind == kind);
            if (last != null && last.Restored == null && DateTime.Now - last.Created < within) return last;
            return Begin(kind, title);
        }
    }

    public void CaptureFile(RestorePoint point, string path) => Capture(point, "file", Path.GetFullPath(path), () => CaptureFileState(path));

    /// <summary>A whole folder that WinMarchy owns (the Zebar pack): kept as a zip.</summary>
    public void CaptureDirectory(RestorePoint point, string path) => Capture(point, "dir", Path.GetFullPath(path), () => CaptureDirectoryState(path));

    public void CaptureRegistry(RestorePoint point, string key, string name) =>
        Capture(point, "registry", key + "|" + name, () =>
        {
            var data = Registry.Read(key, name);
            return new CapturedItem { Existed = data != null, Registry = data };
        });

    public void CaptureWallpaper(RestorePoint point) =>
        Capture(point, "wallpaper", "wallpaper", () =>
        {
            var current = Wallpaper?.Current();
            var item = new CapturedItem { OriginalPath = current, Existed = !string.IsNullOrEmpty(current) };
            // Windows overwrites its TranscodedWallpaper copy when a new picture is set, so keep the image itself.
            if (!string.IsNullOrEmpty(current) && File.Exists(current)) item.Blob = StoreBlob(File.ReadAllBytes(current), Path.GetExtension(current));
            return item;
        });

    void Capture(RestorePoint point, string kind, string target, Func<CapturedItem> capture)
    {
        lock (_sync)
        {
            if (point.Items.Any(i => i.Kind == kind && SameTarget(i.Target, target))) return;
            var item = capture();
            item.Kind = kind;
            item.Target = target;
            item.Captured = DateTime.Now;
            point.Items.Add(item);
            Save(point);

            // First time WinMarchy touches this target: that is the original state.
            var original = LoadOriginal() ?? new RestorePoint { Id = OriginalId, Kind = "original", Title = "original", Created = DateTime.Now };
            if (!original.Items.Any(i => i.Kind == kind && SameTarget(i.Target, target)))
            {
                original.Items.Add(item);
                Save(original);
            }
        }
    }

    static bool SameTarget(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    CapturedItem CaptureFileState(string path)
    {
        if (!File.Exists(path)) return new CapturedItem { Existed = false };
        return new CapturedItem { Existed = true, Blob = StoreBlob(File.ReadAllBytes(path), ".bin") };
    }

    CapturedItem CaptureDirectoryState(string path)
    {
        if (!Directory.Exists(path)) return new CapturedItem { Existed = false };
        var tmp = Path.Combine(Paths.Ensure(Root), $"dir-{Guid.NewGuid():N}.zip");
        try
        {
            ZipFile.CreateFromDirectory(path, tmp, CompressionLevel.Fastest, includeBaseDirectory: false);
            return new CapturedItem { Existed = true, Blob = StoreBlob(File.ReadAllBytes(tmp), ".zip") };
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    /// <summary>Content-addressed: the same file captured ten times is stored once.</summary>
    string StoreBlob(byte[] data, string extension)
    {
        var name = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant() + extension;
        var file = Path.Combine(Paths.Ensure(BlobDir), name);
        if (!File.Exists(file)) FileSafety.WriteBytes(file, data);
        return name;
    }

    // ------------------------------------------------------------------ listing

    public IReadOnlyList<RestorePoint> List()
    {
        lock (_sync)
        {
            if (!Directory.Exists(Root)) return Array.Empty<RestorePoint>();
            return Directory.GetDirectories(Root)
                .Where(d => Path.GetFileName(d) is not ("blobs" or OriginalId))
                .Select(d => Load(Path.Combine(d, PointFile)))
                .OfType<RestorePoint>()
                .Where(p => p.Items.Count > 0)
                .OrderByDescending(p => p.Created)
                .ToList();
        }
    }

    public RestorePoint? LoadOriginal()
    {
        lock (_sync) return Load(Path.Combine(Root, OriginalId, PointFile));
    }

    public RestorePoint? Get(string id) =>
        id == OriginalId ? LoadOriginal() : List().FirstOrDefault(p => p.Id == id);

    static RestorePoint? Load(string file)
    {
        try
        {
            return File.Exists(file) ? Json.Deserialize<RestorePoint>(File.ReadAllText(file)) : null;
        }
        catch (Exception ex)
        {
            Log.Warn($"Restore point unreadable: {file}: {ex.Message}");
            return null;
        }
    }

    void Save(RestorePoint point) =>
        FileSafety.WriteText(Path.Combine(Paths.Ensure(Path.Combine(Root, point.Id)), PointFile), Json.Serialize(point, indented: true));

    // ------------------------------------------------------------------ restoring

    /// <summary>
    /// Puts every item of the point back, newest capture first. Stopping and restarting the programs
    /// that keep these files open is the caller's job (see <c>RestoreService</c>).
    /// </summary>
    public IReadOnlyList<RestoredItem> Restore(RestorePoint point)
    {
        var results = new List<RestoredItem>();
        lock (_sync)
        {
            foreach (var item in Enumerable.Reverse(point.Items))
            {
                try
                {
                    RestoreItem(item);
                    results.Add(new RestoredItem(item, true, null));
                }
                catch (Exception ex)
                {
                    Log.Warn(L.T("backup.itemFailed", ("target", item.Target), ("detail", ex.Message)));
                    results.Add(new RestoredItem(item, false, ex.Message));
                }
            }

            if (point.IsOriginal)
            {
                // Back at the start: the next change records a fresh original state.
                var dir = Path.Combine(Root, OriginalId);
                var archived = Path.Combine(Root, $"{DateTime.Now:yyyyMMdd-HHmmss}-original-restored");
                if (Directory.Exists(dir)) Directory.Move(dir, archived);
                var moved = Load(Path.Combine(archived, PointFile));
                if (moved != null)
                {
                    moved.Id = Path.GetFileName(archived);
                    moved.Kind = "original-restored";
                    moved.Restored = DateTime.Now;
                    Save(moved);
                }
            }
            else
            {
                point.Restored = DateTime.Now;
                Save(point);
            }
        }
        return results;
    }

    void RestoreItem(CapturedItem item)
    {
        switch (item.Kind)
        {
            case "file":
                if (item.Existed) FileSafety.WriteBytes(item.Target, ReadBlob(item.Blob));
                else if (File.Exists(item.Target)) File.Delete(item.Target);
                break;

            case "dir":
                if (FileSafety.IsDangerousDirectory(item.Target)) throw new WinMarchyException("backupDangerous", ("path", item.Target));
                if (Directory.Exists(item.Target)) Directory.Delete(item.Target, recursive: true);
                if (item.Existed)
                {
                    var zip = BlobPath(item.Blob);
                    Directory.CreateDirectory(item.Target);
                    FileSafety.ExtractZip(zip, item.Target, CancellationToken.None);
                }
                break;

            case "registry":
                var (key, name) = SplitRegistry(item.Target);
                if (item.Existed && item.Registry != null) Registry.Write(key, name, item.Registry);
                else Registry.Delete(key, name);
                break;

            case "wallpaper":
                if (Wallpaper == null || !item.Existed) break;
                var path = item.OriginalPath;
                if (item.Blob != null)
                {
                    var copy = BlobPath(item.Blob);
                    bool originalIntact = path != null && File.Exists(path) &&
                                          Net.Sha256(File.ReadAllBytes(path)) + Path.GetExtension(path) == item.Blob;
                    if (!originalIntact) path = copy;
                }
                if (path != null) Wallpaper.Set(path);
                break;
        }
    }

    internal static (string Key, string Name) SplitRegistry(string target)
    {
        var bar = target.LastIndexOf('|');
        return (target[..bar], target[(bar + 1)..]);
    }

    string BlobPath(string? blob)
    {
        if (blob == null) throw new WinMarchyException("backupBlobMissing", ("blob", "?"));
        var file = Path.Combine(BlobDir, blob);
        if (!File.Exists(file)) throw new WinMarchyException("backupBlobMissing", ("blob", blob));
        return file;
    }

    byte[] ReadBlob(string? blob) => File.ReadAllBytes(BlobPath(blob));

    /// <summary>Deletes a restore point (never the original state). Blobs no other point needs are removed too.</summary>
    public void Delete(string id)
    {
        if (id == OriginalId) return;
        lock (_sync)
        {
            var dir = Path.Combine(Root, id);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            PruneBlobs();
        }
    }

    void PruneBlobs()
    {
        if (!Directory.Exists(BlobDir)) return;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in List().Append(LoadOriginal()).OfType<RestorePoint>())
            foreach (var i in p.Items)
                if (i.Blob != null) used.Add(i.Blob);
        foreach (var file in Directory.GetFiles(BlobDir))
            if (!used.Contains(Path.GetFileName(file))) File.Delete(file);
    }
}

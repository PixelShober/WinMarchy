using WinMarchy.Core.Integrations;

namespace WinMarchy.Core.Themes;

/// <summary>
/// Previews and wallpapers under C:\WinMarchy\Themes\&lt;theme&gt;. Downloaded from the pinned Omarchy
/// tag and checked against the SHA-256 in the manifest; files that do not match are thrown away.
/// </summary>
public static class ThemeAssets
{
    public static string Dir(string theme) => Path.Combine(Paths.ThemesDir, theme);

    public static string? PreviewFile(OmarchyTheme theme)
    {
        var file = Path.Combine(Dir(theme.Name), "preview.png");
        return File.Exists(file) ? file : null;
    }

    public static string BackgroundFile(OmarchyTheme theme, ThemeAsset asset) =>
        Path.Combine(Dir(theme.Name), asset.File.Replace('/', Path.DirectorySeparatorChar));

    public static IReadOnlyList<string> DownloadedBackgrounds(OmarchyTheme theme) =>
        theme.Backgrounds.Select(b => BackgroundFile(theme, b)).Where(File.Exists).ToList();

    public static bool HasAllBackgrounds(OmarchyTheme theme) => theme.Backgrounds.All(b => File.Exists(BackgroundFile(theme, b)));

    public static long MissingBytes(OmarchyTheme theme, bool backgrounds) =>
        (theme.Preview is { } p && !File.Exists(Path.Combine(Dir(theme.Name), p.File)) ? p.Size : 0) +
        (backgrounds ? theme.Backgrounds.Where(b => !File.Exists(BackgroundFile(theme, b))).Sum(b => b.Size) : 0);

    /// <summary>Downloads the preview and, if asked, the wallpapers that are not there yet.</summary>
    public static async Task DownloadAsync(HttpClient http, OmarchyTheme theme, bool backgrounds, Action<string>? report, CancellationToken ct)
    {
        var assets = new List<ThemeAsset>();
        if (theme.Preview != null) assets.Add(theme.Preview);
        if (backgrounds) assets.AddRange(theme.Backgrounds);
        foreach (var asset in assets)
        {
            var target = Path.Combine(Dir(theme.Name), asset.File.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(target) && new FileInfo(target).Length == asset.Size) continue;
            report?.Invoke(L.T("themes.downloading", ("file", asset.File), ("size", Net.Mb(asset.Size))));
            var url = ThemeCatalog.Manifest.BaseUrl + theme.Name + "/" + string.Join('/', asset.File.Split('/').Select(Uri.EscapeDataString));
            await Net.DownloadVerifiedAsync(http, url, target, asset.Sha256, null, ct);
        }
    }

    /// <summary>
    /// Imports a theme folder (Omarchy's themes/&lt;name&gt; layout: colors.toml, preview.png,
    /// backgrounds/) – for machines without access to GitHub.
    /// </summary>
    public static int ImportFolder(string source, Action<string>? report)
    {
        int count = 0;
        foreach (var theme in ThemeCatalog.All)
        {
            var dir = new[] { Path.Combine(source, theme.Name), Path.Combine(source, "themes", theme.Name) }.FirstOrDefault(Directory.Exists);
            if (dir == null) continue;
            var target = Dir(theme.Name);
            var preview = Path.Combine(dir, "preview.png");
            if (File.Exists(preview)) { Directory.CreateDirectory(target); File.Copy(preview, Path.Combine(target, "preview.png"), true); }
            var bg = Path.Combine(dir, "backgrounds");
            if (Directory.Exists(bg))
            {
                Directory.CreateDirectory(Path.Combine(target, "backgrounds"));
                foreach (var f in Directory.EnumerateFiles(bg))
                    File.Copy(f, Path.Combine(target, "backgrounds", Path.GetFileName(f)), true);
            }
            count++;
            report?.Invoke(L.T("themes.imported", ("theme", theme.Title)));
        }
        return count;
    }
}

/// <summary>Sets a theme wallpaper (WebP converted to JPEG first) and cycles through them.</summary>
public static class Wallpapers
{
    public const string ContentDeliveryKey = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    /// <summary>All 22 themes together, to say how much "load everything" costs.</summary>
    public static long AllMissingBytes() => ThemeCatalog.All.Sum(t => ThemeAssets.MissingBytes(t, backgrounds: true));

    public static int AllCount() => ThemeCatalog.All.Sum(t => t.Backgrounds.Count);

    public static string? Apply(IntegrationContext ctx, OmarchyTheme theme, int index)
    {
        var files = ThemeAssets.DownloadedBackgrounds(theme);
        if (files.Count == 0 || ctx.Wallpaper == null)
        {
            ctx.Report(L.T("integrations.wallpaper.none", ("theme", theme.Title)));
            return null;
        }
        var file = files[((index % files.Count) + files.Count) % files.Count];
        var usable = Usable(file, ctx.Images);
        if (usable == null)
        {
            ctx.Report(L.T("integrations.wallpaper.unsupported", ("file", Path.GetFileName(file))));
            return null;
        }
        ctx.Backups.CaptureWallpaper(ctx.Point);
        ctx.WriteRegistry(WindowsAppearance.DesktopKey, "WallpaperStyle", RegistryData.Str("10"));   // fill
        ctx.WriteRegistry(WindowsAppearance.DesktopKey, "TileWallpaper", RegistryData.Str("0"));
        ctx.Wallpaper.Set(usable);
        ctx.Report(L.T("integrations.wallpaper.done", ("file", Path.GetFileName(file))));
        if (ctx.Settings.ThemeTargets.LockScreen) ApplyLockScreen(ctx, usable);
        return file;
    }

    /// <summary>
    /// The same picture on the lock screen. The picture before and Windows' Spotlight switches go into
    /// the restore point first, so undo brings back exactly what was there.
    /// </summary>
    public static bool ApplyLockScreen(IntegrationContext ctx, string file)
    {
        if (ctx.Backups.LockScreen is not { } lockScreen) return false;
        try
        {
            ctx.Backups.CaptureRegistry(ctx.Point, ContentDeliveryKey, "RotatingLockScreenEnabled");
            ctx.Backups.CaptureRegistry(ctx.Point, ContentDeliveryKey, "RotatingLockScreenOverlayEnabled");
            ctx.Backups.CaptureLockScreen(ctx.Point);
            lockScreen.Set(Usable(file, ctx.Images) ?? file);
            ctx.Report(L.T("integrations.lockScreen.done", ("file", Path.GetFileName(file))));
            return true;
        }
        catch (Exception ex)
        {
            // Mostly a policy (Personalization\LockScreenImage or NoChangingLockScreen).
            ctx.Report(L.T("integrations.lockScreen.failed", ("detail", Net.Describe(ex))));
            Log.Warn(L.T("integrations.lockScreen.failed", ("detail", Net.Describe(ex))));
            return false;
        }
    }

    /// <summary>Windows sets JPEG, PNG and BMP reliably; WebP only with a codec, so it is converted once.</summary>
    static string? Usable(string file, IImageConverter? images)
    {
        if (!file.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) return file;
        var jpg = Path.ChangeExtension(file, ".jpg");
        if (File.Exists(jpg)) return jpg;
        return images != null && images.ToJpeg(file, jpg) ? jpg : null;
    }
}

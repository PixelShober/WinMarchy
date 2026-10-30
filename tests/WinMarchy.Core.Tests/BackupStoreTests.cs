using WinMarchy.Core.Backup;

namespace WinMarchy.Core.Tests;

public class BackupStoreTests
{
    sealed class FakeWallpaper : IWallpaperAccess
    {
        public string? Path { get; set; }
        public string? Current() => Path;
        public void Set(string path) => Path = path;
    }

    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    [Fact]
    public void A_changed_file_comes_back()
    {
        using var tmp = new TempDir();
        var store = new BackupStore(tmp.File("backups"), new MemoryUserRegistry(), null);
        var file = tmp.File("config.yaml");
        File.WriteAllText(file, "before");

        var point = store.Begin("theme", "Theme Nord");
        store.CaptureFile(point, file);
        File.WriteAllText(file, "after");

        var results = store.Restore(point);
        Assert.All(results, r => Assert.True(r.Ok));
        Assert.Equal("before", File.ReadAllText(file));
    }

    [Fact]
    public void A_file_that_did_not_exist_is_removed_again()
    {
        using var tmp = new TempDir();
        var store = new BackupStore(tmp.File("backups"), new MemoryUserRegistry(), null);
        var file = tmp.File("new.json");

        var point = store.Begin("setup", "Setup");
        store.CaptureFile(point, file);
        File.WriteAllText(file, "{}");

        store.Restore(point);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Registry_values_come_back_or_disappear()
    {
        using var tmp = new TempDir();
        var registry = new MemoryUserRegistry();
        registry.Write(Key, "AppsUseLightTheme", RegistryData.Dword(1));
        var store = new BackupStore(tmp.File("backups"), registry, null);

        var point = store.Begin("theme", "Theme");
        store.CaptureRegistry(point, Key, "AppsUseLightTheme");
        store.CaptureRegistry(point, Key, "WinMarchyNew");
        registry.Write(Key, "AppsUseLightTheme", RegistryData.Dword(0));
        registry.Write(Key, "WinMarchyNew", RegistryData.Str("x"));

        store.Restore(point);
        Assert.Equal(1, registry.Read(Key, "AppsUseLightTheme")?.Number);
        Assert.Null(registry.Read(Key, "WinMarchyNew"));
    }

    [Fact]
    public void The_original_state_keeps_the_first_capture_of_each_target()
    {
        using var tmp = new TempDir();
        var store = new BackupStore(tmp.File("backups"), new MemoryUserRegistry(), null);
        var file = tmp.File("settings.json");
        File.WriteAllText(file, "v1");

        var first = store.Begin("theme", "first");
        store.CaptureFile(first, file);
        File.WriteAllText(file, "v2");
        var second = store.Begin("theme", "second");
        store.CaptureFile(second, file);
        File.WriteAllText(file, "v3");

        store.Restore(second);
        Assert.Equal("v2", File.ReadAllText(file));

        var original = store.LoadOriginal();
        Assert.NotNull(original);
        Assert.Single(original!.Items);
        store.Restore(original);
        Assert.Equal("v1", File.ReadAllText(file));
        // Back at the start: the next change records a fresh original.
        Assert.Null(store.LoadOriginal());
    }

    [Fact]
    public void A_folder_comes_back_as_it_was()
    {
        using var tmp = new TempDir();
        var store = new BackupStore(tmp.File("backups"), new MemoryUserRegistry(), null);
        var dir = tmp.File("pack");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "bar.css"), "old");

        var point = store.Begin("setup", "Setup");
        store.CaptureDirectory(point, dir);
        File.WriteAllText(Path.Combine(dir, "bar.css"), "new");
        File.WriteAllText(Path.Combine(dir, "extra.js"), "x");

        store.Restore(point);
        Assert.Equal("old", File.ReadAllText(Path.Combine(dir, "bar.css")));
        Assert.False(File.Exists(Path.Combine(dir, "extra.js")));
    }

    [Fact]
    public void The_wallpaper_comes_back_even_when_the_original_file_changed()
    {
        using var tmp = new TempDir();
        var original = tmp.File("TranscodedWallpaper.jpg");
        File.WriteAllBytes(original, new byte[] { 1, 2, 3 });
        var wallpaper = new FakeWallpaper { Path = original };
        var store = new BackupStore(tmp.File("backups"), new MemoryUserRegistry(), wallpaper);

        var point = store.Begin("theme", "Theme");
        store.CaptureWallpaper(point);
        File.WriteAllBytes(original, new byte[] { 9, 9 });   // Windows overwrote its copy
        wallpaper.Path = tmp.File("other.jpg");

        store.Restore(point);
        Assert.NotNull(wallpaper.Path);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(wallpaper.Path!));
    }

    [Fact]
    public void Deleting_a_point_keeps_blobs_other_points_need()
    {
        using var tmp = new TempDir();
        var store = new BackupStore(tmp.File("backups"), new MemoryUserRegistry(), null);
        var file = tmp.File("a.txt");
        File.WriteAllText(file, "same");
        var p1 = store.Begin("theme", "one");
        store.CaptureFile(p1, file);
        var p2 = store.Begin("theme", "two");
        store.CaptureFile(p2, file);

        store.Delete(p1.Id);
        Assert.DoesNotContain(store.List(), p => p.Id == p1.Id);
        File.WriteAllText(file, "changed");
        Assert.All(store.Restore(p2), r => Assert.True(r.Ok));
        Assert.Equal("same", File.ReadAllText(file));
    }

    [Fact]
    public void Wallpaper_cycling_reuses_one_point()
    {
        using var tmp = new TempDir();
        var store = new BackupStore(tmp.File("backups"), new MemoryUserRegistry(), new FakeWallpaper());
        var file = tmp.File("x");
        var a = store.Reuse("wallpaper", "Wallpaper", TimeSpan.FromHours(1));
        store.CaptureFile(a, file);
        var b = store.Reuse("wallpaper", "Wallpaper", TimeSpan.FromHours(1));
        Assert.Equal(a.Id, b.Id);
    }
}

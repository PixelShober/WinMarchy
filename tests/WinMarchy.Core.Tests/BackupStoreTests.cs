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
    public void A_pause_shows_a_plain_windows_and_puts_winmarchys_look_back()
    {
        using var tmp = new TempDir();
        var registry = new MemoryUserRegistry();
        var wallpaper = new FakeWallpaper();
        var store = new BackupStore(tmp.File("backups"), registry, wallpaper);

        // WinMarchy's theme: dark, its own wallpaper; an earlier tool had already set auto colouring.
        registry.Write(Key, "AppsUseLightTheme", RegistryData.Dword(0));
        registry.Write(Integrations.WindowsAppearance.DesktopKey, "AutoColorization", RegistryData.Str("1"));
        var own = tmp.File("tokyo.jpg");
        File.WriteAllBytes(own, new byte[] { 4 });
        wallpaper.Set(own);
        var point = store.Begin("theme", "Theme");

        var windows = tmp.File("windows");
        var plain = Integrations.WindowsDefaults.Parse(AeroTheme, windows, windows11: true);
        List<Backup.CapturedItem>? kept = null;
        var now = store.ShowFor(Integrations.WindowsDefaults.Items(plain), items => kept = items.ToList());
        Assert.Equal(1, registry.Read(Key, "AppsUseLightTheme")!.Number);
        Assert.Equal(1, registry.Read(Key, "SystemUsesLightTheme")!.Number);
        Assert.Equal("0", registry.Read(Integrations.WindowsAppearance.DesktopKey, "AutoColorization")!.Text);
        Assert.Equal(windows + @"\web\wallpaper\Windows\img0.jpg", wallpaper.Current());
        // The state until now, handed over before anything changed.
        Assert.Equal(7, now.Count);
        Assert.Equal(now, kept);

        store.PutBack(now);
        Assert.Equal(0, registry.Read(Key, "AppsUseLightTheme")!.Number);
        // A value that was not there before the pause is gone again.
        Assert.Null(registry.Read(Key, "SystemUsesLightTheme"));
        Assert.Equal("1", registry.Read(Integrations.WindowsAppearance.DesktopKey, "AutoColorization")!.Text);
        Assert.Equal(own, wallpaper.Current());
        // Restore points are untouched.
        Assert.Empty(store.Get(point.Id)?.Items ?? new List<Backup.CapturedItem>());
    }

    const string AeroTheme = """
        ; Copyright © Microsoft Corp.
        [Theme]
        DisplayName=@%SystemRoot%\System32\themeui.dll,-2060

        [Control Panel\Desktop]
        Wallpaper=%SystemRoot%\web\wallpaper\Windows\img0.jpg
        TileWallpaper=0

        [VisualStyles]
        Path=%ResourceDir%\Themes\Aero\Aero.msstyles
        ColorStyle=NormalColor
        AutoColorization=0
        ColorizationColor=0XC40078D4
        SystemMode=Light
        AppMode=Light
        """;

    [Fact]
    public void The_pause_keeps_the_users_own_light_or_dark_mode()
    {
        using var tmp = new TempDir();
        var registry = new MemoryUserRegistry();
        var store = new BackupStore(tmp.File("backups"), registry, new FakeWallpaper());
        // Before WinMarchy: dark apps; the taskbar's mode was never set. WinMarchy's theme then made both light.
        registry.Write(Key, "AppsUseLightTheme", RegistryData.Dword(0));
        var point = store.Begin("theme", "Theme");
        store.CaptureRegistry(point, Key, "AppsUseLightTheme");
        store.CaptureRegistry(point, Key, "SystemUsesLightTheme");
        registry.Write(Key, "AppsUseLightTheme", RegistryData.Dword(1));
        registry.Write(Key, "SystemUsesLightTheme", RegistryData.Dword(1));

        var look = Integrations.WindowsDefaults.Parse(AeroTheme, tmp.File("windows"), windows11: true);
        store.ShowFor(Integrations.WindowsDefaults.ForPause(look, store.LoadOriginal()));
        Assert.Equal(0, registry.Read(Key, "AppsUseLightTheme")!.Number);
        Assert.Null(registry.Read(Key, "SystemUsesLightTheme"));
        // Windows' blue as accent, whatever the theme had.
        Assert.Equal(Themes.ColorMath.ToAbgr("#0078D4"), registry.Read(Integrations.WindowsAppearance.DwmKey, "AccentColor")!.AsInt);
    }

    [Fact]
    public void The_plain_look_comes_from_windows_default_theme()
    {
        var look = Integrations.WindowsDefaults.Parse(AeroTheme.Replace("0XC40078D4", "0XC4005FB8").Replace("SystemMode=Light", "SystemMode=Dark"), @"C:\Windows", windows11: true);
        Assert.True(look.AppsLight);
        Assert.False(look.SystemLight);
        Assert.Equal("#005FB8", look.Accent);
        Assert.False(look.AutoColorization);
        Assert.Equal(@"C:\Windows\web\wallpaper\Windows\img0.jpg", look.Wallpaper);

        // Without the theme file: Windows 11 is light all over, Windows 10 has a dark taskbar; Windows' blue.
        var eleven = Integrations.WindowsDefaults.Parse(null, @"C:\Windows", windows11: true);
        Assert.True(eleven.AppsLight && eleven.SystemLight);
        Assert.Equal("#0078D4", eleven.Accent);
        var ten = Integrations.WindowsDefaults.Parse(null, @"C:\Windows", windows11: false);
        Assert.True(ten.AppsLight);
        Assert.False(ten.SystemLight);
        Assert.Equal(Path.Combine(@"C:\Windows", "Web", "Wallpaper", "Windows", "img0.jpg"), ten.Wallpaper);
    }

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

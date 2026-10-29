using WinMarchy.Core.Backup;
using WinMarchy.Core.Integrations;
using WinMarchy.Core.Platform;
using WinMarchy.Core.Tools;

namespace WinMarchy.Core.Tests;

[Collection("Language")]
public class FilesDefaultTests
{
    const string Verb = @"Software\Classes\Directory\shell\openinfiles";

    static (Locations Loc, string Exe) FakeFiles(TempDir tmp)
    {
        var local = tmp.File("local");
        var exe = Path.Combine(local, "Microsoft", "WindowsApps", "files.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, "");
        return (new Locations(tmp.File("home"), tmp.File("roaming"), local, tmp.File("docs"), tmp.File("startup"), tmp.File("pf")), exe);
    }

    [Fact]
    public void Files_becomes_the_default_and_undo_leaves_no_keys_behind()
    {
        using var tmp = new TempDir();
        var (loc, exe) = FakeFiles(tmp);
        var registry = new MemoryUserRegistry();
        var store = new BackupStore(tmp.File("backups"), registry, null);
        var point = store.Begin("files-default", "Files");

        FilesDefault.Apply(new IntegrationContext(loc, store, point, new UserSettings()), on: true);
        Assert.True(FilesDefault.IsOn(registry));
        Assert.Equal(exe, FilesDefault.Target(registry));
        Assert.Equal($"\"{exe}\" \"%1\"", registry.Read(Verb + @"\command", "")!.Text);
        Assert.Equal($"\"{exe}\"", registry.Read(FilesDefault.WinEKey + @"\shell\opennewwindow\command", "")!.Text);

        Assert.All(store.Restore(point), r => Assert.True(r.Ok));
        Assert.False(FilesDefault.IsOn(registry));
        Assert.False(registry.KeyExists(Verb));
        Assert.False(registry.KeyExists(FilesDefault.WinEKey));
    }

    [Fact]
    public void Switching_off_removes_only_what_points_to_Files()
    {
        using var tmp = new TempDir();
        var (loc, _) = FakeFiles(tmp);
        var registry = new MemoryUserRegistry();
        var store = new BackupStore(tmp.File("backups"), registry, null);
        FilesDefault.Apply(new IntegrationContext(loc, store, store.Begin("files-default", "on"), new UserSettings()), on: true);
        // Some other tool owns Win+E on this PC.
        registry.Write(FilesDefault.WinEKey + @"\shell\opennewwindow\command", "", RegistryData.Str(@"""C:\Tools\other.exe"""));

        FilesDefault.Apply(new IntegrationContext(loc, store, store.Begin("files-default", "off"), new UserSettings()), on: false);
        Assert.False(FilesDefault.IsOn(registry));
        Assert.False(registry.KeyExists(Verb));
        Assert.Equal(@"""C:\Tools\other.exe""", registry.Read(FilesDefault.WinEKey + @"\shell\opennewwindow\command", "")!.Text);
    }

    [Fact]
    public void Without_Files_nothing_changes()
    {
        using var tmp = new TempDir();
        var loc = new Locations(tmp.File("home"), tmp.File("roaming"), tmp.File("local"), tmp.File("docs"), tmp.File("startup"), tmp.File("pf"));
        var registry = new MemoryUserRegistry();
        var store = new BackupStore(tmp.File("backups"), registry, null);
        Assert.Throws<WinMarchyException>(() =>
            FilesDefault.Apply(new IntegrationContext(loc, store, store.Begin("files-default", "on"), new UserSettings()), on: true));
        Assert.False(registry.KeyExists(@"Software\Classes\Directory"));
    }
}

[Collection("Language")]
public class PolicyCheckTests
{
    [Fact]
    public void Finds_the_policies_that_are_set_and_skips_the_rest()
    {
        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            [@"HKCU|Software\Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop|NoChangingWallPaper"] = 1,
            [@"HKLM|Software\Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop|NoChangingWallPaper"] = 0,
            [@"HKLM|SOFTWARE\Policies\Microsoft\Windows\Personalization|LockScreenImage"] = @"C:\corp\lock.jpg",
            [@"HKCU|Software\Microsoft\Windows Script Host\Settings|Enabled"] = 0,
            [@"HKCU|Software\Microsoft\Windows\CurrentVersion\Internet Settings|ProxyServer"] = "proxy.corp:8080",
            [@"HKCU|Software\Microsoft\Windows\CurrentVersion\Internet Settings|ProxyEnable"] = 0,
            [@"HKLM|SOFTWARE\Policies\Microsoft\Windows\SrpV2|"] = "",
        };
        var found = PolicyCheck.Evaluate((hive, key, name) => values.TryGetValue($"{hive}|{key}|{name}", out var v) ? v : null);
        var ids = found.Select(f => f.Id).ToList();

        Assert.Equal(new[] { "wallpaperLocked", "lockScreen", "appLocker", "scriptHost" }, ids);
        Assert.Contains(found, f => f.Id == "lockScreen" && f.Value == @"C:\corp\lock.jpg" && f.Location.StartsWith(@"HKLM\SOFTWARE\Policies"));
        Assert.All(found, f => Assert.DoesNotContain("policies.effects", f.Effect));
    }

    [Fact]
    public void A_proxy_counts_only_while_it_is_switched_on()
    {
        var values = new Dictionary<string, object>
        {
            [@"HKCU|Software\Microsoft\Windows\CurrentVersion\Internet Settings|ProxyServer"] = "proxy.corp:8080",
            [@"HKCU|Software\Microsoft\Windows\CurrentVersion\Internet Settings|ProxyEnable"] = 1,
        };
        var found = PolicyCheck.Evaluate((hive, key, name) => values.TryGetValue($"{hive}|{key}|{name}", out var v) ? v : null);
        Assert.Equal("proxy.corp:8080", Assert.Single(found).Value);
    }
}

public class ExtrasTests
{
    [Fact]
    public void PowerShell_Herdr_and_Pi_are_the_optional_extras() =>
        Assert.Equal(new[] { "pwsh", "herdr", "pi" }, ToolCatalog.Extras.Select(t => t.Id));

    [Fact]
    public void Every_extra_can_be_installed_from_its_vendor() =>
        Assert.All(ToolCatalog.Extras, t => Assert.True(t.Kind != InstallKind.None && t.Repo != null && t.Asset != null, t.Id));
}

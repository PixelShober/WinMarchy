namespace WinMarchy.Core.Integrations;

/// <summary>
/// Files as the default file manager, the way Files' own setting did it before it shipped a launcher:
/// folders and drives get an "Open in Files" verb as their default, and Win+E opens Files. Only real
/// folders are affected; This PC, the recycle bin and the control panel stay with Explorer. All of it
/// lives in HKEY_CURRENT_USER and goes into the restore point first.
/// </summary>
public static class FilesDefault
{
    public const string Verb = "openinfiles";
    static readonly string[] Classes = { @"Software\Classes\Directory", @"Software\Classes\Drive" };

    /// <summary>"This PC", which Win+E opens.</summary>
    public const string WinEKey = @"Software\Classes\CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}";

    /// <summary>Files' command-line alias (Store and sideload versions install one each).</summary>
    public static string? Exe(Locations loc)
    {
        var apps = Path.Combine(loc.LocalAppData, "Microsoft", "WindowsApps");
        return new[] { "files-stable.exe", "files.exe", "files-preview.exe" }.Select(n => Path.Combine(apps, n)).FirstOrDefault(File.Exists);
    }

    public static bool IsOn(IUserRegistry registry) =>
        string.Equals(registry.Read(Classes[0] + @"\shell", "")?.Text, Verb, StringComparison.OrdinalIgnoreCase);

    /// <summary>The program the verb starts, for the self-check at startup.</summary>
    public static string? Target(IUserRegistry registry)
    {
        var command = registry.Read(Classes[0] + @"\shell\" + Verb + @"\command", "")?.Text;
        if (command == null || !command.StartsWith('"')) return null;
        var end = command.IndexOf('"', 1);
        return end > 1 ? command[1..end] : null;
    }

    public static void Apply(IntegrationContext ctx, bool on)
    {
        if (on)
        {
            var exe = Exe(ctx.Loc) ?? throw new WinMarchyException("filesMissing");
            foreach (var cls in Classes)
            {
                var verb = cls + @"\shell\" + Verb;
                ctx.Backups.CaptureRegistryKey(ctx.Point, verb);
                ctx.WriteRegistry(cls + @"\shell", "", RegistryData.Str(Verb));
                ctx.WriteRegistry(verb, "", RegistryData.Str(L.T("integrations.filesDefault.verb")));
                ctx.WriteRegistry(verb + @"\command", "", RegistryData.Str($"\"{exe}\" \"%1\""));
            }
            ctx.Backups.CaptureRegistryKey(ctx.Point, WinEKey);
            ctx.WriteRegistry(WinEKey + @"\shell\opennewwindow\command", "", RegistryData.Str($"\"{exe}\""));
            ctx.WriteRegistry(WinEKey + @"\shell\opennewwindow\command", "DelegateExecute", RegistryData.Str(""));
            ctx.Report(L.T("integrations.filesDefault.on"));
            return;
        }

        foreach (var cls in Classes)
        {
            if (string.Equals(ctx.Registry.Read(cls + @"\shell", "")?.Text, Verb, StringComparison.OrdinalIgnoreCase))
            {
                ctx.Backups.CaptureRegistry(ctx.Point, cls + @"\shell", "");
                ctx.Registry.Delete(cls + @"\shell", "");
            }
            var verb = cls + @"\shell\" + Verb;
            if (ctx.Registry.KeyExists(verb))
            {
                ctx.Backups.CaptureRegistry(ctx.Point, verb, "");
                ctx.Backups.CaptureRegistry(ctx.Point, verb + @"\command", "");
                ctx.Registry.DeleteKey(verb);
            }
        }
        // Win+E only if it is ours: some other tool may have its own entry there.
        var winE = ctx.Registry.Read(WinEKey + @"\shell\opennewwindow\command", "")?.Text ?? "";
        if (winE.Contains(@"\WindowsApps\files", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Backups.CaptureRegistry(ctx.Point, WinEKey + @"\shell\opennewwindow\command", "");
            ctx.Backups.CaptureRegistry(ctx.Point, WinEKey + @"\shell\opennewwindow\command", "DelegateExecute");
            ctx.Registry.DeleteKey(WinEKey);
        }
        ctx.Report(L.T("integrations.filesDefault.off"));
    }
}

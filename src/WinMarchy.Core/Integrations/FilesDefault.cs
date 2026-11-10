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

    /// <summary>
    /// The pause: folders and Win+E open in Explorer again, without a restore point. The "Open in
    /// Files" verb stays, so <see cref="Resume"/> finds Files' path in it. Win+E only if the entry is ours.
    /// </summary>
    public static void Pause(IUserRegistry registry)
    {
        foreach (var cls in Classes)
            if (string.Equals(registry.Read(cls + @"\shell", "")?.Text, Verb, StringComparison.OrdinalIgnoreCase))
                registry.Delete(cls + @"\shell", "");
        if (OursOnWinE(registry)) registry.DeleteKey(WinEKey);
    }

    /// <summary>The end of a pause: Files opens folders and Win+E again, as <see cref="Apply"/> set it up.</summary>
    public static void Resume(IUserRegistry registry)
    {
        if (Target(registry) is not { } exe) return;
        foreach (var cls in Classes)
            if (registry.KeyExists(cls + @"\shell\" + Verb)) registry.Write(cls + @"\shell", "", RegistryData.Str(Verb));
        // Another tool's Win+E entry stayed during the pause; it is left as it is.
        if (registry.KeyExists(WinEKey + @"\shell\opennewwindow\command")) return;
        registry.Write(WinEKey + @"\shell\opennewwindow\command", "", RegistryData.Str($"\"{exe}\""));
        registry.Write(WinEKey + @"\shell\opennewwindow\command", "DelegateExecute", RegistryData.Str(""));
    }

    static bool OursOnWinE(IUserRegistry registry) =>
        (registry.Read(WinEKey + @"\shell\opennewwindow\command", "")?.Text ?? "").Replace('/', '\\').Contains(@"\WindowsApps\files", StringComparison.OrdinalIgnoreCase);

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
        if (OursOnWinE(ctx.Registry))
        {
            ctx.Backups.CaptureRegistry(ctx.Point, WinEKey + @"\shell\opennewwindow\command", "");
            ctx.Backups.CaptureRegistry(ctx.Point, WinEKey + @"\shell\opennewwindow\command", "DelegateExecute");
            ctx.Registry.DeleteKey(WinEKey);
        }
        ctx.Report(L.T("integrations.filesDefault.off"));
    }
}

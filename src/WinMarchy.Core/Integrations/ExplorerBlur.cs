using System.Text;
using WinMarchy.Core.Themes;

namespace WinMarchy.Core.Integrations;

/// <summary>
/// ExplorerBlurMica for the signed-in user only. Registered with regsvr32 (as administrator), its DLL
/// writes two entries for all users; WinMarchy writes the same two under HKEY_CURRENT_USER, which
/// Explorer reads as well, so no admin rights are needed and both are in the restore point. Its
/// config.ini next to the DLL carries the theme's background as the tint. Explorer windows opened
/// afterwards show the effect; the DLL stays loaded in Explorer until the next sign-in.
/// </summary>
public static class ExplorerBlur
{
    /// <summary>The DLL's class id (its folder extension), from ExplorerBlurMica's module.h.</summary>
    public const string Clsid = "{887D3A6A-502E-4AF5-9CE6-D515E12AFE89}";

    const string ClassKey = @"Software\Classes\CLSID\" + Clsid;
    const string ServerKey = ClassKey + @"\InProcServer32";
    const string ExtensionKey = @"Software\Classes\Drive\shellex\FolderExtensions\" + Clsid;

    /// <summary>The DLL Explorer loads for this user, if WinMarchy (or anyone) registered one here.</summary>
    public static string? RegisteredDll(IUserRegistry registry) => registry.Read(ServerKey, "")?.Text;

    public static bool IsOn(IUserRegistry registry) => RegisteredDll(registry) != null && registry.KeyExists(ExtensionKey);

    public static void Enable(IntegrationContext ctx, string dll, Palette palette)
    {
        if (!File.Exists(dll)) throw new WinMarchyException("toolExeMissing", ("tool", "ExplorerBlurMica"), ("exe", Path.GetFileName(dll)));
        WriteConfig(ctx, dll, palette);
        ctx.Backups.CaptureRegistryKey(ctx.Point, ClassKey);
        ctx.Backups.CaptureRegistryKey(ctx.Point, ExtensionKey);
        ctx.WriteRegistry(ServerKey, "", RegistryData.Str(dll));
        ctx.WriteRegistry(ServerKey, "ThreadingModel", RegistryData.Str("Apartment"));
        // All drive types, as the DLL's own registration does.
        ctx.WriteRegistry(ExtensionKey, "DriveMask", RegistryData.Dword(0xff));
        ctx.Report(L.T("integrations.explorerBlur.on"));
    }

    public static void Disable(IntegrationContext ctx)
    {
        foreach (var (key, names) in new[] { (ServerKey, new[] { "", "ThreadingModel" }), (ExtensionKey, new[] { "DriveMask" }) })
        {
            if (!ctx.Registry.KeyExists(key)) continue;
            foreach (var name in names) ctx.Backups.CaptureRegistry(ctx.Point, key, name);
        }
        if (ctx.Registry.KeyExists(ClassKey)) ctx.Registry.DeleteKey(ClassKey);
        if (ctx.Registry.KeyExists(ExtensionKey)) ctx.Registry.DeleteKey(ExtensionKey);
        ctx.Report(L.T("integrations.explorerBlur.off"));
    }

    /// <summary>Theme change: the new tint, for Explorer windows opened from now on.</summary>
    public static void ApplyTheme(IntegrationContext ctx, Palette palette)
    {
        if (!IsOn(ctx.Registry) || RegisteredDll(ctx.Registry) is not { } dll || !File.Exists(dll)) return;
        WriteConfig(ctx, dll, palette);
    }

    static void WriteConfig(IntegrationContext ctx, string dll, Palette palette) =>
        ctx.WriteFile(Path.Combine(Path.GetDirectoryName(dll)!, "config.ini"), ConfigIni(palette));

    /// <summary>
    /// Acrylic (it works on Windows 10 and 11) tinted with the theme's background; the address bar,
    /// scroll bars and Windows 11's toolbar let it show through. Both colour sections are the same:
    /// the theme also sets Windows to light or dark.
    /// </summary>
    public static string ConfigIni(Palette p)
    {
        var (r, g, b) = ColorMath.Rgb(p.Background);
        var alpha = p.IsLight ? 200 : 170;
        var sb = new StringBuilder();
        sb.Append("[config]\r\neffect=1\r\nclearAddress=true\r\nclearBarBg=true\r\nclearWinUIBg=true\r\nshowLine=true\r\n");
        foreach (var section in new[] { "light", "dark" })
            sb.Append($"[{section}]\r\nr={r}\r\ng={g}\r\nb={b}\r\na={alpha}\r\n");
        return sb.ToString();
    }
}

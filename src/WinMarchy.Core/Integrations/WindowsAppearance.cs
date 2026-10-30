using WinMarchy.Core.Themes;

namespace WinMarchy.Core.Integrations;

/// <summary>
/// Windows itself: light or dark mode as the theme is, and its accent colour. Only per-user
/// personalisation (HKCU) that Windows' own settings page writes as well – no shell patching.
/// Title bars keep the user's choice of colouring.
/// </summary>
public static class WindowsAppearance
{
    public const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    public const string DwmKey = @"Software\Microsoft\Windows\DWM";
    public const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    public const string DesktopKey = @"Control Panel\Desktop";

    public static void Apply(IntegrationContext ctx, Palette palette)
    {
        int light = palette.IsLight ? 1 : 0;
        ctx.WriteRegistry(PersonalizeKey, "AppsUseLightTheme", RegistryData.Dword(light));
        ctx.WriteRegistry(PersonalizeKey, "SystemUsesLightTheme", RegistryData.Dword(light));

        var accent = palette.Accent;
        ctx.WriteRegistry(DwmKey, "AccentColor", RegistryData.Dword(ColorMath.ToAbgr(accent)));
        ctx.WriteRegistry(DwmKey, "ColorizationColor", RegistryData.Dword(ColorMath.ToArgbInt(accent, 0xC4)));
        ctx.WriteRegistry(AccentKey, "AccentColorMenu", RegistryData.Dword(ColorMath.ToAbgr(accent)));
        // Otherwise Windows picks the accent from the wallpaper and overrides ours on the next change.
        ctx.WriteRegistry(DesktopKey, "AutoColorization", RegistryData.Str("0"));

        Win32.BroadcastSettingChange("ImmersiveColorSet");
        ctx.Report(L.T("integrations.windows.done", ("mode", palette.IsLight ? L.T("common.light") : L.T("common.dark"))));
    }
}

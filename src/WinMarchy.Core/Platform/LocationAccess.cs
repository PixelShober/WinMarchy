using Microsoft.Win32;

namespace WinMarchy.Core.Platform;

/// <summary>
/// Whether Windows lets desktop apps see the location. Windows 11 hands the list of Wi-Fi networks
/// only to apps that may; without it YASB's Wi-Fi menu shows nothing but an error.
/// </summary>
public static class LocationAccess
{
    const string Store = @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location";

    public static bool DesktopAppsAllowed()
    {
        if (!OperatingSystem.IsWindows()) return true;
        try
        {
            // Group policy first: location off, or apps kept from it.
            if (Read(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation") is 1) return false;
            if (Read(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsAccessLocation") is 2) return false;
            // The switch for the whole device, the user's own, and the one for desktop apps (Settings → Privacy → Location).
            return Read(Registry.LocalMachine, Store, "Value") as string != "Deny" &&
                   Read(Registry.CurrentUser, Store, "Value") as string != "Deny" &&
                   Read(Registry.CurrentUser, Store + @"\NonPackaged", "Value") as string != "Deny";
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }

    static object? Read(RegistryKey hive, string key, string name)
    {
        using var k = hive.OpenSubKey(key);
        return k?.GetValue(name);
    }
}

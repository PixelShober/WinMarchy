using System.Runtime.Versioning;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace WinMarchy.Core;

/// <summary>A registry value as WinMarchy stores it in a restore point.</summary>
/// <param name="Kind">"dword", "qword", "string", "expand", "multi" or "binary".</param>
/// <param name="Text">String values; for binary data base64, for multi-strings joined with "\n".</param>
public sealed record RegistryData(string Kind, string? Text, long Number)
{
    public static RegistryData Dword(int value) => new("dword", null, value);
    public static RegistryData Str(string value) => new("string", value, 0);

    [JsonIgnore]
    public int AsInt => (int)Number;
}

/// <summary>
/// WinMarchy only ever touches HKEY_CURRENT_USER. Keys are given relative to it
/// ("Software\Microsoft\Windows\CurrentVersion\Themes\Personalize").
/// </summary>
public interface IUserRegistry
{
    RegistryData? Read(string key, string name);
    void Write(string key, string name, RegistryData data);
    void Delete(string key, string name);
    bool KeyExists(string key);
    /// <summary>Removes the key with everything below it.</summary>
    void DeleteKey(string key);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsUserRegistry : IUserRegistry
{
    public RegistryData? Read(string key, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key);
        if (k == null || !k.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase)) return null;
        var kind = k.GetValueKind(name);
        var value = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return kind switch
        {
            RegistryValueKind.DWord => new RegistryData("dword", null, Convert.ToInt32(value)),
            RegistryValueKind.QWord => new RegistryData("qword", null, Convert.ToInt64(value)),
            RegistryValueKind.ExpandString => new RegistryData("expand", value as string, 0),
            RegistryValueKind.MultiString => new RegistryData("multi", string.Join("\n", (string[])value!), 0),
            RegistryValueKind.Binary => new RegistryData("binary", Convert.ToBase64String((byte[])value!), 0),
            _ => new RegistryData("string", value?.ToString(), 0),
        };
    }

    public void Write(string key, string name, RegistryData data)
    {
        using var k = Registry.CurrentUser.CreateSubKey(key, writable: true);
        switch (data.Kind)
        {
            case "dword": k.SetValue(name, (int)data.Number, RegistryValueKind.DWord); break;
            case "qword": k.SetValue(name, data.Number, RegistryValueKind.QWord); break;
            case "expand": k.SetValue(name, data.Text ?? "", RegistryValueKind.ExpandString); break;
            case "multi": k.SetValue(name, (data.Text ?? "").Split('\n'), RegistryValueKind.MultiString); break;
            case "binary": k.SetValue(name, Convert.FromBase64String(data.Text ?? ""), RegistryValueKind.Binary); break;
            default: k.SetValue(name, data.Text ?? "", RegistryValueKind.String); break;
        }
    }

    public void Delete(string key, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }

    public bool KeyExists(string key)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key);
        return k != null;
    }

    public void DeleteKey(string key) => Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
}

/// <summary>For tests and for running on Linux.</summary>
public sealed class MemoryUserRegistry : IUserRegistry
{
    readonly Dictionary<string, RegistryData> _values = new(StringComparer.OrdinalIgnoreCase);

    static string Id(string key, string name) => key.TrimEnd('\\') + "|" + name;

    public RegistryData? Read(string key, string name) => _values.TryGetValue(Id(key, name), out var v) ? v : null;
    public void Write(string key, string name, RegistryData data) => _values[Id(key, name)] = data;
    public void Delete(string key, string name) => _values.Remove(Id(key, name));

    static bool Below(string id, string key) => id.StartsWith(key.TrimEnd('\\') + "|", StringComparison.OrdinalIgnoreCase) ||
                                               id.StartsWith(key.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    public bool KeyExists(string key) => _values.Keys.Any(id => Below(id, key));

    public void DeleteKey(string key)
    {
        foreach (var id in _values.Keys.Where(id => Below(id, key)).ToList()) _values.Remove(id);
    }
}

public static class UserRegistry
{
    public static IUserRegistry Current { get; set; } =
        OperatingSystem.IsWindows() ? new WindowsUserRegistry() : new MemoryUserRegistry();
}

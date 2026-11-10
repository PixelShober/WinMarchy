using System.Reflection;

namespace WinMarchy.Core;

/// <summary>Version and identity of the running WinMarchy.</summary>
public static class AppInfo
{
    public const string GitHubRepo = "PixelShober/WinMarchy";

    public static string Version { get; } = ReadVersion();

    /// <summary>
    /// CI and local builds ("0.0.0-ci.12", "0.0.0-dev") run portable and never auto-update – otherwise
    /// testing a pull request build would replace it with the last release.
    /// </summary>
    public static bool IsPrerelease => Version.Contains('-');

    /// <summary>"installed" (C:\WinMarchy\App), "portable" or "dev".</summary>
    public static string Mode { get; set; } = "portable";

    static string ReadVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(info)) return asm.GetName().Version?.ToString(3) ?? "0.0.0";
        var plus = info.IndexOf('+');   // strip "+commit" source revision
        return plus > 0 ? info[..plus] : info;
    }

    /// <summary>Compares "1.2.3" style versions; pre-release suffixes ("-dev") sort before the release.</summary>
    public static int CompareVersions(string a, string b)
    {
        static (Version v, bool pre) Parse(string s)
        {
            s = s.Trim().TrimStart('v', 'V');
            var dash = s.IndexOf('-');
            var core = dash >= 0 ? s[..dash] : s;
            return (System.Version.TryParse(core, out var v) ? v : new Version(0, 0), dash >= 0);
        }
        var (va, pa) = Parse(a);
        var (vb, pb) = Parse(b);
        int c = va.CompareTo(vb);
        if (c != 0) return c;
        return pa == pb ? 0 : pa ? -1 : 1;
    }
}

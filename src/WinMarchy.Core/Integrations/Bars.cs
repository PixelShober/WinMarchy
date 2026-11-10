using WinMarchy.Core.Tools;

namespace WinMarchy.Core.Integrations;

/// <summary>The bar at the top: YASB. Its entries are chosen on the Tiling page.</summary>
public static class Bars
{
    /// <summary>The bar's program.</summary>
    public static ToolDef Tool => ToolCatalog.Yasb;

    /// <summary>The bar's process name (also used for GlazeWM's ignore rule).</summary>
    public const string Process = "yasb";

    /// <summary>Height of the bar in pixels. GlazeWM leaves this much room through its top gap.</summary>
    public const int Height = 26;

    /// <summary>
    /// What the bar can show, in its order on the screen, and whether it is on unless the user says
    /// otherwise. The defaults are Omarchy's Waybar: logo and workspaces, clock, tray, network, sound,
    /// CPU and battery.
    /// </summary>
    public static readonly IReadOnlyList<(string Id, bool On)> Modules = new[]
    {
        ("logo", true), ("workspaces", true), ("clock", true), ("media", false), ("tray", true), ("direction", false),
        ("network", true), ("audio", true), ("cpu", true), ("memory", false), ("battery", true),
    };

    public static bool IsOn(UserSettings s, string module) =>
        s.BarModules.TryGetValue(module, out var on) ? on : Modules.FirstOrDefault(m => m.Id == module).On;
}

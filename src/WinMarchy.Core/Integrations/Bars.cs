using WinMarchy.Core.Tools;

namespace WinMarchy.Core.Integrations;

/// <summary>
/// The bar at the top: YASB, or Zebar on setups from before the switch until the user moves over
/// (Settings → Bar). Both show the same entries, chosen on the Tiling page.
/// </summary>
public static class Bars
{
    public const string Yasb = "yasb";
    public const string Zebar = "zebar";

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

    /// <summary>Entries only YASB has.</summary>
    public static readonly IReadOnlySet<string> YasbOnly = new HashSet<string> { "media" };

    public static bool IsOn(UserSettings s, string module) =>
        s.BarModules.TryGetValue(module, out var on) ? on : Modules.FirstOrDefault(m => m.Id == module).On;

    /// <summary>
    /// The bar that runs now. Never chosen: a Zebar that WinMarchy set up earlier stays until the
    /// user switches, everything else gets YASB.
    /// </summary>
    public static string Chosen(UserSettings s, Locations loc) =>
        s.Bar is Yasb or Zebar ? s.Bar : ZebarPack.IsInstalled(loc) ? Zebar : Yasb;

    /// <summary>The bar a setup installs: the one chosen in the settings, else YASB.</summary>
    public static string ForSetup(UserSettings s) => s.Bar == Zebar ? Zebar : Yasb;

    public static ToolDef Tool(string bar) => bar == Zebar ? ToolCatalog.Zebar : ToolCatalog.Yasb;

    /// <summary>The process name of the bar (also used for GlazeWM's ignore rule).</summary>
    public static string Process(string bar) => bar == Zebar ? "zebar" : "yasb";
}

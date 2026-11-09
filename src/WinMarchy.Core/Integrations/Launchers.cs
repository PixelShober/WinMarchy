using WinMarchy.Core.Tools;

namespace WinMarchy.Core.Integrations;

/// <summary>
/// The program search behind the Win key: Flow Launcher or Fluent Search, chosen in Settings. Both
/// listen on Alt+F24, which AutoHotkey presses when Win is tapped alone. Only the chosen one runs;
/// two would both answer the key.
/// </summary>
public static class Launchers
{
    public const string Flow = "flow";
    public const string FluentSearch = "fluentsearch";

    /// <summary>"flow" or "fluentsearch"; anything else counts as Flow Launcher.</summary>
    public static string Of(UserSettings s) => s.Launcher == FluentSearch ? FluentSearch : Flow;

    public static ToolDef Tool(string id) => id == FluentSearch ? ToolCatalog.FluentSearch : ToolCatalog.Flow;

    /// <summary>The process name, without ".exe".</summary>
    public static string Process(string id) => id == FluentSearch ? FluentSearchConfig.Process : "Flow.Launcher";

    /// <summary>The launcher that is not chosen.</summary>
    public static string Other(string id) => id == FluentSearch ? Flow : FluentSearch;

    public static ToolDef Tool(UserSettings s) => Tool(Of(s));

    public static string Process(UserSettings s) => Process(Of(s));
}

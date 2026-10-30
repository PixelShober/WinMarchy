using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Xml;
using WinMarchy.Core.Themes;

namespace WinMarchy.Core.Integrations;

/// <summary>
/// Flow Launcher is the "Omarchy menu" for apps on Windows: a theme in the Omarchy colours (square,
/// 2 px border like Walker) and the hotkey Alt+F24, which WinMarchy presses when Win is tapped alone.
/// Flow writes its Settings.json when it exits, so it is closed first and started again afterwards.
/// </summary>
public static class FlowTheme
{
    public const string ThemeName = "WinMarchy";
    public const string Hotkey = "Alt + F24";

    public static string Xaml(Palette p)
    {
        var values = new Dictionary<string, string>
        {
            ["background"] = p.Background, ["foreground"] = p.Foreground, ["accent"] = p.Accent,
            ["selection"] = p.Selection, ["muted"] = p.Muted, ["dark_foreground"] = ColorMath.Mix(p.Background, p.Foreground, 0.67),
            ["selected_row"] = ColorMath.Mix(p.Background, p.Foreground, 0.08), ["border"] = p.Accent,
        };
        var xaml = Template;
        foreach (var (k, v) in values) xaml = xaml.Replace("{{" + k + "}}", v);
        // Never write a theme Flow cannot load.
        new XmlDocument().LoadXml(xaml);
        return xaml;
    }

    public static string PatchSettings(string original, bool light)
    {
        var root = Jsonc.ParseObject(original);
        root["Hotkey"] = Hotkey;
        root["Theme"] = ThemeName;
        root["ColorScheme"] = light ? "Light" : "Dark";
        // Older Flow versions store the backdrop as a number, newer ones as a name. A fresh file
        // leaves it to Flow.
        if (root.ContainsKey("BackdropType"))
            root["BackdropType"] = root["BackdropType"] is JsonValue v && v.TryGetValue<string>(out _) ? "None" : 0;
        root["UseSound"] = false;
        return Json.WriteLike(root, original);
    }

    /// <summary>Settings.json of the portable or installed Flow Launcher.</summary>
    public static string SettingsFile(Locations loc, string? flowExe)
    {
        if (flowExe != null)
        {
            var portable = Path.Combine(Path.GetDirectoryName(flowExe)!, "UserData", "Settings", "Settings.json");
            if (File.Exists(portable)) return portable;
        }
        return loc.FlowSettings;
    }

    public static async Task ApplyAsync(IntegrationContext ctx, Palette palette, string? flowExe, CancellationToken ct)
    {
        var settingsFile = SettingsFile(ctx.Loc, flowExe);
        bool fresh = false;
        if (!File.Exists(settingsFile))
        {
            if (flowExe == null || !File.Exists(flowExe))
            {
                ctx.Report(L.T("integrations.flow.notFound"));
                return;
            }
            // Just installed: Flow writes its Settings.json only when it exits for the first time.
            // A file with the few values WinMarchy needs is enough; Flow fills in the rest.
            fresh = true;
        }
        var themeFile = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(settingsFile)!)!, "Themes", ThemeName + ".xaml");

        var running = Process.GetProcessesByName("Flow.Launcher");
        string? exe = flowExe;
        foreach (var p in running)
        {
            try { exe ??= p.MainModule?.FileName; } catch { }
        }
        if (running.Length > 0) await StopAsync(running, ct);

        ctx.WriteFile(themeFile, Xaml(palette));
        ctx.WriteFile(settingsFile, PatchSettings(fresh ? "{}" : FileSafety.ReadShared(settingsFile), palette.IsLight));
        ctx.Report(L.T("integrations.flow.done"));

        // Flow is the launcher behind Win: afterwards it runs, whether it did before or not.
        if (exe != null && File.Exists(exe)) await StartAndCheckAsync(exe, ct);
    }

    /// <summary>
    /// Starts Flow and makes sure it stays up. Right after a restart Flow sometimes quits again: the
    /// old instance was still letting go, or Squirrel's first run was still busy. Flow then gets up to
    /// two more starts; every step goes into the verbose log.
    /// </summary>
    public static async Task StartAndCheckAsync(string exe, CancellationToken ct)
    {
        foreach (var old in Process.GetProcessesByName("Flow.Launcher"))
        {
            try
            {
                if (!old.HasExited && !old.WaitForExit(5000)) Log.Trace($"flow: pid {old.Id} still running before the start", "flow");
            }
            catch { }
        }
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            Log.Trace($"flow: start {attempt} of 3: {exe}", "flow");
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true })?.Dispose();
            // Flow.Launcher.exe in the install folder only starts the real one and exits; give it
            // time, then it has to be there twice in a row.
            int seen = 0;
            for (int i = 0; i < 20 && seen < 2; i++)
            {
                await Task.Delay(500, ct);
                var running = Process.GetProcessesByName("Flow.Launcher");
                seen = i >= 9 && running.Length > 0 ? seen + 1 : 0;
                if (i is 3 or 9 or 15) Log.Trace($"flow: after {(i + 1) * 0.5:0.0} s: {string.Join(", ", running.Select(p => p.Id))}", "flow");
            }
            if (seen >= 2)
            {
                Log.Trace("flow: running", "flow");
                return;
            }
            Log.Warn(L.T("integrations.flow.restartAgain"));
        }
    }

    public static async Task StopAsync(Process[] processes, CancellationToken ct)
    {
        foreach (var p in processes)
        {
            try { p.CloseMainWindow(); } catch { }
        }
        for (int i = 0; i < 20 && processes.Any(p => !p.HasExited); i++) await Task.Delay(150, ct);
        foreach (var p in processes.Where(p => !p.HasExited))
        {
            try { p.Kill(); p.WaitForExit(3000); } catch { }
        }
    }

    const string Template = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <!-- Generated by WinMarchy from the current Omarchy theme. -->
  <ResourceDictionary.MergedDictionaries><ResourceDictionary Source="pack://application:,,,/Themes/Base.xaml" /></ResourceDictionary.MergedDictionaries>
  <CornerRadius x:Key="ItemRadius">0</CornerRadius>
  <Thickness x:Key="ItemMargin">6 0 6 0</Thickness>
  <Thickness x:Key="ResultMargin">0 0 0 6</Thickness>
  <Style x:Key="WindowBorderStyle" TargetType="{x:Type Border}" BasedOn="{StaticResource BaseWindowBorderStyle}"><Setter Property="Background" Value="{{background}}"/><Setter Property="BorderBrush" Value="{{border}}"/><Setter Property="BorderThickness" Value="2"/><Setter Property="CornerRadius" Value="0"/></Style>
  <Style x:Key="WindowStyle" TargetType="{x:Type Window}" BasedOn="{StaticResource BaseWindowStyle}"/>
  <Style x:Key="QueryBoxStyle" TargetType="{x:Type TextBox}" BasedOn="{StaticResource BaseQueryBoxStyle}"><Setter Property="Foreground" Value="{{foreground}}"/><Setter Property="CaretBrush" Value="{{accent}}"/><Setter Property="SelectionBrush" Value="{{selection}}"/></Style>
  <Style x:Key="QuerySuggestionBoxStyle" TargetType="{x:Type TextBox}" BasedOn="{StaticResource BaseQuerySuggestionBoxStyle}"><Setter Property="Foreground" Value="{{dark_foreground}}"/></Style>
  <Style x:Key="PendingLineStyle" TargetType="{x:Type Line}" BasedOn="{StaticResource BasePendingLineStyle}"><Setter Property="Stroke" Value="{{accent}}"/></Style>
  <Style x:Key="SeparatorStyle" TargetType="{x:Type Rectangle}" BasedOn="{StaticResource BaseSeparatorStyle}"><Setter Property="Fill" Value="{{muted}}"/></Style>
  <Style x:Key="SearchIconStyle" TargetType="{x:Type Path}" BasedOn="{StaticResource BaseSearchIconStyle}"><Setter Property="Fill" Value="{{dark_foreground}}"/></Style>
  <Style x:Key="ItemGlyph" TargetType="{x:Type TextBlock}" BasedOn="{StaticResource BaseGlyphStyle}"><Setter Property="Foreground" Value="{{foreground}}"/></Style>
  <Style x:Key="ItemGlyphSelectedStyle" TargetType="{x:Type TextBlock}" BasedOn="{StaticResource BaseGlyphSelectedStyle}"><Setter Property="Foreground" Value="{{accent}}"/></Style>
  <Style x:Key="ItemTitleStyle" TargetType="{x:Type TextBlock}" BasedOn="{StaticResource BaseItemTitleStyle}"><Setter Property="Foreground" Value="{{foreground}}"/></Style>
  <Style x:Key="ItemSubTitleStyle" TargetType="{x:Type TextBlock}" BasedOn="{StaticResource BaseItemSubTitleStyle}"><Setter Property="Foreground" Value="{{dark_foreground}}"/></Style>
  <Style x:Key="ItemNumberStyle" TargetType="{x:Type TextBlock}" BasedOn="{StaticResource BaseItemNumberStyle}"><Setter Property="Foreground" Value="{{dark_foreground}}"/></Style>
  <Style x:Key="ItemTitleSelectedStyle" TargetType="{x:Type TextBlock}" BasedOn="{StaticResource BaseItemTitleSelectedStyle}"><Setter Property="Foreground" Value="{{accent}}"/></Style>
  <Style x:Key="ItemSubTitleSelectedStyle" TargetType="{x:Type TextBlock}" BasedOn="{StaticResource BaseItemSubTitleSelectedStyle}"><Setter Property="Foreground" Value="{{foreground}}"/></Style>
  <SolidColorBrush x:Key="ItemSelectedBackgroundColor">{{selected_row}}</SolidColorBrush>
  <Style x:Key="ItemImageSelectedStyle" TargetType="{x:Type Image}" BasedOn="{StaticResource BaseItemImageSelectedStyle}"/>
  <Style x:Key="ItemHotkeyStyle" TargetType="{x:Type TextBlock}" BasedOn="{StaticResource BaseItemHotkeyStyle}"><Setter Property="Foreground" Value="{{dark_foreground}}"/></Style>
  <Style x:Key="ItemHotkeySelectedStyle" TargetType="{x:Type TextBlock}" BasedOn="{StaticResource BaseItemHotkeySelectedStyle}"><Setter Property="Foreground" Value="{{foreground}}"/></Style>
  <Style x:Key="HighlightStyle"><Setter Property="Inline.FontWeight" Value="Bold"/><Setter Property="Inline.Foreground" Value="{{accent}}"/></Style>
  <Style x:Key="ThumbStyle" TargetType="{x:Type Thumb}" BasedOn="{StaticResource BaseThumbStyle}"><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type Thumb}"><Border Background="{{muted}}" BorderThickness="0" CornerRadius="0"/></ControlTemplate></Setter.Value></Setter></Style>
  <Style x:Key="ScrollBarStyle" TargetType="{x:Type ScrollBar}" BasedOn="{StaticResource BaseScrollBarStyle}"><Setter Property="Width" Value="3"/></Style>
  <Style x:Key="ClockBox" TargetType="{x:Type TextBlock}" BasedOn="{StaticResource BaseClockBox}"><Setter Property="Foreground" Value="{{dark_foreground}}"/></Style>
  <Style x:Key="DateBox" TargetType="{x:Type TextBlock}" BasedOn="{StaticResource BaseDateBox}"><Setter Property="Foreground" Value="{{dark_foreground}}"/></Style>
  <Style x:Key="PreviewBorderStyle" TargetType="{x:Type Border}" BasedOn="{StaticResource BasePreviewBorderStyle}"><Setter Property="BorderBrush" Value="{{muted}}"/></Style>
  <Style x:Key="PreviewItemTitleStyle" TargetType="{x:Type TextBlock}" BasedOn="{StaticResource BasePreviewItemTitleStyle}"><Setter Property="Foreground" Value="{{foreground}}"/></Style>
  <Style x:Key="PreviewItemSubTitleStyle" TargetType="{x:Type TextBlock}" BasedOn="{StaticResource BasePreviewItemSubTitleStyle}"><Setter Property="Foreground" Value="{{dark_foreground}}"/></Style>
  <Style x:Key="PreviewGlyph" TargetType="{x:Type TextBlock}" BasedOn="{StaticResource BasePreviewGlyph}"><Setter Property="Foreground" Value="{{foreground}}"/></Style>
</ResourceDictionary>
""";
}

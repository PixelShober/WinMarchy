namespace WinMarchy.App.Startup;

/// <summary>
/// The command line. "--action" takes everything after it, because AutoHotkey, GlazeWM and the bar pass
/// the action unquoted ("--action wm:focus --direction left").
/// </summary>
public sealed record StartArgs(string? Action, string? Page, bool Background, bool AfterUpdate, bool Portable, string? SelfTest)
{
    public const string AfterUpdateSwitch = "--after-update";
    public const string BackgroundSwitch = "--background";
    public const string PortableSwitch = "--portable";

    public static StartArgs Parse(IReadOnlyList<string> args)
    {
        string? action = null, page = null, selfTest = null;
        bool background = false, afterUpdate = false, portable = false;
        for (int i = 0; i < args.Count; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--action":
                    action = string.Join(' ', args.Skip(i + 1)).Trim();
                    i = args.Count;
                    break;
                case "--page" when i + 1 < args.Count:
                    page = args[++i];
                    break;
                case "--selftest" when i + 1 < args.Count:
                    selfTest = args[++i];
                    break;
                case BackgroundSwitch:
                    background = true;
                    break;
                case AfterUpdateSwitch:
                    afterUpdate = true;
                    break;
                case PortableSwitch:
                    portable = true;
                    break;
            }
        }
        return new StartArgs(string.IsNullOrEmpty(action) ? null : action, page, background, afterUpdate, portable, selfTest);
    }
}

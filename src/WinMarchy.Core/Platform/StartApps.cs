namespace WinMarchy.Core.Platform;

/// <summary>A program from the Start menu: its name and what starts it.</summary>
public sealed record StartApp(string Name, string Target);

/// <summary>
/// The programs Windows' own search finds, for the WinMarchy menu (Win+Space): the Start menu's app
/// list ("shell:AppsFolder"), which also holds Store apps. Read once in the background and kept for a
/// few minutes. If the shell does not hand it out, the Start menu shortcuts are used instead.
/// </summary>
public static class StartApps
{
    static IReadOnlyList<StartApp> _cache = Array.Empty<StartApp>();
    static DateTime _loaded = DateTime.MinValue;
    static Task? _loading;
    static readonly object Lock = new();

    /// <summary>What is known now; starts a refresh if the list is older than five minutes.</summary>
    public static IReadOnlyList<StartApp> Current
    {
        get
        {
            Refresh();
            return _cache;
        }
    }

    /// <summary>Reads the list again unless it is fresh; the task ends when the list is there.</summary>
    public static Task Refresh(bool force = false)
    {
        lock (Lock)
        {
            if (_loading is { IsCompleted: false } || (!force && DateTime.Now - _loaded < TimeSpan.FromMinutes(5))) return _loading ?? Task.CompletedTask;
            return _loading = Task.Run(() =>
            {
                try
                {
                    var list = OperatingSystem.IsWindows() ? FromShell() : new List<StartApp>();
                    if (list.Count == 0) list = FromShortcuts();
                    _cache = list;
                    _loaded = DateTime.Now;
                }
                catch (Exception ex)
                {
                    Log.Debug("Start menu apps: " + Net.Describe(ex));
                }
            });
        }
    }

    /// <summary>Best matches first: name starts with the text, then a word does, then it contains it.</summary>
    public static IReadOnlyList<StartApp> Search(IEnumerable<StartApp> apps, string query, int max = 8)
    {
        query = query.Trim();
        if (query.Length == 0) return Array.Empty<StartApp>();
        int Rank(StartApp a)
        {
            if (a.Name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)) return 0;
            if (a.Name.Split(' ', '-', '.').Any(w => w.StartsWith(query, StringComparison.CurrentCultureIgnoreCase))) return 1;
            return a.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ? 2 : 3;
        }
        return apps.Select(a => (App: a, Rank: Rank(a))).Where(x => x.Rank < 3)
            .OrderBy(x => x.Rank).ThenBy(x => x.App.Name.Length).ThenBy(x => x.App.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => x.App).Take(max).ToList();
    }

    /// <summary>Starts an app the way the Start menu does.</summary>
    public static bool Launch(StartApp app) =>
        app.Target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
            ? AppLauncher.Start(app.Target)
            : AppLauncher.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "shell:AppsFolder\\" + app.Target);

    /// <summary>The Start menu's app list through Shell.Application (late-bound, on an STA thread as the shell wants).</summary>
    static List<StartApp> FromShell()
    {
        var result = new List<StartApp>();
        var thread = new Thread(() =>
        {
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application");
                if (type == null) return;
                dynamic shell = Activator.CreateInstance(type)!;
                dynamic? folder = shell.NameSpace("shell:::{4234d49b-0245-4df3-b780-3893943456e1}");
                if (folder == null) return;
                foreach (dynamic item in folder.Items())
                {
                    string name = item.Name;
                    string path = item.Path;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
                    // Links to websites and uninstallers are noise in a launcher.
                    if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase) || name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("deinstall", StringComparison.OrdinalIgnoreCase)) continue;
                    result.Add(new StartApp(name, path));
                }
            }
            catch (Exception ex)
            {
                Log.Debug("AppsFolder: " + ex.Message);
            }
        }) { IsBackground = true, Name = "WinMarchy apps" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(15));
        return result.GroupBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).Select(g => g.First()).ToList();
    }

    static List<StartApp> FromShortcuts()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "Start Menu", "Programs"),
        };
        var list = new List<StartApp>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                foreach (var link in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileNameWithoutExtension(link);
                    if (name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase) || name.Contains("deinstall", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new StartApp(name, link));
                }
            }
            catch { }
        }
        return list.GroupBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).Select(g => g.First()).ToList();
    }
}

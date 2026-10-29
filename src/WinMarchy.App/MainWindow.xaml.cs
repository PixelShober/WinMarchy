using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using WinMarchy.App.Pages;
using WinMarchy.App.Ui;
using WinMarchy.Core;

namespace WinMarchy.App;

public partial class MainWindow : Window
{
    readonly Dictionary<string, Func<FrameworkElement>> _factories;
    readonly Dictionary<string, FrameworkElement> _pages = new();
    readonly DispatcherTimer _status = new() { Interval = TimeSpan.FromSeconds(2) };
    string _route = "";
    bool _ready;

    public MainWindow()
    {
        InitializeComponent();
        Toasts.Attach(ToastHost);
        Dialogs.Attach(DialogHost);
        _factories = new Dictionary<string, Func<FrameworkElement>>
        {
            ["start"] = () => new OverviewPage(),
            ["setup"] = () => new SetupPage(),
            ["themes"] = () => new ThemesPage(),
            ["hotkeys"] = () => new HotkeysPage(),
            ["tiling"] = () => new TilingPage(),
            ["restore"] = () => new RestorePage(),
            ["settings"] = () => new SettingsPage(),
            ["log"] = () => new LogPage(),
        };
        VersionText.Text = $"WinMarchy {AppInfo.Version} · {L.T("app.modes." + AppInfo.Mode)}";
        WindowPlacement.Restore(this);

        SourceInitialized += (_, _) => ThemeManager.ApplyFrame(this);
        Loaded += (_, _) =>
        {
            SelectScheme(AppModel.Settings.ColorScheme);
            _ready = true;
            if (_route.Length == 0) Navigate(AppState.Load().SetupDone ? "start" : "setup");
            UpdateStatus();
        };
        SizeChanged += (_, _) => GlideMarker(animate: false);
        ThemeManager.Changed += () => ThemeManager.ApplyFrame(this);
        _status.Tick += (_, _) => UpdateStatus();
        _status.Start();
        AppModel.Updater.Changed += s => Dispatcher.InvokeAsync(() => ShowUpdate(s));
        PreviewKeyDown += OnKey;
    }

    // ------------------------------------------------------------------ navigation

    RadioButton NavFor(string route) => route switch
    {
        "setup" => NavSetup, "themes" => NavThemes, "hotkeys" => NavHotkeys, "tiling" => NavTiling,
        "restore" => NavRestore, "settings" => NavSettings, "log" => NavLog, _ => NavStart,
    };

    static string RouteOf(RadioButton b) => b.Name switch
    {
        "NavSetup" => "setup", "NavThemes" => "themes", "NavHotkeys" => "hotkeys", "NavTiling" => "tiling",
        "NavRestore" => "restore", "NavSettings" => "settings", "NavLog" => "log", _ => "start",
    };

    public void Navigate(string route)
    {
        if (!_factories.ContainsKey(route)) route = "start";
        var nav = NavFor(route);
        if (nav.IsChecked != true) nav.IsChecked = true;   // raises OnNav
        else Show(route);
    }

    void OnNav(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton b) Show(RouteOf(b));
    }

    void Show(string route)
    {
        if (route == _route) return;
        bool first = _route.Length == 0;
        _route = route;
        if (!_pages.TryGetValue(route, out var page))
        {
            page = _factories[route]();
            _pages[route] = page;
        }
        if (page is IPage p) p.OnShown();
        if (first || !_ready) Page.Content = page;
        else Motion.SwapPage(Page, Scroller, OverlayHost, page);
        Scroller.ScrollToTop();
        GlideMarker(animate: !first);
    }

    void GlideMarker(bool animate)
    {
        if (_route.Length == 0) return;
        Dispatcher.InvokeAsync(() => Motion.Glide(Marker, NavFor(_route), MarkerCanvas, animate), DispatcherPriority.Loaded);
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        // Ctrl+1 … Ctrl+8 jump to the tabs.
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key >= Key.D1 && e.Key <= Key.D8)
        {
            var routes = new[] { "start", "setup", "themes", "hotkeys", "tiling", "restore", "settings", "log" };
            Navigate(routes[e.Key - Key.D1]);
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ appearance

    void SelectScheme(string scheme)
    {
        var target = scheme switch { "light" => SchemeLight, "dark" => SchemeDark, _ => SchemeSystem };
        _suppressScheme = true;
        target.IsChecked = true;
        _suppressScheme = false;
    }

    bool _suppressScheme;

    void OnScheme(object sender, RoutedEventArgs e)
    {
        if (_suppressScheme || !_ready || sender is not RadioButton b || b.Tag is not string scheme) return;
        if (scheme == AppModel.Settings.ColorScheme) return;
        ThemeManager.SwitchWithReveal(Root, OverlayHost, b, () =>
        {
            ThemeManager.Set(scheme);
            AppModel.Update(s => s.ColorScheme = scheme);
        });
    }

    // ------------------------------------------------------------------ status

    void UpdateStatus()
    {
        var agent = AppModel.Agent;
        string text;
        string brush;
        if (agent?.Glaze is { Connected: true })
        {
            text = agent.StatusLine();
            brush = "Ok";
        }
        else if (Core.Services.Components.IsRunning("glazewm"))
        {
            text = L.T("status.glazeConnecting");
            brush = "Warn";
        }
        else
        {
            text = L.T("status.glazeStopped");
            brush = "TextMuted";
        }
        StatusText.Text = text;
        StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
    }

    void OnStatusClick(object sender, MouseButtonEventArgs e) => Navigate("tiling");

    void ShowUpdate(Core.Updates.UpdateStatus s)
    {
        if (s.Ready)
        {
            UpdateText.Text = L.T("update.readyBanner", ("version", s.Latest ?? "?"));
            UpdateBanner.Visibility = Visibility.Visible;
        }
    }

    void OnUpdateRestart(object sender, RoutedEventArgs e) => App.RestartForUpdate();

    // ------------------------------------------------------------------ window

    public void BringToFront(string? route = null)
    {
        if (route != null) Navigate(route);
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    bool _hintShown;

    protected override void OnClosing(CancelEventArgs e)
    {
        WindowPlacement.Save(this);
        if (App.ExitRequested) return;
        // With the agent running WinMarchy stays in the tray; the window only hides.
        if (AppModel.Agent != null && AppState.Load().SetupDone)
        {
            e.Cancel = true;
            Hide();
            if (!_hintShown)
            {
                _hintShown = true;
                Overlays.Osd.Show(L.T("app.stillRunning"));
            }
            return;
        }
        if (AppModel.Jobs.AnyRunning)
        {
            e.Cancel = true;
            _ = ConfirmCloseAsync();
            return;
        }
        App.Quit();
    }

    async Task ConfirmCloseAsync()
    {
        if (await Dialogs.ConfirmAsync(L.T("app.closeTitle"), L.T("app.confirmClose"), L.T("app.closeAnyway"), danger: true)) App.Quit();
    }
}

/// <summary>A page that wants to refresh itself whenever it comes into view.</summary>
public interface IPage
{
    void OnShown();
}

/// <summary>Remembers window size and position in C:\WinMarchy\Config\window.json.</summary>
static class WindowPlacement
{
    sealed record Placement(double Left, double Top, double Width, double Height, bool Maximized);

    public static void Restore(Window w)
    {
        try
        {
            if (!File.Exists(Paths.WindowFile)) return;
            var p = Json.Deserialize<Placement>(File.ReadAllText(Paths.WindowFile));
            if (p == null || p.Width < 600 || p.Height < 400) return;
            var visible = p.Left >= SystemParameters.VirtualScreenLeft - 50 && p.Top >= SystemParameters.VirtualScreenTop - 50 &&
                          p.Left + 100 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                          p.Top + 100 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
            w.Width = p.Width;
            w.Height = p.Height;
            if (visible)
            {
                w.WindowStartupLocation = WindowStartupLocation.Manual;
                w.Left = p.Left;
                w.Top = p.Top;
            }
            if (p.Maximized) w.WindowState = WindowState.Maximized;
        }
        catch
        {
            // start with defaults
        }
    }

    public static void Save(Window w)
    {
        try
        {
            var r = w.WindowState == WindowState.Normal ? new Rect(w.Left, w.Top, w.Width, w.Height) : w.RestoreBounds;
            FileSafety.WriteText(Paths.WindowFile, Json.Serialize(new Placement(r.Left, r.Top, r.Width, r.Height, w.WindowState == WindowState.Maximized)));
        }
        catch
        {
            // not important
        }
    }
}

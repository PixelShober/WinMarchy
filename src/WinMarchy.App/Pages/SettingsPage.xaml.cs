using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WinMarchy.App.Ui;
using WinMarchy.Core;
using WinMarchy.Core.Integrations;
using WinMarchy.Core.Tools;

namespace WinMarchy.App.Pages;

public partial class SettingsPage : UserControl, IPage
{
    bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
        Subtitle.Text = L.T("settings.subtitle", ("path", Paths.SettingsFile));
        foreach (var (tag, key) in new[] { ("auto", "settings.languages.auto"), ("de", "settings.languages.de"), ("de-CH", "settings.languages.deCH"), ("en", "settings.languages.en") })
            LanguageBox.Items.Add(new ComboBoxItem { Content = L.T(key), Tag = tag });
        var folders = new (string Key, string Path)[]
        {
            ("settings.folder.root", Paths.Root), ("settings.folder.logs", Paths.LogDir), ("settings.folder.backups", Paths.BackupDir),
            ("settings.folder.tools", Paths.ToolsDir), ("settings.folder.glazewm", Locations.Current.GlazeWmConfig),
            ("settings.folder.bar", Locations.Current.YasbDir),
        };
        foreach (var (key, path) in folders)
        {
            var b = UiKit.IconTextButton("folder", L.T(key));
            b.Margin = new Thickness(0, 0, 8, 8);
            b.ToolTip = path;
            b.Click += (_, _) => UiKit.OpenFolder(path);
            Folders.Children.Add(b);
        }
        AppModel.Updater.Changed += _ => Dispatcher.InvokeAsync(ShowUpdate);
        ThemeManager.Changed += () => Dispatcher.InvokeAsync(SelectScheme);
    }

    public void OnShown()
    {
        _loading = true;
        var s = AppModel.Settings;
        SelectScheme();
        LanguageBox.SelectedItem = LanguageBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, s.Language, StringComparison.OrdinalIgnoreCase))
                                   ?? LanguageBox.Items[0];
        AutoUpdate.IsChecked = s.AutoUpdate;
        FilesPath.Text = s.FilesSettingsPath ?? "";
        ShowFilesDefault();
        FillBrowsers(Browser1, s.Browser);
        FillBrowsers(Browser2, s.Browser2);
        (s.ScreenshotTool == Core.Platform.Screenshots.Greenshot ? ShotGreenshot : ShotWindows).IsChecked = true;
        ShowScreenshotHint();
        (s.GlazeBuild == GlazeBuilds.Official ? GlazeOfficial : GlazeWinMarchy).IsChecked = true;
        ShowGlazeHint();
        (AppModel.Engine.Components.Bar == Bars.Zebar ? BarZebar : BarYasb).IsChecked = true;
        ShowBarHint();
        ShowExplorerBlur();
        _loading = false;
        ShowUpdate();
    }

    // ------------------------------------------------------------------ browsers

    const string OtherBrowser = "*other*";

    /// <summary>Windows' default, the installed browsers, the chosen one if it is none of those, "other…".</summary>
    static void FillBrowsers(ComboBox box, string? chosen)
    {
        box.Items.Clear();
        var standard = Core.Platform.AppLauncher.DefaultBrowser();
        box.Items.Add(new ComboBoxItem
        {
            Content = standard != null ? L.T("settings.browserDefaultNamed", ("name", BrowserName(standard))) : L.T("settings.browserDefault"),
            Tag = "",
        });
        foreach (var (name, exe) in Core.Platform.AppLauncher.InstalledBrowsers()) box.Items.Add(new ComboBoxItem { Content = name, Tag = exe });
        if (chosen != null && !box.Items.Cast<ComboBoxItem>().Any(i => string.Equals((string)i.Tag, chosen, StringComparison.OrdinalIgnoreCase)))
            box.Items.Add(new ComboBoxItem { Content = Path.GetFileNameWithoutExtension(chosen), Tag = chosen, ToolTip = chosen });
        box.Items.Add(new ComboBoxItem { Content = L.T("settings.browserOther"), Tag = OtherBrowser });
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, chosen ?? "", StringComparison.OrdinalIgnoreCase)) ?? box.Items[0];
    }

    static string BrowserName(string exe) => Path.GetFileNameWithoutExtension(exe).ToLowerInvariant() switch
    {
        "msedge" => "Microsoft Edge", "chrome" => "Google Chrome", "firefox" => "Firefox", "brave" => "Brave",
        "vivaldi" => "Vivaldi", "opera" => "Opera", var other => other,
    };

    void OnBrowser(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || sender is not ComboBox { Tag: string which } box || box.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        string? exe = tag.Length == 0 ? null : tag;
        if (tag == OtherBrowser)
        {
            var d = new OpenFileDialog { Title = L.T("settings.browserOther"), Filter = L.T("settings.exeFilter") };
            if (d.ShowDialog(Window.GetWindow(this)) != true)
            {
                _loading = true;
                FillBrowsers(box, which == "1" ? AppModel.Settings.Browser : AppModel.Settings.Browser2);
                _loading = false;
                return;
            }
            exe = d.FileName;
        }
        AppModel.Update(s => { if (which == "1") s.Browser = exe; else s.Browser2 = exe; });
        _loading = true;
        FillBrowsers(box, exe);
        _loading = false;
    }

    // ------------------------------------------------------------------ screenshots

    void ShowScreenshotHint()
    {
        bool greenshot = AppModel.Settings.ScreenshotTool == Core.Platform.Screenshots.Greenshot;
        var installed = AppModel.Engine.Components.Status(ToolCatalog.Greenshot).Installed;
        ShotHint.Text = greenshot ? L.T("settings.screenshotGreenshotHint")
            : installed ? L.T("settings.screenshotWindowsHint") : L.T("settings.screenshotWindowsHint") + " " + L.T("settings.screenshotGreenshotInstall");
    }

    /// <summary>Greenshot is installed if needed, Print goes to it, and the hotkeys are written again.</summary>
    void OnScreenshotTool(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string tool } || tool == AppModel.Settings.ScreenshotTool) return;
        var job = AppModel.Run("screenshots", "hotkeys", L.T("jobs.screenshots"), AppModel.Engine.ScreenshotToolSteps(tool), async j =>
        {
            await Dispatcher.InvokeAsync(() =>
            {
                Toasts.Show(j.State == JobState.Done ? ToastKind.Ok : ToastKind.Error,
                    j.State == JobState.Done ? L.T("settings.screenshotDone", ("tool", L.T(tool == "greenshot" ? "settings.screenshotGreenshot" : "settings.screenshotWindows"))) : j.Status);
                OnShown();
            });
        });
        if (job != null) ShotJob.Follow(job.Id);
        else OnShown();
    }

    // ------------------------------------------------------------------ GlazeWM build

    void ShowGlazeHint()
    {
        var status = AppModel.Engine.Components.Status(ToolCatalog.GlazeWm);
        var now = !status.Installed ? L.T("settings.glazeMissing")
            : L.T("settings.glazeInstalled", ("build", L.T(ToolCatalog.IsWinMarchyBuild(status.Path) ? "settings.glazeWinMarchy" : "settings.glazeOfficial")),
                ("version", status.Version ?? "?"));
        var wanted = L.T(AppModel.Settings.GlazeBuild == GlazeBuilds.Official ? "settings.glazeOfficialHint" : "settings.glazeWinMarchyHint");
        GlazeHint.Text = wanted + " " + now;
    }

    /// <summary>Installs the other GlazeWM in C:\WinMarchy\Tools and starts it with WinMarchy's configuration.</summary>
    void OnGlazeBuild(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string build } || build == AppModel.Settings.GlazeBuild) return;
        var job = AppModel.Run("glazewm", "setup", L.T("jobs.glazeBuild"), AppModel.Engine.GlazeBuildSteps(build), async j =>
        {
            await Dispatcher.InvokeAsync(() =>
            {
                Toasts.Show(j.State == JobState.Done ? ToastKind.Ok : ToastKind.Error,
                    j.State == JobState.Done ? L.T("settings.glazeDone", ("build", L.T(build == GlazeBuilds.Official ? "settings.glazeOfficial" : "settings.glazeWinMarchy"))) : j.Status);
                OnShown();
            });
        });
        if (job != null) GlazeJob.Follow(job.Id);
        else OnShown();
    }

    // ------------------------------------------------------------------ bar

    void ShowBarHint()
    {
        var c = AppModel.Engine.Components;
        var zebar = c.Status(ToolCatalog.Zebar);
        var yasb = c.Status(ToolCatalog.Yasb);
        var parts = new List<string>
        {
            L.T(c.Bar == Bars.Zebar ? "settings.barZebarHint" : "settings.barYasbHint"),
            yasb.Installed ? L.T("settings.barInstalled", ("tool", "YASB"), ("version", yasb.Version ?? "?")) : L.T("settings.barNotInstalled", ("tool", "YASB")),
        };
        var zebarThere = zebar.Installed || ZebarPack.IsInstalled(Locations.Current);
        if (zebarThere) parts.Add(zebar.Installed && !zebar.InstalledByWinMarchy ? L.T("settings.zebarElsewhere", ("path", zebar.Path)) : L.T("settings.zebarStillThere"));
        BarHint.Text = string.Join(" ", parts);
        // Zebar goes only once YASB is the bar, so the desktop never ends up without one.
        RemoveZebar.Visibility = zebarThere ? Visibility.Visible : Visibility.Collapsed;
        RemoveZebar.IsEnabled = c.Bar == Bars.Yasb;
    }

    /// <summary>Installs the other bar if needed, sets it up in the theme's colours and starts it instead.</summary>
    void OnBar(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string bar } || bar == AppModel.Engine.Components.Bar) return;
        var name = Bars.Tool(bar).Name;
        var job = AppModel.Run("bar", "setup", L.T("jobs.bar", ("tool", name)), AppModel.Engine.BarSteps(bar), async j =>
        {
            await Dispatcher.InvokeAsync(() =>
            {
                Toasts.Show(j.State == JobState.Done ? ToastKind.Ok : ToastKind.Error,
                    j.State == JobState.Done ? L.T("settings.barDone", ("tool", name)) : j.Status);
                OnShown();
            });
        });
        if (job != null) BarJob.Follow(job.Id);
        else OnShown();
    }

    async void OnRemoveZebar(object sender, RoutedEventArgs e)
    {
        if (!await Dialogs.ConfirmAsync(L.T("settings.zebarRemoveTitle"), L.T("settings.zebarRemoveConfirm"), L.T("settings.zebarRemove"), danger: true)) return;
        var job = AppModel.Run("zebar-remove", "setup", L.T("jobs.zebarRemove"), AppModel.Engine.RemoveZebarSteps(), async j =>
        {
            await Dispatcher.InvokeAsync(() =>
            {
                Toasts.Show(j.State == JobState.Done ? ToastKind.Ok : ToastKind.Error, j.State == JobState.Done ? L.T("settings.zebarRemoved") : j.Status);
                OnShown();
            });
        });
        if (job != null) BarJob.Follow(job.Id);
    }

    // ------------------------------------------------------------------ ExplorerBlurMica

    void ShowExplorerBlur()
    {
        var on = AppModel.Engine.ExplorerBlurIsOn;
        BlurSwitch.IsChecked = on;
        BlurBadge.Child = UiKit.Badge(L.T("setup.experimental"), "warn");
        BlurHint.Text = on ? L.T("settings.explorerBlurOnHint") : L.T("settings.explorerBlurOffHint");
    }

    /// <summary>Downloaded if needed and registered for this user only, or registered no more. Each switch is a restore point.</summary>
    async void OnExplorerBlur(object sender, RoutedEventArgs e)
    {
        bool on = BlurSwitch.IsChecked == true;
        if (on && !await Dialogs.ConfirmAsync(L.T("settings.explorerBlurTitle"), L.T("settings.explorerBlurConfirm"), L.T("settings.explorerBlurSwitch")))
        {
            ShowExplorerBlur();
            return;
        }
        var job = AppModel.Run("explorer-blur", "setup", L.T(on ? "jobs.explorerBlurOn" : "jobs.explorerBlurOff"), AppModel.Engine.ExplorerBlurSteps(on), async j =>
        {
            await Dispatcher.InvokeAsync(() =>
            {
                Toasts.Show(j.State == JobState.Done ? ToastKind.Ok : ToastKind.Error,
                    j.State == JobState.Done ? L.T(on ? "settings.explorerBlurOn" : "settings.explorerBlurOff") : j.Status);
                ShowExplorerBlur();
            });
        });
        if (job != null) BlurJob.Follow(job.Id);
        else ShowExplorerBlur();
    }

    void ShowFilesDefault()
    {
        bool on = AppModel.Engine.FilesIsDefault;
        bool files = FilesDefault.Exe(Locations.Current) != null;
        FilesDefaultSwitch.IsChecked = on;
        FilesDefaultSwitch.IsEnabled = files || on;
        FilesDefaultHint.Text = files ? L.T("settings.filesDefaultHint") : L.T("settings.filesDefaultMissing");
    }

    /// <summary>Folders, drives and Win+E open Files, or Explorer again. Each switch is a restore point of its own.</summary>
    void OnFilesDefault(object sender, RoutedEventArgs e)
    {
        bool on = FilesDefaultSwitch.IsChecked == true;
        var job = AppModel.Run("files-default", "files-default", L.T(on ? "jobs.filesDefaultOn" : "jobs.filesDefaultOff"),
            AppModel.Engine.FilesDefaultSteps(on), async j =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    Toasts.Show(j.State == JobState.Done ? ToastKind.Ok : ToastKind.Error,
                        j.State == JobState.Done ? L.T(on ? "settings.filesDefaultOn" : "settings.filesDefaultOff") : j.Status);
                    ShowFilesDefault();
                });
            });
        if (job == null) ShowFilesDefault();
    }

    void SelectScheme()
    {
        var was = _loading;
        _loading = true;
        (AppModel.Settings.ColorScheme switch { "light" => CardLight, "dark" => CardDark, _ => CardSystem }).IsChecked = true;
        _loading = was;
    }

    void OnScheme(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string scheme } b || scheme == AppModel.Settings.ColorScheme) return;
        if (Window.GetWindow(this) is MainWindow w)
            ThemeManager.SwitchWithReveal(w.Root, w.OverlayHost, b, () =>
            {
                ThemeManager.Set(scheme);
                AppModel.Update(s => s.ColorScheme = scheme);
            });
    }

    void OnLanguage(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        AppModel.Update(s => s.Language = tag);
        LanguageHint.Visibility = Visibility.Visible;
    }

    void ShowUpdate()
    {
        var u = AppModel.Updater.Status;
        var parts = new List<string> { L.T("settings.version", ("version", AppInfo.Version), ("mode", L.T("app.modes." + AppInfo.Mode))) };
        if (u.Latest != null) parts.Add(u.Available ? L.T("settings.updateAvailable", ("version", u.Latest)) : L.T("settings.upToDate"));
        if (u.LastCheck != null) parts.Add(L.T("settings.lastCheck", ("date", u.LastCheck.Value.ToString("g", L.Culture))));
        if (u.Error != null) parts.Add(u.Error);
        if (AppInfo.IsPrerelease) parts.Add(L.T("settings.prerelease"));
        UpdateText.Text = string.Join(" · ", parts);
    }

    async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        await AppModel.Updater.CheckAsync(download: AppModel.Settings.AutoUpdate, CancellationToken.None);
        ShowUpdate();
        var u = AppModel.Updater.Status;
        if (u.Error == null) Toasts.Show(ToastKind.Ok, u.Available ? L.T("settings.updateAvailable", ("version", u.Latest)) : L.T("settings.upToDate"));
    }

    void OnAutoUpdate(object sender, RoutedEventArgs e) => AppModel.Update(s => s.AutoUpdate = AutoUpdate.IsChecked == true);

    void OnPathsChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AppModel.Update(s =>
        {
            s.FilesSettingsPath = FilesPath.Text.Trim().Length > 0 ? FilesPath.Text.Trim() : null;
        });
    }

    void OnChooseFiles(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Title = L.T("settings.filesPath"), Filter = "JSON (*.json)|*.json" };
        if (d.ShowDialog(Window.GetWindow(this)) != true) return;
        FilesPath.Text = d.FileName;
        OnPathsChanged(sender, e);
    }
}

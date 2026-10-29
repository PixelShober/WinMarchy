using System.IO.Compression;
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
            ("settings.folder.zebar", Locations.Current.ZebarPackDir),
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
        ScriptPath.Text = s.PersonalScriptPath ?? "";
        FilesPath.Text = s.FilesSettingsPath ?? "";
        var herdr = AppModel.Engine.Components.Status(ToolCatalog.Herdr);
        HerdrState.Text = herdr.Installed ? L.T("settings.herdrFound", ("path", herdr.Path)) : L.T("settings.herdrMissing");
        ShowFilesDefault();
        FillBrowsers(Browser1, s.Browser);
        FillBrowsers(Browser2, s.Browser2);
        (s.ScreenshotTool == Core.Platform.Screenshots.Greenshot ? ShotGreenshot : ShotWindows).IsChecked = true;
        ShowScreenshotHint();
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
            s.PersonalScriptPath = ScriptPath.Text.Trim().Length > 0 ? ScriptPath.Text.Trim() : null;
            s.FilesSettingsPath = FilesPath.Text.Trim().Length > 0 ? FilesPath.Text.Trim() : null;
        });
    }

    void OnChooseScript(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Title = L.T("settings.personalScript"), Filter = L.T("settings.scriptFilter") };
        if (d.ShowDialog(Window.GetWindow(this)) != true) return;
        ScriptPath.Text = d.FileName;
        OnPathsChanged(sender, e);
    }

    void OnChooseFiles(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Title = L.T("settings.filesPath"), Filter = "JSON (*.json)|*.json" };
        if (d.ShowDialog(Window.GetWindow(this)) != true) return;
        FilesPath.Text = d.FileName;
        OnPathsChanged(sender, e);
    }

    /// <summary>Herdr from a zip the user downloaded (as Winarchy Lite did): checked entries, exactly one herdr.exe.</summary>
    void OnHerdr(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Title = L.T("settings.herdrInstall"), Filter = "ZIP (*.zip)|*.zip" };
        if (d.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            using (var zip = ZipFile.OpenRead(d.FileName))
            {
                if (zip.Entries.Count(x => x.Name.Equals("herdr.exe", StringComparison.OrdinalIgnoreCase)) != 1)
                    throw new WinMarchyException("herdrZipInvalid");
            }
            var target = ToolCatalog.Herdr.ToolDir;
            if (Directory.Exists(target)) Directory.Delete(target, true);
            FileSafety.ExtractZip(d.FileName, target, CancellationToken.None);
            AppState.Update(s => { if (!s.InstalledTools.Contains("herdr")) s.InstalledTools.Add("herdr"); });
            Toasts.Show(ToastKind.Ok, L.T("settings.herdrDone"));
            OnShown();
        }
        catch (Exception ex)
        {
            Toasts.Show(ToastKind.Error, Net.Describe(ex));
        }
    }
}

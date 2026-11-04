using System.Diagnostics;
using WinMarchy.Core.Backup;
using WinMarchy.Core.GlazeWm;
using WinMarchy.Core.Hotkeys;
using WinMarchy.Core.Integrations;
using WinMarchy.Core.Themes;
using WinMarchy.Core.Tools;

namespace WinMarchy.Core.Services;

/// <summary>Which programs the full setup installs if they are missing.</summary>
public sealed record SetupOptions(bool InstallGlazeWm, bool InstallBar, bool InstallAutoHotkey, bool InstallFlow, bool DownloadWallpapers)
{
    /// <summary>Ticked extras: PowerShell 7, Starship, Fastfetch, Herdr, Pi … (tool ids).</summary>
    public IReadOnlyCollection<string> Extras { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Everything that changes the system, as step lists for the job manager: setup, theme, hotkeys and
/// undo. Each job starts its own restore point before the first change.
/// </summary>
public sealed class Engine
{
    readonly Locations _loc;
    readonly Func<UserSettings> _settings;
    readonly Action<UserSettings> _save;

    public Engine(Locations loc, BackupStore backups, Func<UserSettings> settings, Action<UserSettings> save, IImageConverter? images)
    {
        _loc = loc;
        Backups = backups;
        _settings = settings;
        _save = save;
        Images = images;
        Components = new Components(loc, settings);
    }

    public BackupStore Backups { get; }
    public Components Components { get; }
    public IImageConverter? Images { get; }

    /// <summary>The open GlazeWM connection of the agent, if any (for reloads).</summary>
    public Func<GlazeIpc?> Glaze { get; set; } = () => null;

    UserSettings S => _settings();

    OmarchyTheme CurrentTheme => ThemeCatalog.Find(S.Theme) ?? ThemeCatalog.Default;

    IntegrationContext Context(RestorePoint point, SetupStep step) =>
        new(_loc, Backups, point, S, text => step.Report(text)) { Images = Images };

    /// <summary>A step that only opens the restore point (so the job shows where its backup is).</summary>
    SetupStep BeginStep(string kind, string title, Action<RestorePoint> set) =>
        new(L.T("steps.restorePoint"), (step, _) =>
        {
            var point = Backups.Begin(kind, title);
            set(point);
            step.Report(L.T("steps.restorePointCreated", ("id", point.Id)), log: true);
            return Task.CompletedTask;
        });

    // ------------------------------------------------------------------ GlazeWM

    public string GlazeConfigText(Palette palette, bool gapsOff = false)
    {
        var s = S;
        var options = GlazeConfig.FromSettings(s, palette, Bars.Height);
        if (gapsOff)
        {
            options.InnerGap = 0;
            options.OuterGap = 0;
        }
        return GlazeConfig.Generate(options);
    }

    public void WriteGlazeConfig(IntegrationContext ctx, Palette palette, bool gapsOff = false)
    {
        ctx.WriteFile(_loc.GlazeWmConfig, GlazeConfigText(palette, gapsOff));
        ctx.Report(L.T("integrations.glazewm.done", ("path", _loc.GlazeWmConfig)));
    }

    /// <summary>Gaps, borders and title bars from the Tiling page: a new GlazeWM config in its own restore point.</summary>
    public async Task ApplyTilingAsync(CancellationToken ct)
    {
        var point = Backups.Begin("tiling", L.T("jobs.tiling"));
        var step = new SetupStep(L.T("steps.glazewm"), (_, _) => Task.CompletedTask);
        var ctx = Context(point, step);
        WriteGlazeConfig(ctx, CurrentTheme.Palette);
        await Components.ReloadGlazeAsync(Glaze(), ct);
        WriteBarConfig(ctx);
    }

    /// <summary>What YASB's config depends on: Fastfetch for the logo menu, Windows' location switch for the Wi-Fi menu.</summary>
    YasbOptions BarOptions => new(Components.Status(ToolCatalog.Fastfetch).Installed, Platform.LocationAccess.DesktopAppsAllowed());

    /// <summary>The bar's entries and shortcuts. YASB reads its config again by itself.</summary>
    void WriteBarConfig(IntegrationContext ctx) => YasbBar.WriteConfig(ctx, Components.SelfExe, BarOptions);

    /// <summary>
    /// Omarchy's "window gaps" toggle: gaps off until it is pressed again. No restore point of its own;
    /// the config file is already in the setup's point, and the next toggle writes it back.
    /// </summary>
    public async Task SetGapsAsync(bool off, CancellationToken ct)
    {
        FileSafety.WriteText(_loc.GlazeWmConfig, GlazeConfigText(CurrentTheme.Palette, gapsOff: off));
        await Components.ReloadGlazeAsync(Glaze(), ct);
    }

    // ------------------------------------------------------------------ hotkeys

    /// <summary>
    /// With Greenshot, Windows' own Win+Shift+S (Snipping Tool) goes to Greenshot too, unless the
    /// user put something else on those keys.
    /// </summary>
    static IReadOnlyList<HotkeyBinding>? Extra(UserSettings s) => s.ScreenshotTool == Platform.Screenshots.Greenshot
        ? new[] { new HotkeyBinding { Keys = "Win+Shift+S", Do = "do:screenshot", Group = "utilities", Label = LocalizedText.Same(L.T("hotkeys.greenshotRegion")) } }
        : null;

    /// <summary>With Greenshot, its Print keys belong to it.</summary>
    static IReadOnlyCollection<string>? LeaveAlone(UserSettings s) =>
        s.ScreenshotTool == Platform.Screenshots.Greenshot ? Platform.Screenshots.GreenshotKeys.ToHashSet() : null;

    public HotkeySet CurrentHotkeys() => HotkeySet.Build(CustomHotkeys.Load(Paths.HotkeysFile));

    async Task ApplyHotkeysAsync(IntegrationContext ctx, SetupStep step, CancellationToken ct)
    {
        var s = S;
        var set = CurrentHotkeys();
        if (set.HasBlockingProblems)
            throw new WinMarchyException("hotkeysInvalid", ("detail", string.Join("; ", set.Problems.Where(p => p.Blocking).Select(p => $"{p.Keys}: {p.Message}"))));

        if (Components.AhkExe != null)
        {
            var glazeCli = ToolCatalog.GlazeCli(Components.GlazeExe) ?? "";
            var script = AhkScript.Generate(set, new AhkContext(Components.SelfExe, glazeCli, s.WinKeyOpensLauncher, LeaveAlone: LeaveAlone(s), Extra: Extra(s)));
            step.Report(L.T("hotkeys.validating"));
            var (ok, output) = await Components.ValidateAhkAsync(script, ct);
            if (!ok) throw new WinMarchyException("ahkInvalid", ("detail", output));
            ctx.WriteFile(Paths.AhkScript, script, bom: true);
            WriteGlazeConfig(ctx, CurrentTheme.Palette);
            await Components.ReloadGlazeAsync(Glaze(), ct);
            Components.StartAhk();
            step.Report(L.T("hotkeys.activeAhk", ("count", set.Active.Count)));
        }
        else
        {
            // Only AutoHotkey catches the keys; without it no shortcut works until it is installed.
            Components.StopAhk();
            WriteGlazeConfig(ctx, CurrentTheme.Palette);
            await Components.ReloadGlazeAsync(Glaze(), ct);
            step.Report(L.T("hotkeys.ahkMissing"));
            Log.Warn(L.T("hotkeys.ahkMissing"));
        }
    }

    public List<SetupStep> HotkeySteps()
    {
        RestorePoint? point = null;
        return new List<SetupStep>
        {
            BeginStep("hotkeys", L.T("jobs.hotkeys"), p => point = p),
            new(L.T("steps.hotkeys"), (step, ct) => ApplyHotkeysAsync(Context(point!, step), step, ct)),
        };
    }

    // ------------------------------------------------------------------ screenshots

    /// <summary>
    /// Switches between Snipping Tool and Greenshot: Greenshot comes from its vendor if it is missing
    /// and is set up (<see cref="GreenshotStep"/>); back to Windows, Greenshot's captures no longer go
    /// to the clipboard by force and the hotkey script takes Print again.
    /// </summary>
    /// <param name="byUser">Chosen in the settings: WinMarchy no longer picks Greenshot by itself.</param>
    public List<SetupStep> ScreenshotToolSteps(string tool, bool byUser)
    {
        RestorePoint? point = null;
        var greenshot = tool == Platform.Screenshots.Greenshot;
        var steps = new List<SetupStep> { BeginStep("screenshots", L.T("jobs.screenshots"), p => point = p) };
        if (greenshot)
        {
            steps.AddRange(InstallSteps(new[] { ToolCatalog.Greenshot }));
            steps.Add(GreenshotStep(() => point!));
        }
        else
        {
            steps.Add(new SetupStep(L.T("steps.greenshotFixed"), (step, _) =>
            {
                if (Components.Status(ToolCatalog.Greenshot).Path is { } exe) WriteGreenshotFixed(Context(point!, step), exe, clipboard: false);
                return Task.CompletedTask;
            }, () => Components.Status(ToolCatalog.Greenshot).Installed ? null : L.T("steps.notInstalled", ("tool", ToolCatalog.Greenshot.Name))));
        }
        steps.Add(new SetupStep(L.T("steps.saveChoice"), (step, _) =>
        {
            var s = S.Clone();
            s.ScreenshotTool = greenshot ? Platform.Screenshots.Greenshot : Platform.Screenshots.Windows;
            if (byUser) s.ScreenshotToolChosen = true;
            _save(s);
            return Task.CompletedTask;
        }));
        steps.Add(new SetupStep(L.T("steps.hotkeys"), (step, ct) => ApplyHotkeysAsync(Context(point!, step), step, ct)));
        return steps;
    }

    /// <summary>
    /// Greenshot as the screenshot tool: Windows' own Print → Snipping Tool is switched off (it would
    /// take the key first), every capture goes straight to the clipboard (WinMarchy then offers to save
    /// it), and Greenshot is started again so it reads that. Unless the user chose Snipping Tool on
    /// purpose, Greenshot becomes the choice. The hotkey script, applied after this, leaves its keys alone.
    /// </summary>
    SetupStep GreenshotStep(Func<RestorePoint> pointOf) => new(L.T("steps.greenshot"), (step, _) =>
    {
        var exe = Components.Status(ToolCatalog.Greenshot).Path ?? throw new WinMarchyException("toolNotFoundAfterInstall", ("tool", ToolCatalog.Greenshot.Name));
        var ctx = Context(pointOf(), step);
        ctx.WriteRegistry(@"Control Panel\Keyboard", "PrintScreenKeyForSnippingEnabled", RegistryData.Dword(0));
        step.Report(L.T("screenshot.printKeyFreed"));
        WriteGreenshotFixed(ctx, exe, clipboard: true);
        if (!S.ScreenshotToolChosen || S.ScreenshotTool == Platform.Screenshots.Greenshot)
        {
            var s = S.Clone();
            s.ScreenshotTool = Platform.Screenshots.Greenshot;
            _save(s);
        }
        return Task.CompletedTask;
    });

    /// <summary>Greenshot-fixed.ini with or without "every capture to the clipboard"; Greenshot reads it when it starts.</summary>
    void WriteGreenshotFixed(IntegrationContext ctx, string exe, bool clipboard)
    {
        var path = Platform.Screenshots.GreenshotFixedIni(exe, _loc);
        var original = File.Exists(path) ? FileSafety.ReadShared(path) : null;
        var text = Platform.Screenshots.GreenshotFixed(original, clipboard);
        if (text == original) return;
        if (text != null) ctx.WriteFile(path, text);
        else if (original != null)
        {
            Backups.CaptureFile(ctx.Point, path);
            File.Delete(path);
        }
        ctx.Report(L.T(clipboard ? "screenshot.toClipboard" : "screenshot.notToClipboard", ("path", path)));
        // Greenshot reads its settings when it starts.
        bool running = Components.IsRunning("Greenshot");
        foreach (var p in Process.GetProcessesByName("Greenshot")) using (p) { try { p.Kill(); p.WaitForExit(3000); } catch { } }
        if (running || clipboard) Platform.AppLauncher.Start(exe);
    }

    // ------------------------------------------------------------------ themes

    // ------------------------------------------------------------------ Files as default

    public bool FilesIsDefault => FilesDefault.IsOn(Backups.Registry);

    public List<SetupStep> FilesDefaultSteps(bool on)
    {
        RestorePoint? point = null;
        return new List<SetupStep>
        {
            BeginStep("files-default", L.T(on ? "jobs.filesDefaultOn" : "jobs.filesDefaultOff"), p => point = p),
            new(L.T("steps.filesDefault"), (step, _) =>
            {
                FilesDefault.Apply(Context(point!, step), on);
                Win32.AssociationsChanged();
                return Task.CompletedTask;
            }),
        };
    }

    /// <summary>
    /// At startup: if Files was uninstalled while it is the default, folders would no longer open.
    /// Then Explorer takes over again (the change is in its own restore point).
    /// </summary>
    public void CheckFilesDefault()
    {
        if (!FilesIsDefault) return;
        var target = FilesDefault.Target(Backups.Registry);
        if (target != null && File.Exists(target)) return;
        var point = Backups.Begin("files-default", L.T("jobs.filesDefaultOff"));
        FilesDefault.Apply(Context(point, new SetupStep("files", (_, _) => Task.CompletedTask)), on: false);
        Win32.AssociationsChanged();
        Log.Warn(L.T("integrations.filesDefault.gone"));
    }

    public List<SetupStep> ThemeSteps(OmarchyTheme theme, bool downloadWallpapers)
    {
        RestorePoint? point = null;
        var steps = new List<SetupStep> { BeginStep("theme", L.T("jobs.theme", ("theme", theme.Title)), p => point = p) };
        steps.AddRange(ThemeChanges(theme, downloadWallpapers, () => point!));
        return steps;
    }

    /// <summary>The theme steps without their own restore point (the full setup shares its point).</summary>
    List<SetupStep> ThemeChanges(OmarchyTheme theme, bool downloadWallpapers, Func<RestorePoint> pointOf)
    {
        var targets = S.ThemeTargets;
        var palette = theme.Palette;
        IntegrationContext Ctx(SetupStep step) => Context(pointOf(), step);
        return new List<SetupStep>
        {
            new(L.T("steps.themeAssets"), async (step, ct) =>
            {
                using var http = Net.CreateClient(TimeSpan.FromMinutes(10));
                await ThemeAssets.DownloadAsync(http, theme, downloadWallpapers && targets.Wallpaper, t => step.Report(t), ct);
            }),
            new(L.T("steps.glazewm"), async (step, ct) =>
            {
                WriteGlazeConfig(Ctx(step), palette);
                await Components.ReloadGlazeAsync(Glaze(), ct);
            }, () => targets.GlazeWm ? null : L.T("steps.off")),
            new(L.T("steps.barTheme"), (step, _) =>
            {
                YasbBar.ApplyTheme(Ctx(step), palette);
                return Task.CompletedTask;
            }, () => targets.Bar ? null : L.T("steps.off")),
            new(L.T("steps.terminal"), (step, _) => { TerminalTheme.Apply(Ctx(step), palette); return Task.CompletedTask; },
                () => targets.Terminal ? null : L.T("steps.off")),
            new(L.T("steps.powershell"), (step, _) => { TerminalTheme.ApplyPowerShell(Ctx(step), palette); return Task.CompletedTask; },
                () => targets.PowerShell ? null : L.T("steps.off")),
            new(L.T("steps.vscode"), (step, ct) => VsCodeTheme.ApplyAsync(Ctx(step), theme, ct),
                () => targets.VsCode ? null : L.T("steps.off")),
            new(L.T("steps.flow"), (step, ct) => FlowTheme.ApplyAsync(Ctx(step), palette, Components.Status(ToolCatalog.Flow).Path, ct),
                () => targets.Flow ? null : L.T("steps.off")),
            new(L.T("steps.files"), (step, _) => { FilesTheme.Apply(Ctx(step), palette); return Task.CompletedTask; },
                () => targets.Files ? null : L.T("steps.off")),
            new(L.T("steps.windows"), (step, _) => { WindowsAppearance.Apply(Ctx(step), palette); return Task.CompletedTask; },
                () => targets.Windows ? null : L.T("steps.off")),
            new(L.T("steps.shellTheme"), (step, _) => { ApplyShell(Ctx(step), palette); return Task.CompletedTask; },
                () => !targets.Shell ? L.T("steps.off") : ShellToolPath(ToolCatalog.Starship) != null || ShellToolPath(ToolCatalog.Fastfetch) != null ? null : L.T("steps.notSetUp")),
            new(L.T("steps.explorerBlurTheme"), (step, _) => { ExplorerBlur.ApplyTheme(Ctx(step), palette); return Task.CompletedTask; },
                () => !targets.ExplorerBlur ? L.T("steps.off") : ExplorerBlur.IsOn(Backups.Registry) ? null : L.T("steps.notSetUp")),
            new(L.T("steps.wallpaper"), (step, _) =>
            {
                var index = AppState.Load().WallpaperIndex.GetValueOrDefault(theme.Name);
                Wallpapers.Apply(Ctx(step), theme, index);
                return Task.CompletedTask;
            }, () => targets.Wallpaper ? null : L.T("steps.off")),
            new(L.T("steps.saveTheme"), (step, _) =>
            {
                var s = S.Clone();
                s.Theme = theme.Name;
                _save(s);
                AppState.Update(st => st.LastApplied = DateTime.Now);
                step.Report(L.T("themes.applied", ("theme", theme.Title)));
                return Task.CompletedTask;
            }),
        };
    }

    /// <summary>Win+Ctrl+Space: the next downloaded wallpaper of the current theme.</summary>
    public string? NextWallpaper() => SetWallpaper(null);

    /// <summary>A wallpaper of the current theme by its place in the list (null: the next one).</summary>
    public string? SetWallpaper(int? index)
    {
        var theme = CurrentTheme;
        int i = 0;
        AppState.Update(st => i = st.WallpaperIndex[theme.Name] = index ?? st.WallpaperIndex.GetValueOrDefault(theme.Name) + 1);
        var point = Backups.Reuse("wallpaper", L.T("jobs.wallpaper"), TimeSpan.FromHours(1));
        var step = new SetupStep("wallpaper", (_, _) => Task.CompletedTask);
        return Wallpapers.Apply(Context(point, step), theme, i);
    }

    /// <summary>The wallpaper shown now also on the lock screen (wallpaper menu).</summary>
    public bool WallpaperToLockScreen()
    {
        var files = ThemeAssets.Backgrounds(CurrentTheme);
        if (files.Count == 0) return false;
        var index = AppState.Load().WallpaperIndex.GetValueOrDefault(CurrentTheme.Name);
        var point = Backups.Reuse("lockscreen", L.T("jobs.lockScreen"), TimeSpan.FromHours(1));
        var step = new SetupStep("lockscreen", (_, _) => Task.CompletedTask);
        return Wallpapers.ApplyLockScreen(Context(point, step), files[((index % files.Count) + files.Count) % files.Count]);
    }

    /// <summary>Previews and wallpapers of all 22 themes. Only downloads into C:\WinMarchy\Themes, so no restore point.</summary>
    public SetupStep DownloadAllWallpapersStep() => new(L.T("steps.allWallpapers", ("count", Wallpapers.AllCount())), async (step, ct) =>
    {
        using var http = Net.CreateClient(TimeSpan.FromMinutes(20));
        int done = 0;
        foreach (var theme in ThemeCatalog.All)
        {
            ct.ThrowIfCancellationRequested();
            await ThemeAssets.DownloadAsync(http, theme, backgrounds: true, null, ct);
            step.Report(L.T("themes.allProgress", ("done", ++done), ("total", ThemeCatalog.All.Count), ("theme", theme.Title)));
        }
    }, () => Wallpapers.AllMissingBytes() == 0 ? L.T("themes.allThere") : null);

    // ------------------------------------------------------------------ GlazeWM

    /// <summary>WinMarchy's build of GlazeWM. There is nothing to fall back to: an official release lacks what WinMarchy needs.</summary>
    Task InstallGlazeAsync(SetupStep step, CancellationToken ct) => ToolInstaller.InstallAsync(ToolCatalog.GlazeWm, step, ct);

    /// <summary>
    /// Settings → GlazeWM: WinMarchy's build in place of an official release found on the computer,
    /// started with WinMarchy's configuration. The official one stays where it is but no longer runs.
    /// </summary>
    public List<SetupStep> GlazeForkSteps()
    {
        RestorePoint? point = null;
        return new List<SetupStep>
        {
            BeginStep("glazewm", L.T("jobs.glazeBuild"), p => point = p),
            new(L.T("steps.install", ("tool", ToolCatalog.GlazeWm.Name)), (step, ct) => InstallGlazeAsync(step, ct),
                () => ToolCatalog.IsWinMarchyBuild(Components.GlazeExe) ? L.T("steps.alreadyInstalled", ("path", Components.GlazeExe)) : null),
            new(L.T("steps.glazeStart"), async (step, ct) =>
            {
                WriteGlazeConfig(Context(point!, step), CurrentTheme.Palette);
                if (!Components.StartGlaze()) throw new WinMarchyException("glazeMissing");
                await Components.ReloadGlazeAsync(Glaze(), ct);
            }),
        };
    }

    // ------------------------------------------------------------------ bar

    /// <summary>YASB's config.yaml and styles.css in the theme's colours.</summary>
    SetupStep BarSetupStep(Func<RestorePoint> pointOf) => new(L.T("steps.barSetup"), (step, _) =>
    {
        YasbBar.Install(Context(pointOf(), step), CurrentTheme.Palette, Components.SelfExe, BarOptions);
        return Task.CompletedTask;
    }, () => Components.BarExe == null ? L.T("steps.barMissing", ("tool", Bars.Tool.Name)) : null);

    // ------------------------------------------------------------------ extras

    /// <summary>Starship or Fastfetch as installed by WinMarchy (one found elsewhere is the user's own and stays untouched).</summary>
    string? ShellToolPath(ToolDef tool) => Components.Status(tool) is { InstalledByWinMarchy: true, Path: { } p } ? p : null;

    void ApplyShell(IntegrationContext ctx, Palette palette) =>
        ShellExtras.Apply(ctx, palette, ShellToolPath(ToolCatalog.Starship), ShellToolPath(ToolCatalog.Fastfetch));

    /// <summary>What an extra needs besides its files: Starship's prompt, Fastfetch's colours, ExplorerBlurMica's registration.</summary>
    SetupStep? ExtraSetup(ToolDef tool, Func<RestorePoint> pointOf)
    {
        string? Exe() => Components.Status(tool).Path;
        if (tool == ToolCatalog.Starship || tool == ToolCatalog.Fastfetch)
            return new SetupStep(L.T(tool == ToolCatalog.Starship ? "steps.starship" : "steps.fastfetch"), (step, _) =>
            {
                if (Exe() == null) throw new WinMarchyException("toolNotFoundAfterInstall", ("tool", tool.Name));
                ApplyShell(Context(pointOf(), step), CurrentTheme.Palette);
                // With Fastfetch the logo menu offers the system information.
                if (tool == ToolCatalog.Fastfetch) YasbBar.WriteConfig(Context(pointOf(), step), Components.SelfExe, BarOptions);
                return Task.CompletedTask;
            });
        // Installed, Greenshot takes over the screenshots (unless Snipping Tool was chosen on purpose).
        if (tool == ToolCatalog.Greenshot) return GreenshotStep(pointOf);
        if (tool == ToolCatalog.ExplorerBlurMica)
            return new SetupStep(L.T("steps.explorerBlur"), (step, _) =>
            {
                ExplorerBlur.Enable(Context(pointOf(), step), Exe() ?? throw new WinMarchyException("toolNotFoundAfterInstall", ("tool", tool.Name)), CurrentTheme.Palette);
                return Task.CompletedTask;
            });
        return null;
    }

    /// <summary>Extras later on (Setup page), in a restore point of their own: the files, then what each one needs.</summary>
    public List<SetupStep> ExtraSteps(IEnumerable<ToolDef> tools)
    {
        RestorePoint? point = null;
        var list = tools.ToList();
        var steps = new List<SetupStep> { BeginStep("extras", L.T("jobs.extras", ("tools", string.Join(", ", list.Select(t => t.Name)))), p => point = p) };
        foreach (var tool in list)
        {
            steps.AddRange(InstallSteps(new[] { tool }));
            if (ExtraSetup(tool, () => point!) is { } setup) steps.Add(setup);
        }
        // Greenshot's keys leave the hotkey script.
        if (list.Contains(ToolCatalog.Greenshot)) steps.Add(new SetupStep(L.T("steps.hotkeys"), (step, ct) => ApplyHotkeysAsync(Context(point!, step), step, ct)));
        return steps;
    }

    public bool ExplorerBlurIsOn => ExplorerBlur.IsOn(Backups.Registry);

    /// <summary>Settings → ExplorerBlurMica: downloaded if needed and registered for this user, or registered no more.</summary>
    public List<SetupStep> ExplorerBlurSteps(bool on)
    {
        RestorePoint? point = null;
        var steps = new List<SetupStep> { BeginStep("explorer-blur", L.T(on ? "jobs.explorerBlurOn" : "jobs.explorerBlurOff"), p => point = p) };
        if (on)
        {
            steps.AddRange(InstallSteps(new[] { ToolCatalog.ExplorerBlurMica }));
            steps.Add(ExtraSetup(ToolCatalog.ExplorerBlurMica, () => point!)!);
        }
        else
        {
            steps.Add(new SetupStep(L.T("steps.explorerBlurOff"), (step, _) =>
            {
                ExplorerBlur.Disable(Context(point!, step));
                return Task.CompletedTask;
            }));
        }
        return steps;
    }

    // ------------------------------------------------------------------ program updates

    /// <summary>
    /// Start → Update: the given programs (all WinMarchy can update if none are given), each from its
    /// vendor's latest release if that is newer (<see cref="ToolUpdates.Updatable"/>). Programs that
    /// run are ended for the swap and started again; a terminal or agent that is open keeps its
    /// program until it is closed. Only C:\WinMarchy\Tools changes, so no restore point.
    /// </summary>
    public List<SetupStep> ToolUpdateSteps(IEnumerable<ToolDef>? only = null)
    {
        var tools = ToolUpdates.Updatable(AppState.Load());
        if (only != null) tools = tools.Intersect(only).ToList();
        return tools
            .Select(t => new SetupStep(L.T("steps.update", ("tool", t.Name)), (step, ct) => UpdateToolAsync(t, step, ct)))
            .ToList();
    }

    /// <summary>
    /// At every start of WinMarchy, GlazeWM as WinMarchy needs it: an official build found in place
    /// of WinMarchy's is replaced by it (<see cref="GlazeForkSteps"/>), and with
    /// <paramref name="updates"/> WinMarchy's build is updated when a newer release is out. Null when
    /// there is nothing to do. Asks GitHub, so it belongs in the background.
    /// </summary>
    public async Task<(string Title, List<SetupStep> Steps)?> GlazeUpkeepAsync(bool updates, CancellationToken ct)
    {
        var glaze = ToolCatalog.GlazeWm;
        var status = Components.Status(glaze);
        if (!ToolCatalog.IsWinMarchyBuild(status.Path)) return (L.T("jobs.glazeBuild"), GlazeForkSteps());
        if (!updates || await ToolInstaller.NewerReleaseAsync(glaze, status.Version, ct) is not { } release) return null;
        return (L.T("jobs.glazeUpdate", ("version", release.Tag)),
            new List<SetupStep> { new(L.T("steps.update", ("tool", glaze.Name)), (step, ct2) => UpdateToolAsync(glaze, step, ct2, release)) });
    }

    /// <param name="known">The latest release, if the caller looked it up already.</param>
    async Task UpdateToolAsync(ToolDef tool, SetupStep step, CancellationToken ct, ReleaseInfo? known = null)
    {
        var status = Components.Status(tool);
        try
        {
            step.Report(L.T("tools.lookup", ("tool", tool.Name)));
            // An official GlazeWM in WinMarchy's folder (from older versions) is replaced by WinMarchy's build, whatever its version.
            bool replace = tool == ToolCatalog.GlazeWm && !ToolCatalog.IsWinMarchyBuild(status.Path);
            var release = known ?? await ToolInstaller.LatestReleaseAsync(tool, ct);
            if (release == null) return;
            ToolUpdates.Remember(tool, release.Tag);
            if (!replace && ToolInstaller.IsCurrent(release.Tag, AppState.Load().ToolTags.GetValueOrDefault(tool.Id), status.Version))
            {
                step.Report(L.T("tools.upToDate", ("tool", tool.Name), ("version", status.Version ?? "?")));
                return;
            }
            if (tool == ToolCatalog.ExplorerBlurMica && ExplorerBlurIsOn)
            {
                step.Report(L.T("tools.updateInUse", ("tool", tool.Name)));
                return;
            }
            var process = Path.GetFileNameWithoutExtension(tool.Exe);
            bool wasRunning = Components.IsRunning(process);
            switch (tool.Id)
            {
                case "glazewm":
                    break;      // the installer ends GlazeWM itself, so its hidden windows come back
                case "yasb":
                    Components.StopBar();
                    break;
                case "autohotkey":
                    Components.StopAhk();
                    break;
                case "greenshot":
                    foreach (var p in Process.GetProcessesByName(process)) using (p) { try { p.Kill(); p.WaitForExit(3000); } catch { } }
                    break;
                default:
                    if (wasRunning)
                    {
                        step.Report(L.T("tools.updateRunning", ("tool", tool.Name)));
                        return;
                    }
                    break;
            }
            try
            {
                await ToolInstaller.InstallAsync(tool, step, ct, release);
            }
            finally
            {
                // Whatever happened, what ran before runs again.
                switch (tool.Id)
                {
                    case "glazewm":
                        Components.StartGlaze();
                        break;
                    case "yasb" when S.ShowBar && wasRunning:
                        Components.StartBar();
                        break;
                    case "autohotkey":
                        Components.StartAhk();
                        break;
                    case "greenshot" when wasRunning && Components.Status(tool).Path is { } exe:
                        Platform.AppLauncher.Start(exe);
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One program that cannot be had does not hold up the others.
            step.Report(L.T("tools.updateFailed", ("tool", tool.Name), ("detail", Net.Describe(ex))));
            Log.Warn(L.T("tools.updateFailed", ("tool", tool.Name), ("detail", Net.Describe(ex))));
        }
    }

    // ------------------------------------------------------------------ full setup

    /// <summary>Extras later on, without running the whole setup again. Only C:\WinMarchy\Tools changes.</summary>
    public List<SetupStep> InstallSteps(IEnumerable<ToolDef> tools) => tools
        .Select(tool => new SetupStep(L.T("steps.install", ("tool", tool.Name)), async (step, ct) => { await ToolInstaller.InstallAsync(tool, step, ct); },
            () => Components.Status(tool).Installed ? L.T("steps.alreadyInstalled", ("path", Components.Status(tool).Path)) : null))
        .ToList();

    public List<SetupStep> SetupSteps(SetupOptions o)
    {
        RestorePoint? point = null;
        var steps = new List<SetupStep> { BeginStep("setup", L.T("jobs.setup"), p => point = p) };

        void Install(ToolDef tool, bool wanted) => steps.Add(new SetupStep(L.T("steps.install", ("tool", tool.Name)),
            async (step, ct) => await ToolInstaller.InstallAsync(tool, step, ct),
            () => !wanted ? L.T("steps.notChosen")
                : Components.Status(tool).Installed ? L.T("steps.alreadyInstalled", ("path", Components.Status(tool).Path)) : null));

        steps.Add(new SetupStep(L.T("steps.install", ("tool", ToolCatalog.GlazeWm.Name)),
            (step, ct) => InstallGlazeAsync(step, ct),
            () => !o.InstallGlazeWm ? L.T("steps.notChosen")
                : ToolCatalog.IsWinMarchyBuild(Components.GlazeExe) ? L.T("steps.alreadyInstalled", ("path", Components.GlazeExe)) : null));
        Install(Bars.Tool, o.InstallBar);
        Install(ToolCatalog.AutoHotkey, o.InstallAutoHotkey);
        Install(ToolCatalog.Flow, o.InstallFlow);
        foreach (var extra in ToolCatalog.Extras)
        {
            Install(extra, o.Extras.Contains(extra.Id));
            if (o.Extras.Contains(extra.Id) && ExtraSetup(extra, () => point!) is { } setup) steps.Add(setup);
        }

        steps.Add(new SetupStep(L.T("steps.legacy"), (step, _) =>
        {
            var done = Components.RetireLegacy(Backups, point!);
            step.Report(done.Count == 0 ? L.T("legacy.none") : string.Join(" · ", done));
            ImportLegacyThemes(step);
            return Task.CompletedTask;
        }));
        steps.Add(BarSetupStep(() => point!));
        steps.Add(new SetupStep(L.T("steps.glazeStart"), async (step, ct) =>
        {
            WriteGlazeConfig(Context(point!, step), CurrentTheme.Palette);
            if (!Components.StartGlaze()) throw new WinMarchyException("glazeMissing");
            await Components.ReloadGlazeAsync(Glaze(), ct);
        }));
        steps.Add(new SetupStep(L.T("steps.hotkeys"), (step, ct) => ApplyHotkeysAsync(Context(point!, step), step, ct)));
        steps.Add(new SetupStep(L.T("steps.autostart"), (step, _) =>
        {
            Components.SetAutostart(Backups, point!, S.StartWithWindows);
            step.Report(S.StartWithWindows ? L.T("steps.autostartOn") : L.T("steps.autostartOff"));
            return Task.CompletedTask;
        }));

        // All wallpapers, if wanted: switching themes later then needs no download.
        if (o.DownloadWallpapers) steps.Add(DownloadAllWallpapersStep());
        // The theme runs last: it styles everything that was just installed.
        steps.AddRange(ThemeChanges(CurrentTheme, o.DownloadWallpapers, () => point!));
        steps.Add(new SetupStep(L.T("steps.bar"), (step, _) =>
        {
            Components.StopBar();
            if (S.ShowBar) Components.StartBar();
            AppState.Update(st => st.SetupDone = true);
            return Task.CompletedTask;
        }));
        return steps;
    }

    /// <summary>Previews and wallpapers Winarchy Lite already downloaded are taken over instead of downloading them again.</summary>
    void ImportLegacyThemes(SetupStep step)
    {
        var legacyThemes = Path.Combine(_loc.LegacyStateDir, "themes");
        if (!Directory.Exists(legacyThemes)) return;
        int n = ThemeAssets.ImportFolder(legacyThemes, null);
        var walls = Path.Combine(_loc.LegacyStateDir, "wallpapers");
        if (Directory.Exists(walls))
        {
            foreach (var dir in Directory.EnumerateDirectories(walls))
            {
                var target = Path.Combine(ThemeAssets.Dir(Path.GetFileName(dir)), "backgrounds");
                Directory.CreateDirectory(target);
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    var dest = Path.Combine(target, Path.GetFileName(f));
                    if (!File.Exists(dest)) File.Copy(f, dest);
                }
            }
        }
        if (n > 0) step.Report(L.T("legacy.themesImported", ("count", n)));
    }

    // ------------------------------------------------------------------ undo

    public List<SetupStep> RestoreSteps(RestorePoint point, bool removeTools)
    {
        var steps = new List<SetupStep>
        {
            new(L.T("steps.stopPrograms"), async (step, ct) =>
            {
                // Flow and Files write their settings on exit: stop them before the files go back.
                await FlowTheme.StopAsync(Process.GetProcessesByName("Flow.Launcher"), ct);
                foreach (var p in Process.GetProcessesByName("Files")) { try { p.CloseMainWindow(); } catch { } }
                if (point.IsOriginal)
                {
                    Components.StopAhk();
                    Components.StopBar();
                }
            }),
            new(L.T("steps.restoreFiles"), (step, _) =>
            {
                var results = Backups.Restore(point);
                var failed = results.Count(r => !r.Ok);
                step.Report(L.T("backup.restored", ("count", results.Count - failed), ("failed", failed)));
                if (failed > 0) throw new WinMarchyException("restorePartial", ("failed", failed));
                return Task.CompletedTask;
            }),
            new(L.T("steps.afterRestore"), async (step, ct) =>
            {
                Win32.BroadcastSettingChange("ImmersiveColorSet");
                await Components.ReloadGlazeAsync(Glaze(), ct);
                var flow = Components.Status(ToolCatalog.Flow).Path;
                if (flow != null) Platform.AppLauncher.Start(flow);
                if (point.IsOriginal)
                {
                    var before = AppState.Load().TaskbarAutoHideBefore;
                    Platform.Taskbar.Show();
                    if (before != null) Platform.Taskbar.SetAutoHide(before.Value);
                    AppState.Update(st => { st.TaskbarAutoHideBefore = null; st.SetupDone = false; });
                    var s = S.Clone();
                    s.HideTaskbar = false;
                    _save(s);
                }
            }),
        };
        if (removeTools)
        {
            steps.Add(new SetupStep(L.T("steps.removeTools"), (step, _) =>
            {
                foreach (var id in AppState.Load().InstalledTools.ToList())
                {
                    var tool = ToolCatalog.Get(id);
                    ToolInstaller.Remove(tool, _loc);
                    step.Report(L.T("tools.removed", ("tool", tool.Name)));
                }
                return Task.CompletedTask;
            }));
        }
        return steps;
    }
}

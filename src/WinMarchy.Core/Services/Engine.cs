using System.Diagnostics;
using WinMarchy.Core.Backup;
using WinMarchy.Core.GlazeWm;
using WinMarchy.Core.Hotkeys;
using WinMarchy.Core.Integrations;
using WinMarchy.Core.Themes;
using WinMarchy.Core.Tools;

namespace WinMarchy.Core.Services;

/// <summary>Which programs the full setup installs if they are missing.</summary>
public sealed record SetupOptions(bool InstallGlazeWm, bool InstallZebar, bool InstallAutoHotkey, bool InstallFlow, bool DownloadWallpapers)
{
    /// <summary>Ticked extras: PowerShell 7, Herdr, Pi (tool ids).</summary>
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
        var options = GlazeConfig.FromSettings(s, palette, ZebarPack.BarHeight);
        if (gapsOff)
        {
            options.InnerGap = 0;
            options.OuterGap = 0;
        }
        if (s.HotkeyEngine == "glazewm")
        {
            var set = HotkeySet.Build(s.PersonalProfile, CustomHotkeys.Load(Paths.HotkeysFile), "glazewm");
            options.Keybindings = GlazeConfig.Keybindings(set, KeyboardLayouts.Current, Components.SelfExe, LeaveAlone(s), Extra(s));
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
        WriteGlazeConfig(Context(point, step), CurrentTheme.Palette);
        await Components.ReloadGlazeAsync(Glaze(), ct);
        // The bar's entries: Zebar reads config.js when the bar starts.
        var dir = _loc.ZebarPackDir;
        Backups.CaptureDirectory(point, dir);
        if (ZebarPack.WriteConfig(_loc, Components.SelfExe, S) && Components.IsRunning("zebar"))
        {
            Components.StopBar();
            if (S.ShowBar) Components.StartBar();
        }
    }

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

    public HotkeySet CurrentHotkeys() => HotkeySet.Build(S.PersonalProfile, CustomHotkeys.Load(Paths.HotkeysFile), S.HotkeyEngine);

    async Task ApplyHotkeysAsync(IntegrationContext ctx, SetupStep step, CancellationToken ct)
    {
        var s = S;
        var set = CurrentHotkeys();
        if (set.HasBlockingProblems)
            throw new WinMarchyException("hotkeysInvalid", ("detail", string.Join("; ", set.Problems.Where(p => p.Blocking).Select(p => $"{p.Keys}: {p.Message}"))));

        if (s.HotkeyEngine == "autohotkey" && Components.AhkExe != null)
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
            if (s.HotkeyEngine == "autohotkey") step.Report(L.T("hotkeys.ahkMissingFallback"));
            Components.StopAhk();
            WriteGlazeConfig(ctx, CurrentTheme.Palette);
            await Components.ReloadGlazeAsync(Glaze(), ct);
            step.Report(L.T("hotkeys.activeGlaze", ("count", set.Active.Count)));
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
    /// Switches between Snipping Tool and Greenshot: Greenshot comes from its vendor if it is missing,
    /// Windows' own Print → Snipping Tool is switched off (it would take the key first), and the hotkey
    /// script leaves Greenshot's keys alone. Back to Windows, the script takes Print again.
    /// </summary>
    public List<SetupStep> ScreenshotToolSteps(string tool)
    {
        RestorePoint? point = null;
        var greenshot = tool == Platform.Screenshots.Greenshot;
        var steps = new List<SetupStep> { BeginStep("screenshots", L.T("jobs.screenshots"), p => point = p) };
        if (greenshot)
        {
            steps.AddRange(InstallSteps(new[] { ToolCatalog.Greenshot }));
            steps.Add(new SetupStep(L.T("steps.printKey"), (step, _) =>
            {
                Context(point!, step).WriteRegistry(@"Control Panel\Keyboard", "PrintScreenKeyForSnippingEnabled", RegistryData.Dword(0));
                step.Report(L.T("screenshot.printKeyFreed"));
                if (!Components.IsRunning("Greenshot") && Components.Status(ToolCatalog.Greenshot).Path is { } exe) Platform.AppLauncher.Start(exe);
                return Task.CompletedTask;
            }));
        }
        steps.Add(new SetupStep(L.T("steps.saveChoice"), (step, _) =>
        {
            var s = S.Clone();
            s.ScreenshotTool = greenshot ? Platform.Screenshots.Greenshot : Platform.Screenshots.Windows;
            _save(s);
            return Task.CompletedTask;
        }));
        steps.Add(new SetupStep(L.T("steps.hotkeys"), (step, ct) => ApplyHotkeysAsync(Context(point!, step), step, ct)));
        return steps;
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
            new(L.T("steps.zebar"), (step, _) => { ZebarPack.ApplyTheme(Ctx(step), palette); return Task.CompletedTask; },
                () => targets.Zebar ? null : L.T("steps.off")),
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

    // ------------------------------------------------------------------ GlazeWM build

    /// <summary>
    /// Installs the GlazeWM the settings ask for. If WinMarchy's build cannot be had (no release yet,
    /// GitHub unreachable for that repository), the official one keeps the desktop working.
    /// </summary>
    async Task InstallGlazeAsync(SetupStep step, CancellationToken ct)
    {
        var source = ToolCatalog.GlazeSource(S);
        try
        {
            await ToolInstaller.InstallAsync(source, step, ct);
        }
        catch (Exception ex) when (source == ToolCatalog.GlazeWm &&
                                   (ex is WinMarchyException { Code: "toolAssetMissing" or "releaseTagUnknown" or "toolNoChecksum" } ||
                                    ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound }))
        {
            Log.Warn(L.T("tools.glazeFallback", ("detail", ex.Message)));
            await ToolInstaller.InstallAsync(ToolCatalog.GlazeWmOfficial, step, ct);
        }
    }

    /// <summary>Settings → GlazeWM: install the other build and start it with WinMarchy's configuration.</summary>
    public List<SetupStep> GlazeBuildSteps(string build)
    {
        RestorePoint? point = null;
        return new List<SetupStep>
        {
            BeginStep("glazewm", L.T("jobs.glazeBuild"), p => point = p),
            new(L.T("steps.saveChoice"), (_, _) =>
            {
                var s = S.Clone();
                s.GlazeBuild = build == GlazeBuilds.Official ? GlazeBuilds.Official : GlazeBuilds.WinMarchy;
                _save(s);
                return Task.CompletedTask;
            }),
            new(L.T("steps.install", ("tool", ToolCatalog.GlazeWm.Name)), (step, ct) => InstallGlazeAsync(step, ct),
                () => ToolCatalog.IsWantedGlaze(Components.GlazeExe, S) ? L.T("steps.alreadyInstalled", ("path", Components.GlazeExe)) : null),
            new(L.T("steps.glazeStart"), async (step, ct) =>
            {
                WriteGlazeConfig(Context(point!, step), CurrentTheme.Palette);
                if (!Components.StartGlaze()) throw new WinMarchyException("glazeMissing");
                await Components.ReloadGlazeAsync(Glaze(), ct);
            }),
        };
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
                : ToolCatalog.IsWantedGlaze(Components.GlazeExe, S) ? L.T("steps.alreadyInstalled", ("path", Components.GlazeExe)) : null));
        Install(ToolCatalog.Zebar, o.InstallZebar);
        Install(ToolCatalog.AutoHotkey, o.InstallAutoHotkey);
        Install(ToolCatalog.Flow, o.InstallFlow);
        foreach (var extra in ToolCatalog.Extras) Install(extra, o.Extras.Contains(extra.Id));

        steps.Add(new SetupStep(L.T("steps.legacy"), (step, _) =>
        {
            var done = Components.RetireLegacy(Backups, point!);
            step.Report(done.Count == 0 ? L.T("legacy.none") : string.Join(" · ", done));
            ImportLegacyThemes(step);
            return Task.CompletedTask;
        }));
        steps.Add(new SetupStep(L.T("steps.zebarPack"), (step, _) =>
        {
            ZebarPack.Install(Context(point!, step), CurrentTheme.Palette, Components.SelfExe);
            return Task.CompletedTask;
        }, () => Components.ZebarExe == null ? L.T("steps.zebarMissing") : null));
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

using System.Windows;
using System.Windows.Controls;
using WinMarchy.App.Ui;
using WinMarchy.Core;
using WinMarchy.Core.Hotkeys;

namespace WinMarchy.App.Pages;

public partial class HotkeysPage : UserControl, IPage
{
    static readonly string[] GroupIds = { "all", "favorites", "tiling", "workspaces", "apps", "menus", "utilities", "system" };
    static readonly string[] Kinds = { "wm", "app", "web", "open", "send", "do", "run" };

    string _group = "all";
    HotkeySet? _set;
    CustomHotkeys _custom = new();
    HotkeyBinding? _editing;
    bool _loading;

    public HotkeysPage()
    {
        InitializeComponent();
        foreach (var g in GroupIds)
        {
            var b = new RadioButton { Style = UiKit.S("Segment"), GroupName = "hkgroup", Content = L.T("hotkeys.groups." + g), Tag = g, IsChecked = g == "all" };
            b.Checked += (_, _) => { _group = g; Render(); };
            Groups.Children.Add(b);
        }
        foreach (var k in Kinds) EditKind.Items.Add(new ComboBoxItem { Content = L.T("hotkeys.kinds." + k), Tag = k });
        if (AppModel.Jobs.Get("hotkeys") != null) Job.Follow("hotkeys");
    }

    public void OnShown()
    {
        _loading = true;
        var s = AppModel.Settings;
        (s.HotkeyEngine == "glazewm" ? EngineGlaze : EngineAhk).IsChecked = true;
        WinAlone.IsChecked = s.WinKeyOpensLauncher;
        Personal.IsChecked = s.PersonalProfile;
        _loading = false;
        UpdateEngineText();
        Reload();
    }

    void Reload()
    {
        _custom = CustomHotkeys.Load(Paths.HotkeysFile);
        _set = AppModel.Engine.CurrentHotkeys();
        var problems = _set.Problems.Select(p => $"{p.Keys}: {p.Message}").Distinct().ToList();
        Problems.Visibility = problems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ProblemsText.Text = string.Join("\n", problems);
        Render();
    }

    void UpdateEngineText()
    {
        var ahk = AppModel.Settings.HotkeyEngine != "glazewm";
        EngineText.Text = ahk ? L.T("hotkeys.engineAhkText") : L.T("hotkeys.engineGlazeText");
        WinAlone.IsEnabled = ahk;
        if (ahk && AppModel.Engine.Components.AhkExe == null) EngineText.Text += " " + L.T("hotkeys.ahkNotInstalled");
    }

    void OnEngine(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string engine }) return;
        AppModel.Update(s => s.HotkeyEngine = engine);
        UpdateEngineText();
        Reload();
    }

    void OnWinAlone(object sender, RoutedEventArgs e) => AppModel.Update(s => s.WinKeyOpensLauncher = WinAlone.IsChecked == true);

    void OnPersonal(object sender, RoutedEventArgs e)
    {
        AppModel.Update(s => s.PersonalProfile = Personal.IsChecked == true);
        Reload();
    }

    void OnSearch(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Render();
    }

    // ------------------------------------------------------------------ list

    void Render()
    {
        if (_set == null) return;
        List.Children.Clear();
        var query = Search.Text.Trim();
        var off = new HashSet<string>(_custom.Disabled.Select(Normalize), StringComparer.OrdinalIgnoreCase);
        var shown = _set.All
            .Where(b => _set.Active.Contains(b) || off.Contains(b.Keys) || (b.Source == "custom" && !b.Enabled))
            .Where(b => _group == "all" || b.Group == _group || (_group == "favorites" && b.Favorite))
            .Where(b => query.Length == 0 || b.Keys.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        b.Describe().Contains(query, StringComparison.OrdinalIgnoreCase) || b.Do.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (shown.Count == 0)
        {
            List.Children.Add(UiKit.Text(L.T("hotkeys.empty"), "Muted", new Thickness(20, 18, 20, 18)));
            return;
        }
        string? lastGroup = null;
        foreach (var b in shown.OrderBy(b => Array.IndexOf(GroupIds, b.Group)).ThenBy(b => shown.IndexOf(b)).ToList())
        {
            if (_group is "all" or "favorites" && b.Group != lastGroup)
            {
                lastGroup = b.Group;
                var head = UiKit.Text(L.T("hotkeys.groups." + b.Group), "H3", new Thickness(20, List.Children.Count == 0 ? 16 : 22, 20, 6));
                List.Children.Add(head);
            }
            List.Children.Add(Row(b, off.Contains(b.Keys)));
        }
    }

    static string Normalize(string keys) => KeyCombo.TryParse(keys, out var c, out _) ? c.ToString() : keys;

    FrameworkElement Row(HotkeyBinding b, bool disabled)
    {
        var grid = new Grid { Margin = new Thickness(20, 0, 12, 0), MinHeight = 40 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var keys = UiKit.Keycaps(b.Keys);
        keys.Margin = new Thickness(0, 6, 12, 6);
        grid.Children.Add(keys);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 12, 6) };
        var label = UiKit.Text(b.Describe(), "Body");
        if (disabled)
        {
            label.TextDecorations = TextDecorations.Strikethrough;
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        }
        text.Children.Add(label);
        var notes = new List<string>();
        if (b.Omarchy != null) notes.Add(L.T("hotkeys.omarchy", ("binding", b.Omarchy)));
        if (b.Replaces != null) notes.Add(L.T("hotkeys.replaces", ("label", b.Replaces.Describe())));
        if (b.Note != null) notes.Add(b.Note.ToString());
        if (notes.Count > 0) text.Children.Add(UiKit.Text(string.Join(" · ", notes), "Small"));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var source = UiKit.Badge(L.T("hotkeys.sources." + b.Source), b.Source == "custom" ? "accent" : "muted");
        source.Margin = new Thickness(0, 0, 10, 0);
        Grid.SetColumn(source, 2);
        grid.Children.Add(source);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        // Favourites come first in Win+K, the rest under "Optional".
        var starIcon = new Icon { Kind = "star", Size = 14 };
        starIcon.SetResourceReference(Icon.ForegroundProperty, b.Favorite ? "Accent" : "TextMuted");
        var star = new Button { Style = UiKit.S("IconButton"), Content = starIcon, ToolTip = b.Favorite ? L.T("hotkeys.unstarTip") : L.T("hotkeys.starTip") };
        System.Windows.Automation.AutomationProperties.SetName(star, (string)star.ToolTip);
        star.Click += (_, _) =>
        {
            _custom.Favorites[b.Keys] = !b.Favorite;
            _custom.Save(Paths.HotkeysFile);
            Reload();
        };
        actions.Children.Add(star);
        var edit = new Button { Style = UiKit.S("IconButton"), Content = new Icon { Kind = "pencil", Size = 14 }, ToolTip = L.T("hotkeys.editTip") };
        edit.Click += (_, _) => BeginEdit(b, isNew: false);
        actions.Children.Add(edit);
        if (b.Source == "custom")
        {
            var del = new Button { Style = UiKit.S("IconButton"), Content = new Icon { Kind = "trash", Size = 14 }, ToolTip = L.T("hotkeys.deleteTip") };
            del.Click += (_, _) =>
            {
                _custom.Bindings.RemoveAll(c => Normalize(c.Keys) == b.Keys);
                SaveCustom();
            };
            actions.Children.Add(del);
        }
        else
        {
            var toggle = new Button
            {
                Style = UiKit.S("IconButton"),
                Content = new Icon { Kind = disabled ? "plus" : "minus", Size = 14 },
                ToolTip = disabled ? L.T("hotkeys.enableTip") : L.T("hotkeys.disableTip"),
            };
            toggle.Click += (_, _) =>
            {
                if (disabled) _custom.Disabled.RemoveAll(k => Normalize(k) == b.Keys);
                else _custom.Disabled.Add(b.Keys);
                SaveCustom();
            };
            actions.Children.Add(toggle);
        }
        Grid.SetColumn(actions, 3);
        grid.Children.Add(actions);

        var row = new Border { Child = grid, BorderThickness = new Thickness(0, 1, 0, 0) };
        row.SetResourceReference(Border.BorderBrushProperty, "Border");
        return row;
    }

    /// <summary>Saves and puts the shortcuts into effect right away (AutoHotkey or GlazeWM).</summary>
    void SaveCustom()
    {
        _custom.Save(Paths.HotkeysFile);
        Reload();
        if (AppModel.Engine.CurrentHotkeys().HasBlockingProblems)
        {
            Toasts.Show(ToastKind.Error, L.T("hotkeys.savedWithProblems"));
            return;
        }
        Apply(L.T("hotkeys.savedAndApplied"));
    }

    void OnExport(object sender, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.SaveFileDialog { Title = L.T("hotkeys.export"), FileName = L.T("hotkeys.exportFile") + ".json", Filter = "JSON (*.json)|*.json" };
        if (d.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            CustomHotkeys.Load(Paths.HotkeysFile).Save(d.FileName);
            Toasts.Show(ToastKind.Ok, L.T("hotkeys.exported", ("path", d.FileName)));
        }
        catch (Exception ex)
        {
            Toasts.Show(ToastKind.Error, Net.Describe(ex));
        }
    }

    /// <summary>Replaces the own shortcuts with an exported file; the old file goes into a restore point first.</summary>
    void OnImport(object sender, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFileDialog { Title = L.T("hotkeys.import"), Filter = "JSON (*.json)|*.json" };
        if (d.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var imported = CustomHotkeys.Import(d.FileName);
            var point = AppModel.Engine.Backups.Begin("hotkeys", L.T("jobs.hotkeysImport"));
            AppModel.Engine.Backups.CaptureFile(point, Paths.HotkeysFile);
            _custom = imported;
            SaveCustom();
        }
        catch (Exception ex)
        {
            Toasts.Show(ToastKind.Error, L.T("hotkeys.importFailed", ("detail", Net.Describe(ex))));
        }
    }

    // ------------------------------------------------------------------ editor

    void OnAdd(object sender, RoutedEventArgs e) =>
        BeginEdit(new HotkeyBinding { Keys = "Win+Alt+", Do = "app:terminal", Group = "apps", Source = "custom" }, isNew: true);

    void BeginEdit(HotkeyBinding b, bool isNew)
    {
        _editing = b;
        EditorTitle.Text = isNew ? L.T("hotkeys.edit.newTitle") : L.T("hotkeys.edit.title", ("keys", b.Keys));
        EditKeys.Text = b.Keys;
        EditLabel.Text = b.Label?.ToString() ?? "";
        var action = b.Action ?? new HotkeyAction(ActionKind.App, "terminal");
        var kind = action.Kind.ToString().ToLowerInvariant();
        EditKind.SelectedItem = EditKind.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == kind);
        FillChoices(kind, action.Value);
        EditError.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Visible;
        Motion.FadeIn(Editor, 6);
        EditKeys.Focus();
        EditKeys.CaretIndex = EditKeys.Text.Length;
    }

    void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EditKind.SelectedItem is ComboBoxItem { Tag: string kind }) FillChoices(kind, null);
    }

    void FillChoices(string kind, string? value)
    {
        EditChoice.Items.Clear();
        IEnumerable<string>? choices = kind switch
        {
            "app" => HotkeyAction.Apps,
            "do" => HotkeyAction.Commands,
            _ => null,
        };
        EditValueHint.Text = L.T("hotkeys.kindHints." + kind);
        if (choices != null)
        {
            foreach (var c in choices)
                EditChoice.Items.Add(new ComboBoxItem { Content = kind == "app" ? L.T("apps." + c) : $"{c} – {L.T("commands." + c)}", Tag = c });
            var first = value?.Split(' ')[0];
            EditChoice.SelectedItem = EditChoice.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == first) ?? EditChoice.Items[0];
            EditChoice.Visibility = Visibility.Visible;
            EditValue.Visibility = Visibility.Collapsed;
        }
        else
        {
            EditValue.Text = value ?? "";
            EditChoice.Visibility = Visibility.Collapsed;
            EditValue.Visibility = Visibility.Visible;
        }
    }

    void OnEditSave(object sender, RoutedEventArgs e)
    {
        if (_editing == null || EditKind.SelectedItem is not ComboBoxItem { Tag: string kind }) return;
        var value = EditChoice.Visibility == Visibility.Visible && EditChoice.SelectedItem is ComboBoxItem { Tag: string v } ? v : EditValue.Text.Trim();
        // Commands with an argument keep the argument the profile had ("menu system").
        if (kind == "do" && _editing.Action is { Kind: ActionKind.Do } old && old.Value.StartsWith(value + " ")) value = old.Value;

        string error;
        if (!KeyCombo.TryParse(EditKeys.Text, out var combo, out error) || (combo.ReservedReason() is { } r && (error = r) != null) ||
            !HotkeyAction.TryParse(kind + ":" + value, out _, out error))
        {
            EditError.Text = error;
            EditError.Visibility = Visibility.Visible;
            return;
        }
        var oldKeys = _editing.Keys;
        _custom.Bindings.RemoveAll(c => Normalize(c.Keys) == combo.ToString() || (_editing.Source == "custom" && Normalize(c.Keys) == Normalize(oldKeys)));
        var label = EditLabel.Text.Trim();
        _custom.Bindings.Add(new HotkeyBinding
        {
            Keys = combo.ToString(),
            Do = kind + ":" + value,
            Group = _editing.Group,
            Label = label.Length > 0 ? LocalizedText.Same(label) : _editing.Label,
        });
        // Moving a profile shortcut to other keys switches the old keys off.
        if (_editing.Source != "custom" && Normalize(oldKeys) != combo.ToString() && !_custom.Disabled.Contains(oldKeys))
            _custom.Disabled.Add(oldKeys);
        Editor.Visibility = Visibility.Collapsed;
        _editing = null;
        SaveCustom();
    }

    void OnEditCancel(object sender, RoutedEventArgs e)
    {
        Editor.Visibility = Visibility.Collapsed;
        _editing = null;
    }

    void OnApply(object sender, RoutedEventArgs e) => Apply(L.T("hotkeys.applied"));

    void Apply(string doneText)
    {
        var job = AppModel.Run("hotkeys", "hotkeys", L.T("jobs.hotkeys"), AppModel.Engine.HotkeySteps(), async j =>
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (j.State == JobState.Done) Toasts.Show(ToastKind.Ok, doneText);
            });
        });
        if (job != null) Job.Follow(job.Id);
    }
}

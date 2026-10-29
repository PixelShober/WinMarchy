namespace WinMarchy.Core.Hotkeys;

public sealed record HotkeyProblem(string Keys, string Message, bool Blocking);

/// <summary>
/// The shortcuts in effect: "Business" (always), "Persönlich" (optional) and the user's own ones, each
/// later layer replacing bindings with the same keys of the earlier ones.
/// </summary>
public sealed class HotkeySet
{
    HotkeySet(List<HotkeyBinding> active, List<HotkeyBinding> all, List<HotkeyProblem> problems)
    {
        Active = active;
        All = all;
        Problems = problems;
    }

    /// <summary>What gets generated, in profile order.</summary>
    public IReadOnlyList<HotkeyBinding> Active { get; }

    /// <summary>Everything, including replaced and switched-off bindings (for the editor).</summary>
    public IReadOnlyList<HotkeyBinding> All { get; }

    public IReadOnlyList<HotkeyProblem> Problems { get; }

    public bool HasBlockingProblems => Problems.Any(p => p.Blocking);

    public static HotkeyProfile LoadProfile(string id)
    {
        using var stream = typeof(HotkeySet).Assembly.GetManifestResourceStream($"WinMarchy.Hotkeys.{id}.json")
            ?? throw new InvalidOperationException($"hotkey profile {id} is not embedded");
        using var reader = new StreamReader(stream);
        var profile = Json.Deserialize<HotkeyProfile>(reader.ReadToEnd()) ?? new HotkeyProfile { Id = id };
        profile.Id = id;
        profile.Bindings = Expand(profile.Bindings).ToList();
        foreach (var b in profile.Bindings) b.Source = id;
        return profile;
    }

    /// <summary>"Win+{n}" with "focus --workspace {ws}" becomes ten bindings, Win+1 … Win+0.</summary>
    internal static IEnumerable<HotkeyBinding> Expand(IEnumerable<HotkeyBinding> bindings)
    {
        foreach (var b in bindings)
        {
            if (b.Repeat != "workspaces")
            {
                yield return b;
                continue;
            }
            for (int ws = 1; ws <= 10; ws++)
            {
                var n = (ws % 10).ToString();
                var copy = b.Copy();
                copy.Repeat = null;
                copy.Template = b.Keys;
                copy.TemplateLabel = b.Label;
                copy.Keys = b.Keys.Replace("{n}", n);
                copy.Do = b.Do.Replace("{ws}", ws.ToString());
                if (b.Label != null)
                    copy.Label = new LocalizedText { De = b.Label.De.Replace("{ws}", ws.ToString()), En = b.Label.En.Replace("{ws}", ws.ToString()) };
                yield return copy;
            }
        }
    }

    public static HotkeySet Build(bool personal, CustomHotkeys custom, string engine)
    {
        var layers = new List<IEnumerable<HotkeyBinding>> { LoadProfile("business").Bindings };
        if (personal) layers.Add(LoadProfile("personal").Bindings);
        layers.Add(custom.Bindings.Select(b => { var c = b.Copy(); c.Source = "custom"; return c; }));
        return Compose(layers, custom.Disabled, engine, custom.Favorites);
    }

    internal static HotkeySet Compose(IEnumerable<IEnumerable<HotkeyBinding>> layers, IEnumerable<string> disabled, string engine,
        IReadOnlyDictionary<string, bool>? favorites = null)
    {
        var problems = new List<HotkeyProblem>();
        var all = new List<HotkeyBinding>();
        var byKeys = new Dictionary<string, HotkeyBinding>(StringComparer.OrdinalIgnoreCase);
        var off = new HashSet<string>(disabled.Select(Normalize), StringComparer.OrdinalIgnoreCase);
        var starred = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (keys, on) in favorites ?? new Dictionary<string, bool>()) starred[Normalize(keys)] = on;

        foreach (var layer in layers)
        {
            foreach (var original in layer)
            {
                var b = original.Copy();
                all.Add(b);
                if (!KeyCombo.TryParse(b.Keys, out var combo, out var keyError))
                {
                    problems.Add(new HotkeyProblem(b.Keys, keyError, b.Source == "custom"));
                    continue;
                }
                b.Keys = combo.ToString();
                b.Favorite = starred.TryGetValue(b.Keys, out var star) ? star : !b.Optional;
                if (combo.ReservedReason() is { } reserved)
                {
                    problems.Add(new HotkeyProblem(b.Keys, reserved, true));
                    continue;
                }
                if (!HotkeyAction.TryParse(b.Do, out _, out var actionError))
                {
                    problems.Add(new HotkeyProblem(b.Keys, actionError, b.Source == "custom"));
                    continue;
                }
                if (combo.IsWheel && engine == "glazewm")
                {
                    problems.Add(new HotkeyProblem(b.Keys, L.T("hotkeys.errors.wheelNeedsAhk"), false));
                    continue;
                }
                if (byKeys.TryGetValue(b.Keys, out var previous))
                {
                    if (previous.Source == b.Source && b.Source != "custom")
                        problems.Add(new HotkeyProblem(b.Keys, L.T("hotkeys.errors.duplicate", ("keys", b.Keys)), false));
                    b.Replaces = previous;
                }
                byKeys[b.Keys] = b;
            }
        }

        var active = byKeys.Values
            .Where(b => b.Enabled && !off.Contains(b.Keys))
            .OrderBy(b => all.IndexOf(b))
            .ToList();
        return new HotkeySet(active, all, problems);
    }

    static string Normalize(string keys) => KeyCombo.TryParse(keys, out var c, out _) ? c.ToString() : keys;
}

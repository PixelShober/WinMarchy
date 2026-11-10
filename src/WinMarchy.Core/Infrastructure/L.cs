using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace WinMarchy.Core;

/// <summary>
/// Translations. All texts of WinMarchy, read from the embedded locale files (Resources/Locales),
/// so a text exists exactly once per language for the app and the core.
/// </summary>
public static class L
{
    static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Tables = LoadTables();

    /// <summary>"de" or "en".</summary>
    public static string Language { get; private set; } = "de";

    /// <summary>Swiss German: no "ß", written as "ss".</summary>
    public static bool Swiss { get; private set; }

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>The language tag as the UI knows it: "de", "de-CH" or "en".</summary>
    public static string Tag => Swiss ? "de-CH" : Language;

    /// <summary>
    /// setting: "auto" (follow the Windows display language), "de", "de-CH" or "en".
    /// </summary>
    public static void Configure(string? setting, CultureInfo? uiCulture = null)
    {
        var (language, swiss) = Resolve(setting, uiCulture ?? CultureInfo.CurrentUICulture);
        Language = language;
        Swiss = swiss;
        Culture = CultureInfo.GetCultureInfo(swiss ? "de-CH" : language == "de" ? "de-DE" : "en-US");
    }

    internal static (string Language, bool Swiss) Resolve(string? setting, CultureInfo uiCulture)
    {
        switch ((setting ?? "auto").Trim().ToLowerInvariant())
        {
            case "de": return ("de", false);
            case "de-ch": return ("de", true);
            case "en": return ("en", false);
        }
        if (uiCulture.TwoLetterISOLanguageName != "de") return ("en", false);
        var name = uiCulture.Name;
        return ("de", name.EndsWith("-CH", StringComparison.OrdinalIgnoreCase) ||
                      name.EndsWith("-LI", StringComparison.OrdinalIgnoreCase));
    }

    public static string T(string key, params (string Name, object? Value)[] args) => Format(Lookup(key), args);

    /// <summary>Like <see cref="T"/>, but null when the key does not exist.</summary>
    public static string? TryT(string key, params (string Name, object? Value)[] args)
    {
        foreach (var lang in new[] { Language, "en" })
            if (Tables.TryGetValue(lang, out var t) && t.TryGetValue(key, out var v)) return Format(v, args);
        return null;
    }

    static string Lookup(string key)
    {
        foreach (var lang in new[] { Language, "en" })
            if (Tables.TryGetValue(lang, out var t) && t.TryGetValue(key, out var v)) return v;
        return key;
    }

    static string Format(string template, (string Name, object? Value)[] args)
    {
        var text = template;
        foreach (var (name, value) in args)
            text = text.Replace("{" + name + "}", value switch
            {
                null => "",
                IFormattable f => f.ToString(null, Culture),
                _ => value.ToString() ?? "",
            });
        return Swiss ? ToSwiss(text) : text;
    }

    /// <summary>Swiss Standard German does not use "ß".</summary>
    public static string ToSwiss(string text) => text.Replace("ß", "ss").Replace("ẞ", "SS");

    internal static IReadOnlyCollection<string> Languages => Tables.Keys;

    internal static IReadOnlyDictionary<string, string> Table(string language) => Tables[language];

    static Dictionary<string, IReadOnlyDictionary<string, string>> LoadTables()
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        var asm = typeof(L).Assembly;
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith("WinMarchy.Locales.", StringComparison.Ordinal) || !name.EndsWith(".json", StringComparison.Ordinal))
                continue;
            var lang = name["WinMarchy.Locales.".Length..^".json".Length];
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var doc = JsonDocument.Parse(reader.ReadToEnd(), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            var flat = new Dictionary<string, string>(StringComparer.Ordinal);
            Flatten(doc.RootElement, "", flat);
            result[lang] = flat;
        }
        return result;
    }

    internal static void Flatten(JsonElement element, string prefix, Dictionary<string, string> into)
    {
        foreach (var p in element.EnumerateObject())
        {
            var key = prefix.Length == 0 ? p.Name : prefix + "." + p.Name;
            if (p.Value.ValueKind == JsonValueKind.Object) Flatten(p.Value, key, into);
            else if (p.Value.ValueKind == JsonValueKind.String) into[key] = p.Value.GetString()!;
        }
    }
}

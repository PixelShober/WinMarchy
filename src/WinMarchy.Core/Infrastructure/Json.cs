using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WinMarchy.Core;

public static class Json
{
    /// <summary>
    /// camelCase, enums as strings, umlauts written as-is (no ü escapes) – both for the
    /// bridge and for files people open in an editor.
    /// </summary>
    public static readonly JsonSerializerOptions Options = Create(indented: false);

    public static readonly JsonSerializerOptions Indented = Create(indented: true);

    static JsonSerializerOptions Create(bool indented)
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = indented,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return o;
    }

    public static string Serialize<T>(T value, bool indented = false) =>
        JsonSerializer.Serialize(value, indented ? Indented : Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    public static T? Deserialize<T>(JsonElement element) => element.Deserialize<T>(Options);

    /// <summary>
    /// Writes an edited node the way the original file looked: same indentation, same line endings,
    /// trailing newline if it had one, umlauts as-is. Key order is kept by JsonObject.
    /// </summary>
    public static string WriteLike(JsonNode node, string original)
    {
        var indent = Regex.Match(original, @"\n([ \t]+)\S") is { Success: true } m ? m.Groups[1].Value : "  ";
        var newline = original.Contains("\r\n") ? "\r\n" : "\n";
        var text = Format(node, indent, newline);
        return original.EndsWith('\n') ? text + newline : text;
    }

    public static string Format(JsonNode node, string indent = "  ", string newline = "\n") =>
        node.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            IndentCharacter = indent[0],
            IndentSize = indent.Length,
            NewLine = newline,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
}

using System.Collections;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PocketBaseNet;

/// <summary>Port of the SDKs <c>pb.filter(expr, params)</c> helper.</summary>
public static class PocketBaseFilter
{
    // Mimics JSON.stringify: only quotes, backslashes and control chars are escaped.
    private static readonly JsonSerializerOptions JsonStringify = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Replaces the <c>{:paramName}</c> placeholders in <paramref name="raw"/> with the escaped parameter values.
    /// Supported values: string, numbers, bool, null, DateTime/DateTimeOffset (PocketBase datetime format);
    /// everything else is JSON serialized (arrays/objects are additionally wrapped as a JSON string).
    /// Placeholders without a matching parameter are left untouched.
    /// </summary>
    public static string Build(string raw, IReadOnlyDictionary<string, object?>? parameters)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (parameters is null || parameters.Count == 0) return raw;

        var withPlaceholders = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (k, v) in parameters) withPlaceholders["{:" + k + "}"] = v;

        var pattern = string.Join("|", withPlaceholders.Keys.Select(Regex.Escape));

        // single pass replacement (avoids chaining issues, as in the official SDKs)
        return Regex.Replace(raw, pattern, m => Stringify(withPlaceholders[m.Value]), RegexOptions.CultureInvariant);
    }

    /// <summary>Same as <see cref="Build(string, IReadOnlyDictionary{string, object?})"/> reading the parameters from a dictionary or the public properties of an object (e.g. anonymous type).</summary>
    public static string Build(string raw, object? parameters) => Build(raw, ToDictionary(parameters));

    internal static IReadOnlyDictionary<string, object?>? ToDictionary(object? parameters)
    {
        switch (parameters)
        {
            case null:
                return null;
            case IReadOnlyDictionary<string, object?> d:
                return d;
            case IDictionary dict:
                var fromDict = new Dictionary<string, object?>();
                foreach (DictionaryEntry e in dict) fromDict[Convert.ToString(e.Key, CultureInfo.InvariantCulture)!] = e.Value;
                return fromDict;
            default:
                var result = new Dictionary<string, object?>();
                foreach (var p in parameters.GetType().GetProperties())
                {
                    if (p.CanRead && p.GetIndexParameters().Length == 0) result[p.Name] = p.GetValue(parameters);
                }
                return result;
        }
    }

    private static string Stringify(object? value)
    {
        switch (value)
        {
            case null:
                return "null";
            case bool b:
                return b ? "true" : "false";
            case string s:
                return JsonSerializer.Serialize(s, JsonStringify);
            case char c:
                return JsonSerializer.Serialize(c.ToString(), JsonStringify);
            case DateTime dt:
                return JsonSerializer.Serialize(PocketBaseDate.Format(dt), JsonStringify);
            case DateTimeOffset dto:
                return JsonSerializer.Serialize(PocketBaseDate.Format(dto), JsonStringify);
            case JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined }:
                return "null";
            case JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number } je:
                return je.GetRawText();
            case JsonElement { ValueKind: JsonValueKind.String } je:
                return JsonSerializer.Serialize(je.GetString(), JsonStringify);
        }

        if (PocketBaseDate.IsNumber(value)) return ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);

        var stringified = JsonSerializer.Serialize(value, value.GetType(), JsonStringify);

        // wrap in double quotes in case of regular array, object, etc.
        return stringified.StartsWith('[') || stringified.StartsWith('{')
            ? JsonSerializer.Serialize(stringified, JsonStringify)
            : stringified;
    }
}

using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace PocketBase.SDK;

/// <summary>Port of the JS SDK <c>serializeQueryParams</c>.</summary>
internal static class PocketBaseQuery
{
    public static string Serialize(IEnumerable<KeyValuePair<string, object?>> query, JsonSerializerOptions json)
    {
        var result = new List<string>();

        foreach (var (key, value) in query)
        {
            var encodedKey = Uri.EscapeDataString(key);

            foreach (var v in Expand(value))
            {
                var prepared = Prepare(v, json);
                if (prepared is null) continue;
                result.Add(encodedKey + "=" + prepared);
            }
        }

        return string.Join("&", result);
    }

    // arrays/lists -> repeated keys (dictionaries and strings are single values)
    private static IEnumerable<object?> Expand(object? value) => value switch
    {
        null or string or IDictionary => [value],
        JsonElement { ValueKind: JsonValueKind.Array } je => je.EnumerateArray().Select(e => (object?)e).ToList(),
        JsonElement => [value],
        IEnumerable e => e.Cast<object?>(),
        _ => [value],
    };

    private static string? Prepare(object? value, JsonSerializerOptions json)
    {
        switch (value)
        {
            case null:
            case JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined }:
                return null;
            case string s:
                return Uri.EscapeDataString(s);
            case bool b:
                return b ? "true" : "false";
            case DateTime dt:
                return Uri.EscapeDataString(PocketBaseDate.Format(dt));
            case DateTimeOffset dto:
                return Uri.EscapeDataString(PocketBaseDate.Format(dto));
            case char or Guid or Enum:
                return Uri.EscapeDataString(value.ToString()!);
            case JsonElement { ValueKind: JsonValueKind.String } je:
                return Uri.EscapeDataString(je.GetString()!);
            case JsonElement je:
                return Uri.EscapeDataString(je.GetRawText());
        }

        if (PocketBaseDate.IsNumber(value))
            return Uri.EscapeDataString(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));

        // objects -> JSON
        return Uri.EscapeDataString(JsonSerializer.Serialize(value, value.GetType(), json));
    }
}

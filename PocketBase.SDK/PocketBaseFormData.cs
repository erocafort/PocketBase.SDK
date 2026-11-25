using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PocketBase.SDK.Abstractions;

namespace PocketBase.SDK;

/// <summary>Port of the JS SDK <c>tools/formdata.ts</c> (multipart conversion of bodies with files).</summary>
internal static class PocketBaseFormData
{
    /// <summary>
    /// Returns the top-level body fields (dictionary keys or public properties honoring
    /// <see cref="JsonPropertyNameAttribute"/>/<see cref="JsonIgnoreAttribute"/> and the naming policy),
    /// or null if the body is not an object-like value.
    /// </summary>
    public static List<KeyValuePair<string, object?>>? GetFields(object? body, JsonSerializerOptions json)
    {
        switch (body)
        {
            case RecordModel record:
                return GetFields(record.Data, json);
            case System.Text.Json.Nodes.JsonObject obj:
                return obj.Select(p => new KeyValuePair<string, object?>(p.Key, p.Value)).ToList();
            case JsonElement { ValueKind: JsonValueKind.Object } je:
                return je.EnumerateObject().Select(p => new KeyValuePair<string, object?>(p.Name, p.Value)).ToList();
            case JsonElement:
                return null;
            case IDictionary dict:
                var fromDict = new List<KeyValuePair<string, object?>>();
                foreach (DictionaryEntry e in dict) fromDict.Add(new(Convert.ToString(e.Key, CultureInfo.InvariantCulture)!, e.Value));
                return fromDict;
            case IEnumerable<KeyValuePair<string, object?>> pairs:
                return pairs.ToList();
            case null or string or HttpContent or IEnumerable:
                return null;
        }

        var type = body.GetType();
        if (type.IsPrimitive || type.IsEnum || body is decimal or DateTime or DateTimeOffset or Guid) return null;

        var result = new List<KeyValuePair<string, object?>>();
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;

            var ignore = p.GetCustomAttribute<JsonIgnoreAttribute>();
            var value = p.GetValue(body);
            if (ignore is { Condition: JsonIgnoreCondition.Always }) continue;
            if (ignore is { Condition: JsonIgnoreCondition.WhenWritingNull } && value is null) continue;

            var name = p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? json.PropertyNamingPolicy?.ConvertName(p.Name)
                ?? p.Name;

            result.Add(new(name, value));
        }

        return result;
    }

    public static bool HasFileValue(object? value) => value switch
    {
        FileUpload => true,
        string or IDictionary => false,
        IEnumerable e => e.Cast<object?>().Any(v => v is FileUpload),
        _ => false,
    };

    /// <summary>
    /// Converts the body to multipart/form-data if it has at least one <see cref="FileUpload"/> field value;
    /// returns null otherwise (meaning: send it as JSON).
    /// </summary>
    public static MultipartFormDataContent? ConvertIfNeeded(object? body, JsonSerializerOptions json)
    {
        var fields = GetFields(body, json);
        if (fields is null || !fields.Any(f => HasFileValue(f.Value))) return null;

        var form = new MultipartFormDataContent();

        foreach (var (key, val) in fields)
        {
            if (IsJsonLike(val) && !HasFileValue(val))
            {
                // send json-like values as jsonPayload to avoid the implicit string value normalization
                var payload = new Dictionary<string, object?> { [key] = val };
                form.Add(new StringContent(JsonSerializer.Serialize(payload, json), Encoding.UTF8), "@jsonPayload");
                continue;
            }

            // in case of mixed string and file values
            IEnumerable<object?> values = val is FileUpload || !IsJsonLike(val) ? [val] : ((IEnumerable)val!).Cast<object?>();
            foreach (var v in values) AddValue(form, key, v, json);
        }

        return form;
    }

    public static void AddValue(MultipartFormDataContent form, string key, object? value, JsonSerializerOptions json)
    {
        if (value is FileUpload file)
        {
            form.Add(file.CreateContent(), key, file.FileName);
            return;
        }

        form.Add(new StringContent(ToFormString(value, json), Encoding.UTF8), key);
    }

    // JS: typeof val === "object" (null, arrays, plain objects, Date...) -> json payload
    private static bool IsJsonLike(object? value) => value switch
    {
        null => true,
        string or bool or char or Enum or Guid => false,
        FileUpload => false,
        JsonElement je => je.ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null,
        System.Text.Json.Nodes.JsonValue => false,
        _ => !PocketBaseDate.IsNumber(value),
    };

    // JS FormData.append(key, value) -> String(value)
    private static string ToFormString(object? value, JsonSerializerOptions json) => value switch
    {
        null => "null",
        string s => s,
        bool b => b ? "true" : "false",
        JsonElement { ValueKind: JsonValueKind.String } je => je.GetString()!,
        JsonElement je => je.GetRawText(),
        System.Text.Json.Nodes.JsonValue jv when jv.GetValueKind() == JsonValueKind.String => jv.GetValue<string>(),
        System.Text.Json.Nodes.JsonNode jn => jn.ToJsonString(),
        IFormattable f when PocketBaseDate.IsNumber(value) => f.ToString(null, CultureInfo.InvariantCulture),
        char or Enum or Guid => value.ToString()!,
        _ => JsonSerializer.Serialize(value, value.GetType(), json),
    };
}

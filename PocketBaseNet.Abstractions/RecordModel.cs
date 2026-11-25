using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PocketBaseNet.Abstractions;

/// <summary>
/// A single PocketBase record (port of the Dart SDK <c>RecordModel</c> / JS SDK <c>RecordModel</c>).
/// All fields (including <c>expand</c>) are kept in <see cref="Data"/>; use <see cref="Get{T}(string, T)"/>
/// with dot-notation to read them (e.g. <c>record.Get&lt;string&gt;("expand.author.name")</c>).
/// Serializes to/from JSON as its plain data object.
/// </summary>
[JsonConverter(typeof(RecordModelJsonConverter))]
public sealed class RecordModel
{
    public RecordModel() : this(new JsonObject())
    {
    }

    public RecordModel(JsonObject data)
    {
        Data = data ?? new JsonObject();
    }

    /// <summary>The raw record data (all fields).</summary>
    public JsonObject Data { get; }

    public string Id
    {
        get => Get<string>("id");
        set => Data["id"] = value;
    }

    public string CollectionId => Get<string>("collectionId");

    public string CollectionName => Get<string>("collectionName");

    /// <summary>Creates a record from a JSON element (non-object values result in an empty record).</summary>
    public static RecordModel FromJson(JsonElement json) =>
        new(json.ValueKind == JsonValueKind.Object ? (JsonObject)JsonNode.Parse(json.GetRawText())! : new JsonObject());

    /// <summary>Creates a record from a JSON string.</summary>
    public static RecordModel FromJson(string json) =>
        new(JsonNode.Parse(json) as JsonObject ?? new JsonObject());

    /// <summary>
    /// Extracts a single value by a dot-notation path (array items are accessed by index, e.g. <c>"a.b.2.c"</c>)
    /// and casts it to <typeparamref name="T"/> following the Dart SDK casting rules
    /// (<c>string</c>, <c>bool</c>, <c>int</c>, <c>long</c>, <c>double</c>, <c>decimal</c>, <see cref="RecordModel"/>, <c>List&lt;T&gt;</c> of those).
    /// Any other type is deserialized from JSON. Returns <paramref name="defaultValue"/> when the path is missing
    /// (or, if not provided, the "zero" value of the cast type: <c>""</c>, <c>false</c>, <c>0</c>, empty list...).
    /// </summary>
    public T Get<T>(string fieldNameOrPath, T defaultValue = default!)
    {
        if (!TryExtract(Data, fieldNameOrPath, out var node))
        {
            // Dart: missing path -> cast(defaultValue)
            return defaultValue is not null || !RecordCaster.IsZeroable(typeof(T)) ? defaultValue : (T)RecordCaster.Cast(null, typeof(T))!;
        }

        return (T)RecordCaster.Cast(node, typeof(T))!;
    }

    /// <summary>Updates a single record field value.</summary>
    public void Set(string fieldName, object? value) =>
        Data[fieldName] = value is JsonNode n ? n : JsonSerializer.SerializeToNode(value);

    private static readonly JsonSerializerOptions DefaultObjectOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Deserializes the record data to a custom type (case-insensitive property names by default, same as the client).</summary>
    public T? ToObject<T>(JsonSerializerOptions? options = null) => Data.Deserialize<T>(options ?? DefaultObjectOptions);

    /// <summary>Returns a deep copy of the record.</summary>
    public RecordModel Clone() => new((JsonObject)Data.DeepClone());

    public string ToJson() => Data.ToJsonString();

    public override string ToString() => ToJson();

    private static bool TryExtract(JsonNode? data, string path, out JsonNode? result)
    {
        result = data;
        foreach (var part in path.Split('.'))
        {
            switch (result)
            {
                case JsonObject obj when obj.TryGetPropertyValue(part, out var next):
                    result = next;
                    break;
                case JsonArray arr when int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i < arr.Count:
                    result = arr[i];
                    break;
                default:
                    result = null;
                    return false;
            }
        }

        return true;
    }
}

internal static class RecordCaster
{
    public static bool IsZeroable(Type t) =>
        t == typeof(string) || t == typeof(bool) || t == typeof(int) || t == typeof(long) ||
        t == typeof(double) || t == typeof(decimal) || t == typeof(RecordModel) || ListItemType(t) is not null;

    public static object? Cast(JsonNode? node, Type t)
    {
        var underlying = Nullable.GetUnderlyingType(t);

        // .NET nullable value types: null/missing stays null (instead of Dart's zero value)
        if (underlying is not null && node is null) return null;

        var target = underlying ?? t;

        if (target == typeof(string)) return ToStringValue(node);
        if (target == typeof(bool)) return ToBool(node);
        if (target == typeof(int)) return (int)ToDouble(node, integer: true);
        if (target == typeof(long)) return (long)ToDouble(node, integer: true);
        if (target == typeof(double)) return ToDouble(node, integer: false);
        if (target == typeof(decimal)) return (decimal)ToDouble(node, integer: false);
        if (target == typeof(RecordModel)) return node is JsonObject o ? new RecordModel((JsonObject)o.DeepClone()) : new RecordModel();
        if (target == typeof(JsonNode)) return node?.DeepClone();

        var itemType = ListItemType(target);
        if (itemType is not null && IsZeroable(itemType))
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType))!;
            if (node is JsonArray arr)
            {
                foreach (var item in arr) list.Add(Cast(item, itemType));
            }
            else if (node is not null)
            {
                list.Add(Cast(node, itemType));
            }
            return list;
        }

        return node?.Deserialize(t);
    }

    private static Type? ListItemType(Type t)
    {
        if (!t.IsGenericType) return null;
        var def = t.GetGenericTypeDefinition();
        return def == typeof(List<>) || def == typeof(IList<>) || def == typeof(IReadOnlyList<>) || def == typeof(IEnumerable<>)
            ? t.GetGenericArguments()[0]
            : null;
    }

    private static string ToStringValue(JsonNode? node) => node switch
    {
        null => "",
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        _ => node.ToJsonString(),
    };

    // Dart toBool(): null -> false; num -> != 0; string -> not in ("", "false", "0"); list/map -> not empty
    private static bool ToBool(JsonNode? node) => node switch
    {
        null => false,
        JsonArray a => a.Count > 0,
        JsonObject o => o.Count > 0,
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => v.GetValue<double>() != 0,
            JsonValueKind.String => v.GetValue<string>().ToLowerInvariant() is not ("" or "false" or "0"),
            _ => false,
        },
        _ => false,
    };

    // Dart toInt()/toDouble(): null -> 0; string -> parsed or 0; bool -> 0/1; list/map -> length
    private static double ToDouble(JsonNode? node, bool integer) => node switch
    {
        null => 0,
        JsonArray a => a.Count,
        JsonObject o => o.Count,
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.Number => integer ? Math.Truncate(v.GetValue<double>()) : v.GetValue<double>(),
            JsonValueKind.True => 1,
            JsonValueKind.String => integer
                ? (long.TryParse(v.GetValue<string>(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l) ? l : 0)
                : (double.TryParse(v.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0),
            _ => 0,
        },
        _ => 0,
    };
}

internal sealed class RecordModelJsonConverter : JsonConverter<RecordModel>
{
    public override RecordModel? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;

        return JsonNode.Parse(ref reader) is JsonObject obj ? new RecordModel(obj) : new RecordModel();
    }

    public override void Write(Utf8JsonWriter writer, RecordModel value, JsonSerializerOptions options) =>
        value.Data.WriteTo(writer, options);
}

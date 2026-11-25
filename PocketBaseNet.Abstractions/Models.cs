using System.Text.Json;

namespace PocketBaseNet.Abstractions;

// transitional (step 4 will replace them with the SDKs CollectionModel/settings DTOs)
public sealed record PbCollection(string Id, string Name, string Type, DateTimeOffset? Created, DateTimeOffset? Updated, JsonElement Data);

public sealed record PbSettings(JsonElement Data);

public sealed class PocketBaseOptions
{
    /// <summary>Base PocketBase url (e.g. <c>http://127.0.0.1:8090</c>). Required.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Language code sent as <c>Accept-Language</c> header (SDK default: <c>en-US</c>).</summary>
    public string Lang { get; set; } = "en-US";

    /// <summary>
    /// Auto clear the auth store in case of expired or invalidated token (e.g. after 401 response).
    /// Same as <c>pb.autoClearAuthStore(bool)</c>; enabled by default like in the official SDKs.
    /// </summary>
    public bool AutoClearAuthStore { get; set; } = true;

    /// <summary>
    /// Serializer options used for request bodies and typed responses.
    /// By default property names are sent as declared (no camelCase), because PocketBase field names are case-sensitive.
    /// </summary>
    public JsonSerializerOptions JsonSerializerOptions { get; set; } = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}

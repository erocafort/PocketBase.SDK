namespace PocketBase.SDK.Abstractions;

/// <summary>Options for a single API request (equivalent to the SDKs <c>SendOptions</c>).</summary>
public sealed class SendOptions
{
    /// <summary>HTTP method (defaults to GET).</summary>
    public HttpMethod Method { get; set; } = HttpMethod.Get;

    /// <summary>Custom headers (case-insensitive). Explicit <c>Authorization</c>/<c>Accept-Language</c> values are not overwritten.</summary>
    public IDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Query parameters. <c>null</c> values are skipped, collections are sent as repeated keys,
    /// dates are sent in PocketBase format and other objects are JSON serialized.
    /// </summary>
    public IDictionary<string, object?> Query { get; init; } = new Dictionary<string, object?>();

    /// <summary>
    /// Request body. Any object is serialized as JSON, a <see cref="string"/> is sent as-is with JSON content type
    /// and an <see cref="HttpContent"/> (e.g. <see cref="MultipartFormDataContent"/>) is sent untouched
    /// (its ownership is transferred: it is disposed after the request).
    /// </summary>
    public object? Body { get; set; }
}

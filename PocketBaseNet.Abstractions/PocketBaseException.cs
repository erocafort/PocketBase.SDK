using System.Text.Json;

namespace PocketBaseNet.Abstractions;

/// <summary>
/// Normalized error thrown by every PocketBase call.
/// Equivalent to <c>ClientResponseError</c> (JS SDK) / <c>ClientException</c> (Dart SDK).
/// </summary>
public class PocketBaseException : Exception
{
    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    public PocketBaseException(string url, int status, JsonElement response = default, bool isAbort = false, Exception? originalError = null)
        : base(ResolveMessage(Normalize(response), isAbort), originalError)
    {
        Url = url ?? string.Empty;
        Status = status;
        Response = Normalize(response);
        IsAbort = isAbort;
    }

    /// <summary>Requested url.</summary>
    public string Url { get; }

    /// <summary>Response status code (0 when no response was received).</summary>
    public int Status { get; }

    /// <summary>The API JSON error response (always a JSON object, <c>{}</c> if missing), e.g. <c>{"status":400,"message":"...","data":{...}}</c> or <c>{"mfaId":"..."}</c>.</summary>
    public JsonElement Response { get; }

    /// <summary>Whether the error is the result of an aborted request (e.g. HttpClient timeout).</summary>
    public bool IsAbort { get; }

    /// <summary>The original non-normalized error (alias of <see cref="Exception.InnerException"/>).</summary>
    public Exception? OriginalError => InnerException;

    private static JsonElement Normalize(JsonElement response) =>
        response.ValueKind == JsonValueKind.Object ? response : EmptyObject;

    private static string ResolveMessage(JsonElement response, bool isAbort)
    {
        if (response.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(m.GetString()))
            return m.GetString()!;

        return isAbort ? "The request was aborted." : "Something went wrong.";
    }
}

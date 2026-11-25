using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PocketBase.SDK.Abstractions;

/// <summary>Removes the subscription it was returned for (JS/Dart <c>UnsubscribeFunc</c>).</summary>
public delegate Task UnsubscribeFunc();

/// <summary>A generic SSE event message (port of the Dart SDK <c>SseMessage</c>).</summary>
public sealed class SseMessage
{
    /// <summary>The last event id (for <c>PB_CONNECT</c> it is the realtime client id).</summary>
    public string Id { get; init; } = "";

    /// <summary>The event name (the subscription topic, <c>PB_CONNECT</c>...). Defaults to <c>"message"</c>.</summary>
    public string Event { get; init; } = "message";

    /// <summary>The raw event data.</summary>
    public string Data { get; init; } = "";

    /// <summary>The reconnection time in ms sent by the server (0 if not set).</summary>
    public int Retry { get; init; }

    /// <summary>
    /// Decodes <see cref="Data"/> as a JSON object (same as the Dart SDK <c>jsonData()</c>):
    /// non-object values are wrapped as <c>{"data": value}</c> and an empty data returns <c>{}</c>.
    /// </summary>
    public JsonObject JsonData()
    {
        if (Data.Length == 0) return new JsonObject();

        try
        {
            var node = JsonNode.Parse(Data);
            return node is JsonObject obj ? obj : new JsonObject { ["data"] = node };
        }
        catch (JsonException)
        {
            return new JsonObject { ["data"] = Data };
        }
    }

    public override string ToString() =>
        JsonSerializer.Serialize(new Dictionary<string, object> { ["id"] = Id, ["event"] = Event, ["data"] = Data, ["retry"] = Retry });
}

/// <summary>A single record realtime event (JS <c>RecordSubscription</c> / Dart <c>RecordSubscriptionEvent</c>).</summary>
public sealed class RecordSubscriptionEvent<T>
{
    /// <summary><c>create</c>, <c>update</c> or <c>delete</c>.</summary>
    [JsonPropertyName("action")] public string Action { get; set; } = "";

    /// <summary>The changed record (null if missing from the event).</summary>
    [JsonPropertyName("record")] public T? Record { get; set; }
}

/// <summary>
/// Realtime subscription options (JS <c>RecordSubscribeOptions</c>; Dart named <c>expand/filter/fields/query/headers</c> arguments).
/// They are serialized in the topic as <c>?options={"query":{...},"headers":{...}}</c> and applied by the server
/// when building each event message. Typed properties take precedence over the same keys in <see cref="Query"/>.
/// </summary>
public sealed class RealtimeSubscribeOptions
{
    public string? Fields { get; set; }
    public string? Filter { get; set; }
    public string? Expand { get; set; }
    public IDictionary<string, object?> Query { get; init; } = new Dictionary<string, object?>();
    public IDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Realtime (SSE) service (port of the SDKs <c>RealtimeService</c>).
/// The SSE connection is opened on the first subscription and closed automatically when no subscriptions are left.
/// It reconnects automatically (resubmitting the subscriptions) if the connection is interrupted.
/// <para>
/// Callbacks are invoked sequentially from the connection reader (awaited, in the order the events arrive);
/// exceptions thrown by callbacks are caught (and traced) so they don't break the connection.
/// </para>
/// </summary>
public interface IRealtimeService
{
    /// <summary>The established SSE connection client id (empty if not connected).</summary>
    string ClientId { get; }

    /// <summary>Whether the realtime connection has been established.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// Optional hook invoked when the realtime client disconnects, either when unsubscribing from all subscriptions
    /// (empty list) or when the connection was interrupted/closed by the server (the active subscriptions).
    /// To listen for the opposite (connect/reconnect) subscribe to the <c>PB_CONNECT</c> topic.
    /// </summary>
    Action<IReadOnlyList<string>>? OnDisconnect { get; set; }

    /// <summary>
    /// Registers a subscription listener (multiple listeners per topic are allowed).
    /// If the SSE connection is not started yet, this method also initializes it.
    /// Cancelling <paramref name="cancellationToken"/> while waiting removes the listener and throws <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<UnsubscribeFunc> SubscribeAsync(string topic, Func<SseMessage, Task> callback, RealtimeSubscribeOptions? options = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="SubscribeAsync(string, Func{SseMessage, Task}, RealtimeSubscribeOptions?, CancellationToken)"/>
    Task<UnsubscribeFunc> SubscribeAsync(string topic, Action<SseMessage> callback, RealtimeSubscribeOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsubscribes all listeners of <paramref name="topic"/> (including the ones subscribed with options),
    /// or every subscription if it is null/empty. The connection is closed if no subscriptions are left.
    /// </summary>
    Task UnsubscribeAsync(string? topic = null);

    /// <summary>Unsubscribes all listeners whose topic starts with <paramref name="topicPrefix"/> (no-op if none).</summary>
    Task UnsubscribeByPrefixAsync(string topicPrefix);

    /// <summary>Unsubscribes the listeners of <paramref name="topic"/> registered with the specified callback delegate.</summary>
    Task UnsubscribeByTopicAndListenerAsync(string topic, Delegate callback);
}

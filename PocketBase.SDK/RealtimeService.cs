using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PocketBase.SDK.Abstractions;

namespace PocketBase.SDK;

/// <summary>
/// Port of the official JS SDK <c>RealtimeService</c> (connect/reconnect state machine), using an HTTP
/// streamed response instead of the browser <c>EventSource</c> (same approach as the Dart SDK <c>SseClient</c>).
/// </summary>
/// <remarks>
/// The instance holds the subscriptions and the SSE connection, so it must live as long as the auth session:
/// with DI it is registered with the same lifetime as the <see cref="IAuthStore"/>.
/// Requests are sent through a transport obtained from the factory on each use (so long-lived instances
/// don't pin a single <see cref="HttpClient"/>). Disposing the instance closes the connection.
/// </remarks>
public sealed class RealtimeService : IRealtimeService, IDisposable
{
    private const string RealtimePath = "/api/realtime";
    private const string ConnectEvent = "PB_CONNECT";

    private static readonly int[] PredefinedReconnectIntervals = [200, 300, 500, 1000, 1200, 1500, 2000];

    private readonly Func<IPocketBaseTransport> _transportFactory;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _submitLock = new(1, 1);

    private readonly Dictionary<string, List<Listener>> _subscriptions = new();
    private List<string> _lastSentSubscriptions = [];
    private readonly List<TaskCompletionSource> _pendingConnects = [];

    private CancellationTokenSource? _connectionCts; // not null = "eventSource" exists
    private CancellationTokenSource _timersCts = new(); // connect timeout + reconnect timers
    private long _generation; // incremented on each disconnect, to ignore callbacks of closed connections
    private int _reconnectAttempts;
    private string _clientId = "";
    private bool _disposed;

    /// <summary>Creates the service sending its requests through <paramref name="transport"/>.</summary>
    public RealtimeService(IPocketBaseTransport transport)
        : this(() => transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
    }

    /// <summary>Creates the service resolving the transport on each use (used by the DI registration).</summary>
    public RealtimeService(Func<IPocketBaseTransport> transportFactory)
    {
        _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
    }

    /// <summary>Max time to wait for the <c>PB_CONNECT</c> event (JS SDK: 15s).</summary>
    internal TimeSpan MaxConnectTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Max time without receiving any data before the connection is considered dead and reestablished
    /// (Dart SDK: 315s; the server sends a disconnect signal after 5 minutes of inactivity).
    /// </summary>
    internal TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(315);

    /// <summary>Max reconnect attempts after an interrupted connection (JS SDK: unlimited).</summary>
    internal int MaxReconnectAttempts { get; set; } = int.MaxValue;

    /// <summary>Reconnect delays (JS SDK predefined intervals); internal to allow faster tests.</summary>
    internal Func<int, TimeSpan> ReconnectDelay { get; set; } = attempt =>
        TimeSpan.FromMilliseconds(PredefinedReconnectIntervals[Math.Min(attempt, PredefinedReconnectIntervals.Length - 1)]);

    public string ClientId
    {
        get { lock (_sync) return _clientId; }
    }

    public bool IsConnected
    {
        get { lock (_sync) return IsConnectedUnsafe; }
    }

    private bool IsConnectedUnsafe => _connectionCts is not null && _clientId.Length > 0 && _pendingConnects.Count == 0;

    public Action<IReadOnlyList<string>>? OnDisconnect { get; set; }

    // -----------------------------------------------------------------
    // Subscribe / unsubscribe
    // -----------------------------------------------------------------

    public Task<UnsubscribeFunc> SubscribeAsync(string topic, Action<SseMessage> callback, RealtimeSubscribeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return SubscribeCoreAsync(topic, new Listener(callback, e => { callback(e); return Task.CompletedTask; }), options, cancellationToken);
    }

    public Task<UnsubscribeFunc> SubscribeAsync(string topic, Func<SseMessage, Task> callback, RealtimeSubscribeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return SubscribeCoreAsync(topic, new Listener(callback, callback), options, cancellationToken);
    }

    internal async Task<UnsubscribeFunc> SubscribeCoreAsync(string topic, Listener listener, RealtimeSubscribeOptions? options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(topic)) throw new ArgumentException("topic must be set.", nameof(topic));

        var key = topic + SerializeOptions(topic, options);

        bool connected, first;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_subscriptions.TryGetValue(key, out var listeners))
            {
                listeners = [];
                _subscriptions[key] = listeners;
            }
            listeners.Add(listener);

            first = listeners.Count == 1;
            connected = IsConnectedUnsafe;
        }

        try
        {
            if (!connected)
            {
                await ConnectAsync().WaitAsync(cancellationToken).ConfigureAwait(false); // initialize the sse connection
            }
            else if (first)
            {
                await SubmitSubscriptionsAsync().WaitAsync(cancellationToken).ConfigureAwait(false); // send the updated subscriptions
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await UnsubscribeByTopicAndListenerCoreAsync(topic, l => ReferenceEquals(l, listener)).ConfigureAwait(false);
            throw;
        }

        return () => UnsubscribeByTopicAndListenerCoreAsync(topic, l => ReferenceEquals(l, listener));
    }

    public Task UnsubscribeAsync(string? topic = null)
    {
        lock (_sync)
        {
            if (string.IsNullOrEmpty(topic))
            {
                _subscriptions.Clear(); // remove all subscriptions
            }
            else
            {
                foreach (var key in GetSubscriptionKeysByTopic(topic)) _subscriptions.Remove(key);
            }
        }

        return SubmitSubscriptionsAsync();
    }

    public Task UnsubscribeByPrefixAsync(string topicPrefix)
    {
        lock (_sync)
        {
            // "?" so that it can be used as end delimiter for the prefix
            var keys = _subscriptions.Keys.Where(k => (k + "?").StartsWith(topicPrefix, StringComparison.Ordinal)).ToList();
            if (keys.Count == 0) return Task.CompletedTask; // nothing to unsubscribe from

            foreach (var key in keys) _subscriptions.Remove(key);
        }

        return SubmitSubscriptionsAsync();
    }

    public Task UnsubscribeByTopicAndListenerAsync(string topic, Delegate callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return UnsubscribeByTopicAndListenerCoreAsync(topic, l => Equals(l.Original, callback));
    }

    private Task UnsubscribeByTopicAndListenerCoreAsync(string topic, Func<Listener, bool> match)
    {
        lock (_sync)
        {
            foreach (var key in GetSubscriptionKeysByTopic(topic))
            {
                var listeners = _subscriptions[key];
                listeners.RemoveAll(l => match(l));

                // remove the key from the subscriptions list if there are no other listeners
                if (listeners.Count == 0) _subscriptions.Remove(key);
            }
        }

        return SubmitSubscriptionsAsync();
    }

    /// <summary>Serializes the topic options as <c>?options={"query":...,"headers":...}</c> (empty if there are none).</summary>
    private static string SerializeOptions(string topic, RealtimeSubscribeOptions? options)
    {
        if (options is null) return "";

        var query = new Dictionary<string, object?>(options.Query);
        if (options.Fields is not null) query["fields"] = options.Fields;
        if (options.Filter is not null) query["filter"] = options.Filter;
        if (options.Expand is not null) query["expand"] = options.Expand;

        // same as the Dart SDK: only the non-empty parts are serialized
        var serialized = new Dictionary<string, object?>();
        if (query.Count > 0) serialized["query"] = query;
        if (options.Headers.Count > 0) serialized["headers"] = options.Headers;
        if (serialized.Count == 0) return "";

        return (topic.Contains('?') ? "&" : "?") + "options=" + Uri.EscapeDataString(JsonSerializer.Serialize(serialized));
    }

    // must be called inside the lock
    private List<string> GetSubscriptionKeysByTopic(string topic)
    {
        // "?" so that it can be used as end delimiter for the topic
        topic = topic.Contains('?') ? topic : topic + "?";

        return _subscriptions.Keys.Where(k => (k + "?").StartsWith(topic, StringComparison.Ordinal)).ToList();
    }

    // must be called inside the lock
    private List<string> GetNonEmptySubscriptionKeys() => _subscriptions.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key).ToList();

    private bool HasUnsentSubscriptions()
    {
        lock (_sync)
        {
            var latest = GetNonEmptySubscriptionKeys();
            return latest.Count != _lastSentSubscriptions.Count || latest.Any(t => !_lastSentSubscriptions.Contains(t));
        }
    }

    // -----------------------------------------------------------------
    // Subscriptions submit
    // -----------------------------------------------------------------

    /// <summary>Sends the subscriptions (serialized: concurrent calls are sent one after another and the unchanged ones are skipped).</summary>
    private async Task SubmitSubscriptionsAsync()
    {
        await _submitLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await SendSubscriptionsAsync().ConfigureAwait(false);
        }
        finally
        {
            _submitLock.Release();
        }
    }

    private async Task SendSubscriptionsAsync()
    {
        string clientId;
        List<string> subscriptions;
        CancellationToken token;
        Action? notify = null;

        lock (_sync)
        {
            // not initialized yet or connection closed
            if (_clientId.Length == 0) return;

            var latest = GetNonEmptySubscriptionKeys();

            if (latest.Count > 0)
            {
                if (latest.Count == _lastSentSubscriptions.Count && latest.All(_lastSentSubscriptions.Contains)) return; // no change

                // optimistic update
                _lastSentSubscriptions = latest;
                clientId = _clientId;
                subscriptions = [.. latest];
                token = _connectionCts?.Token ?? default;
            }
            else
            {
                // no subscriptions -> close the SSE connection
                notify = DisconnectUnsafe(fromReconnect: false);
                clientId = "";
                subscriptions = [];
                token = default;
            }
        }

        if (subscriptions.Count == 0)
        {
            SafeInvoke(notify);
            return;
        }

        try
        {
            await _transportFactory().SendAsync(RealtimePath, new SendOptions
            {
                Method = HttpMethod.Post,
                Body = new Dictionary<string, object?>
                {
                    ["clientId"] = clientId,
                    ["subscriptions"] = subscriptions,
                },
            }, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // silently ignore aborted pending requests (the connection was closed)
        }

    }

    // -----------------------------------------------------------------
    // Connect / disconnect
    // -----------------------------------------------------------------

    private Task ConnectAsync()
    {
        TaskCompletionSource tcs;
        bool start;

        lock (_sync)
        {
            // immediately resolve to avoid indefinitely blocking the caller during reconnection
            if (_reconnectAttempts > 0) return Task.CompletedTask;

            tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingConnects.Add(tcs);
            start = _pendingConnects.Count == 1;
        }

        if (start) _ = Task.Run(() => InitConnect(expectedGeneration: null));

        return tcs.Task;
    }

    private void InitConnect(long? expectedGeneration)
    {
        Action? notify;
        long generation;
        CancellationToken token;

        lock (_sync)
        {
            if (_disposed || (expectedGeneration is not null && expectedGeneration != _generation)) return;

            notify = DisconnectUnsafe(fromReconnect: true);

            var cts = new CancellationTokenSource();
            _connectionCts = cts;
            generation = _generation;
            token = cts.Token;

            // wait up to MaxConnectTimeout for PB_CONNECT
            StartTimerUnsafe(MaxConnectTimeout, () => ConnectErrorHandler(generation, new TimeoutException("EventSource connect took too long.")));
        }

        SafeInvoke(notify);

        _ = Task.Run(() => ReadLoopAsync(generation, token));
    }

    private async Task ReadLoopAsync(long generation, CancellationToken token)
    {
        try
        {
            var concrete = _transportFactory() switch
            {
                PocketBaseTransport t => t,
                PocketBaseClient c => c.Transport as PocketBaseTransport,
                _ => null,
            } ?? throw new NotSupportedException($"Realtime requires the default {nameof(PocketBaseTransport)}.");

            using var response = await concrete.OpenEventStreamAsync(RealtimePath, token).ConfigureAwait(false);
            using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var parser = new SseParser();

            while (true)
            {
                string? line;
                using (var idle = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    idle.CancelAfter(IdleTimeout);
                    try
                    {
                        line = await reader.ReadLineAsync(idle.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        throw new TimeoutException("Realtime connection idle timeout.");
                    }
                }

                // graceful connection termination (e.g. the server closed an idle client)
                if (line is null) throw new IOException("The realtime connection was closed by the server.");

                var message = parser.ProcessLine(line);
                if (message is not null) await DispatchAsync(generation, message).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // closed by disconnect
        }
        catch (Exception e)
        {
            ConnectErrorHandler(generation, e);
        }
    }

    private async Task DispatchAsync(long generation, SseMessage message)
    {
        if (message.Event == ConnectEvent)
        {
            await HandleConnectAsync(generation, message).ConfigureAwait(false);
            return;
        }

        List<Listener> listeners;
        lock (_sync)
        {
            if (generation != _generation || !_subscriptions.TryGetValue(message.Event, out var registered)) return;
            listeners = [.. registered];
        }

        foreach (var listener in listeners) await SafeCallAsync(listener, message).ConfigureAwait(false);
    }

    private async Task HandleConnectAsync(long generation, SseMessage message)
    {
        lock (_sync)
        {
            if (generation != _generation) return;
            _clientId = message.Id;
            _lastSentSubscriptions = [];
        }

        try
        {
            await SubmitSubscriptionsAsync().ConfigureAwait(false);

            // in case new topics were added while the initial submit was still pending
            for (var maxResubmit = 3; maxResubmit > 0 && HasUnsentSubscriptions(); maxResubmit--)
            {
                await SubmitSubscriptionsAsync().ConfigureAwait(false);
            }

            List<TaskCompletionSource> pending;
            List<Listener> connectListeners;
            lock (_sync)
            {
                if (generation != _generation) return; // disconnected meanwhile (e.g. no subscriptions left)

                // resolve the connect promises and reset the connect meta
                pending = [.. _pendingConnects];
                _pendingConnects.Clear();
                _reconnectAttempts = 0;
                CancelTimersUnsafe();

                connectListeners = GetSubscriptionKeysByTopic(ConnectEvent).SelectMany(k => _subscriptions[k]).ToList();
            }

            foreach (var p in pending) p.TrySetResult();

            // propagate the PB_CONNECT event
            foreach (var listener in connectListeners) await SafeCallAsync(listener, message).ConfigureAwait(false);

            // last resort in case the resubmits above weren't enough
            if (HasUnsentSubscriptions()) await SubmitSubscriptionsAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            lock (_sync)
            {
                if (generation != _generation) return;
                _clientId = "";
                _lastSentSubscriptions = [];
            }

            ConnectErrorHandler(generation, e);
        }
    }

    private void ConnectErrorHandler(long generation, Exception error)
    {
        List<TaskCompletionSource>? rejected = null;
        Action? notify;

        lock (_sync)
        {
            if (generation != _generation || _disposed) return; // error of an already closed connection

            CancelTimersUnsafe();

            if ((_clientId.Length == 0 && _reconnectAttempts == 0) || _reconnectAttempts > MaxReconnectAttempts)
            {
                // wasn't previously connected or the reconnect limit was reached -> reject
                rejected = [.. _pendingConnects];
                _pendingConnects.Clear();
                notify = DisconnectUnsafe(fromReconnect: false);
            }
            else
            {
                // otherwise -> reconnect in the background
                notify = DisconnectUnsafe(fromReconnect: true);

                var delay = ReconnectDelay(_reconnectAttempts);
                _reconnectAttempts++;

                var reconnectGeneration = _generation;
                StartTimerUnsafe(delay, () => InitConnect(reconnectGeneration));
            }
        }

        SafeInvoke(notify);

        if (rejected is { Count: > 0 })
        {
            var exception = error as PocketBaseException ?? new PocketBaseException(string.Empty, 0, default, false, error);
            foreach (var p in rejected) p.TrySetException(exception);
        }
    }

    /// <summary>Closes the connection. Must be called inside the lock; returns the <see cref="OnDisconnect"/> call to invoke outside of it.</summary>
    private Action? DisconnectUnsafe(bool fromReconnect)
    {
        Action? notify = null;
        if (_clientId.Length > 0 && OnDisconnect is { } onDisconnect)
        {
            IReadOnlyList<string> active = [.. _subscriptions.Keys];
            notify = () => onDisconnect(active);
        }

        CancelTimersUnsafe();

        // closes the stream and aborts the pending subscriptions submit (JS cancelRequest)
        _connectionCts?.Cancel();
        _connectionCts?.Dispose();
        _connectionCts = null;
        _generation++;
        _clientId = "";
        _lastSentSubscriptions = [];

        if (!fromReconnect)
        {
            _reconnectAttempts = 0;

            // resolve any remaining connect promises (e.g. unsubscribe called before the connect completed)
            foreach (var p in _pendingConnects) p.TrySetResult();
            _pendingConnects.Clear();
        }

        return notify;
    }

    private void CancelTimersUnsafe()
    {
        _timersCts.Cancel();
        _timersCts.Dispose();
        _timersCts = new CancellationTokenSource();
    }

    private void StartTimerUnsafe(TimeSpan delay, Action action)
    {
        var token = _timersCts.Token;
        _ = Task.Delay(delay, token).ContinueWith(t =>
        {
            if (!t.IsCanceled) action();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private static async Task SafeCallAsync(Listener listener, SseMessage message)
    {
        try
        {
            await listener.Handler(message).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Trace.TraceError($"PocketBase realtime: unhandled exception in the \"{message.Event}\" subscription callback: {e}");
        }
    }

    private static void SafeInvoke(Action? action)
    {
        if (action is null) return;

        try
        {
            action();
        }
        catch (Exception e)
        {
            Trace.TraceError($"PocketBase realtime: unhandled exception in OnDisconnect: {e}");
        }
    }

    /// <summary>Removes all the subscriptions and closes the connection (without invoking <see cref="OnDisconnect"/>).</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;

            _subscriptions.Clear();
            OnDisconnect = null;
            DisconnectUnsafe(fromReconnect: false);
            _timersCts.Cancel();
        }
    }

    /// <summary>A subscription listener: <see cref="Original"/> is the user delegate (used to unsubscribe by listener).</summary>
    internal sealed class Listener(Delegate original, Func<SseMessage, Task> handler)
    {
        public Delegate Original { get; } = original;
        public Func<SseMessage, Task> Handler { get; } = handler;
    }
}

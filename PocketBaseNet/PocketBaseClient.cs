using System.Collections.Concurrent;
using System.Text.Json;
using PocketBaseNet.Abstractions;

namespace PocketBaseNet;

/// <summary>
/// PocketBase client (port of the official SDKs <c>PocketBase</c>/<c>Client</c>).
/// Use it through DI (<c>services.AddPocketBase(...)</c>) or standalone with <see cref="Create"/>.
/// </summary>
public sealed class PocketBaseClient : IPocketBaseClient
{
    private readonly IPocketBaseTransport _transport;
    private readonly ConcurrentDictionary<string, RecordService> _recordServices = new();
    private readonly object _autoRefreshSync = new();
    private AutoRefreshState? _autoRefresh;
    private long _autoRefreshVersion;

    private sealed class AutoRefreshState
    {
        public required double Threshold { get; init; }
        public required Func<CancellationToken, Task> Refresh { get; init; }
        public required Func<CancellationToken, Task> Reauthenticate { get; init; }
        public required string? ModelId { get; init; }
        public required string? ModelCollectionId { get; init; }
        public required IDisposable Subscription { get; init; }
        public required long Version { get; init; }
        public SemaphoreSlim Lock { get; } = new(1, 1);
    }

    /// <summary>Creates a client that owns its own <see cref="RealtimeService"/>.</summary>
    public PocketBaseClient(IPocketBaseTransport transport, PocketBaseOptions options)
        : this(transport, options, null)
    {
    }

    /// <summary>
    /// Creates a client using a shared <paramref name="realtime"/> service (used by DI, where the realtime
    /// subscriptions must outlive the transient client). If null, the client owns a new instance.
    /// </summary>
    public PocketBaseClient(IPocketBaseTransport transport, PocketBaseOptions options, IRealtimeService? realtime)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        Options = options ?? throw new ArgumentNullException(nameof(options));

        Collections = new CollectionsService(this);
        Settings = new SettingsService(this);
        Files = new FilesService(this);
        Logs = new LogsService(this);
        Backups = new BackupsService(this);
        Crons = new CronsService(this);
        Sql = new SqlService(this);
        Health = new HealthService(this);
        Realtime = realtime ?? new RealtimeService(transport);
    }

    /// <summary>Creates a standalone client without dependency injection.</summary>
    /// <param name="options">Client options (<see cref="PocketBaseOptions.BaseUrl"/> is required).</param>
    /// <param name="httpClient">Optional HttpClient (the caller keeps its ownership).</param>
    /// <param name="authStore">Optional auth store (defaults to an in-memory <see cref="AuthStore"/>).</param>
    public static PocketBaseClient Create(PocketBaseOptions options, HttpClient? httpClient = null, IAuthStore? authStore = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.BaseUrl)) throw new ArgumentException("PocketBase BaseUrl is required.", nameof(options));

        return new PocketBaseClient(new PocketBaseTransport(httpClient ?? new HttpClient(), options, authStore ?? new AuthStore()), options);
    }

    internal PocketBaseOptions Options { get; }

    internal IPocketBaseTransport Transport => _transport;

    public string BaseUrl => Options.BaseUrl;

    public IAuthStore AuthStore => _transport.AuthStore;

#pragma warning disable CS0618
    [Obsolete("Since PocketBase v0.23 admins are regular auth records of the \"_superusers\" collection: use Collection(\"_superusers\").")]
    public IRecordService Admins => Collection(PocketBaseNet.AuthStore.SuperusersCollectionName);
#pragma warning restore CS0618

    public ICollectionsService Collections { get; }
    public ISettingsService Settings { get; }
    public IFilesService Files { get; }
    public ILogsService Logs { get; }
    public IBackupsService Backups { get; }
    public ICronsService Crons { get; }
    public ISqlService Sql { get; }
    public IHealthService Health { get; }
    public IRealtimeService Realtime { get; }

    public IRecordService Collection(string idOrName)
    {
        ArgumentException.ThrowIfNullOrEmpty(idOrName);
        return _recordServices.GetOrAdd(idOrName, name => new RecordService(this, name));
    }

    public IBatchService CreateBatch() => new BatchService(this);

    public string BuildUrl(string path) => _transport.BuildUrl(path);

    public string Filter(string raw, IReadOnlyDictionary<string, object?>? parameters) => PocketBaseFilter.Build(raw, parameters);

    public string Filter(string raw, object? parameters) => PocketBaseFilter.Build(raw, parameters);

    public async Task<JsonElement> SendAsync(string path, SendOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new SendOptions();
        await ApplyAutoRefreshAsync(options, cancellationToken).ConfigureAwait(false);
        return await _transport.SendAsync(path, options, cancellationToken).ConfigureAwait(false);
    }

    public async Task<T?> SendAsync<T>(string path, SendOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new SendOptions();
        await ApplyAutoRefreshAsync(options, cancellationToken).ConfigureAwait(false);
        return await _transport.SendAsync<T>(path, options, cancellationToken).ConfigureAwait(false);
    }

    internal void RegisterAutoRefresh(double threshold, Func<CancellationToken, Task> refresh, Func<CancellationToken, Task> reauthenticate)
    {
        ResetAutoRefresh();

        var oldModel = AuthStore.Record;
        var version = Interlocked.Increment(ref _autoRefreshVersion);

        var sub = AuthStore.OnChange((newToken, model) =>
        {
            var modelIdChanged = model?.Id != oldModel?.Id;
            var collectionChanged = (model?.CollectionId ?? oldModel?.CollectionId) is not null && model?.CollectionId != oldModel?.CollectionId;
            if (string.IsNullOrEmpty(newToken) || modelIdChanged || collectionChanged)
                ResetAutoRefresh();
        });

        lock (_autoRefreshSync)
        {
            _autoRefresh = new AutoRefreshState
            {
                Threshold = threshold,
                Refresh = refresh,
                Reauthenticate = reauthenticate,
                ModelId = oldModel?.Id,
                ModelCollectionId = oldModel?.CollectionId,
                Subscription = sub,
                Version = version,
            };
        }
    }

    internal void ResetAutoRefresh()
    {
        AutoRefreshState? state;
        lock (_autoRefreshSync)
        {
            state = _autoRefresh;
            _autoRefresh = null;
            Interlocked.Increment(ref _autoRefreshVersion);
        }

        state?.Subscription.Dispose();
    }

    /// <summary>Returns a new client sharing the same HttpClient/options but with a different auth store (used by impersonate).</summary>
    internal PocketBaseClient WithAuthStore(IAuthStore authStore)
    {
        if (_transport is not PocketBaseTransport concrete)
            throw new NotSupportedException($"Impersonate requires the default {nameof(PocketBaseTransport)}.");

        return new PocketBaseClient(concrete.WithAuthStore(authStore), Options);
    }

    private async Task ApplyAutoRefreshAsync(SendOptions options, CancellationToken cancellationToken)
    {
        AutoRefreshState? state;
        lock (_autoRefreshSync) state = _autoRefresh;
        if (state is null || IsInternalAutoRefreshRequest(options)) return;

        var oldToken = AuthStore.Token;

        await state.Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_autoRefresh is null || state.Version != _autoRefresh.Version || IsInternalAutoRefreshRequest(options)) return;

            var isValid = AuthStore.IsValid;
            if (isValid && PocketBaseJwt.IsTokenExpired(AuthStore.Token, state.Threshold))
            {
                try
                {
                    await state.Refresh(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    isValid = false;
                }
            }

            if (!isValid)
                await state.Reauthenticate(cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(oldToken) && !string.IsNullOrEmpty(AuthStore.Token))
            {
                foreach (var key in options.Headers.Keys.ToList())
                {
                    if (key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) && options.Headers[key] == oldToken)
                    {
                        options.Headers[key] = AuthStore.Token;
                        break;
                    }
                }
            }
        }
        finally
        {
            state.Lock.Release();
        }
    }

    private static bool IsInternalAutoRefreshRequest(SendOptions options)
    {
        if (!options.Query.TryGetValue("autoRefresh", out var value)) return false;

        return value switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            string s when double.TryParse(s, out var n) => n != 0,
            sbyte n => n != 0,
            byte n => n != 0,
            short n => n != 0,
            ushort n => n != 0,
            int n => n != 0,
            uint n => n != 0,
            long n => n != 0,
            ulong n => n != 0,
            float n => Math.Abs(n) > 0,
            double n => Math.Abs(n) > 0,
            decimal n => n != 0,
            _ => false,
        };
    }
}

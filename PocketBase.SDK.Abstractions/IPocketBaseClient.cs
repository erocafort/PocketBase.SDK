namespace PocketBase.SDK.Abstractions;

public interface IPocketBaseClient : IPocketBaseTransport
{
    /// <summary>Base PocketBase url (e.g. <c>http://127.0.0.1:8090</c>).</summary>
    string BaseUrl { get; }

    /// <summary>
    /// Returns the record service for the specified collection id or name (equivalent to <c>pb.collection()</c>).
    /// The instance is cached per client.
    /// </summary>
    IRecordService Collection(string idOrName);

    /// <summary>Creates a new transactional batch (equivalent to <c>pb.createBatch()</c>).</summary>
    IBatchService CreateBatch();

    /// <summary>
    /// Constructs a filter expression with <c>{:paramName}</c> placeholders populated from
    /// <paramref name="parameters"/>, escaping the values (equivalent to <c>pb.filter()</c>).
    /// </summary>
    string Filter(string raw, IReadOnlyDictionary<string, object?>? parameters);

    /// <summary>Same as <see cref="Filter(string, IReadOnlyDictionary{string, object?})"/> but reading the parameters from an object/anonymous type or dictionary.</summary>
    string Filter(string raw, object? parameters);

    /// <summary>Alias of <c>Collection("_superusers")</c>.</summary>
    [Obsolete("Since PocketBase v0.23 admins are regular auth records of the \"_superusers\" collection: use Collection(\"_superusers\").")]
    IRecordService Admins { get; }

    ICollectionsService Collections { get; }
    ISettingsService Settings { get; }
    IFilesService Files { get; }
    ILogsService Logs { get; }
    IBackupsService Backups { get; }
    ICronsService Crons { get; }
    ISqlService Sql { get; }
    IHealthService Health { get; }
    IRealtimeService Realtime { get; }
}

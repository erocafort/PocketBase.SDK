using System.Text.Json.Nodes;

namespace PocketBaseNet.Abstractions;

public interface ICollectionsService
{
    Task<IReadOnlyList<CollectionModel>> GetFullListAsync(FullListOptions? options = null, CancellationToken cancellationToken = default);
    Task<ResultList<CollectionModel>> GetListAsync(int page = 1, int perPage = 30, ListOptions? options = null, CancellationToken cancellationToken = default);
    Task<CollectionModel> GetFirstListItemAsync(string filter, CommonOptions? options = null, CancellationToken cancellationToken = default);
    Task<CollectionModel> GetOneAsync(string idOrName, CommonOptions? options = null, CancellationToken cancellationToken = default);
    Task<CollectionModel> CreateAsync(object body, CommonOptions? options = null, CancellationToken cancellationToken = default);
    Task<CollectionModel> UpdateAsync(string idOrName, object body, CommonOptions? options = null, CancellationToken cancellationToken = default);
    Task DeleteAsync(string idOrName, CommonOptions? options = null, CancellationToken cancellationToken = default);

    Task ImportAsync(IEnumerable<object> collections, bool deleteMissing = false, CommonOptions? options = null, CancellationToken cancellationToken = default);
    Task TruncateAsync(string idOrName, CommonOptions? options = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, CollectionModel>> GetScaffoldsAsync(CommonOptions? options = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConfigurableOAuth2Provider>> GetAllOAuth2ProvidersAsync(CommonOptions? options = null, CancellationToken cancellationToken = default);
    Task<JsonObject> DryRunViewQueryAsync(string query, CommonOptions? options = null, CancellationToken cancellationToken = default);
}

public interface ISettingsService
{
    Task<JsonObject> GetAllAsync(CommonOptions? options = null, CancellationToken cancellationToken = default);
    Task<JsonObject> UpdateAsync(object body, CommonOptions? options = null, CancellationToken cancellationToken = default);
    Task TestS3Async(string filesystem = "storage", CommonOptions? options = null, CancellationToken cancellationToken = default);
    Task TestEmailAsync(string collectionIdOrName, string toEmail, string template = "verification", CommonOptions? options = null, CancellationToken cancellationToken = default);
    Task<AppleClientSecret> GenerateAppleClientSecretAsync(string clientId, string teamId, string keyId, string privateKey, int duration = 3600, CommonOptions? options = null, CancellationToken cancellationToken = default);
}

public interface IFilesService
{
    [Obsolete("Use GetURL().")]
    string GetUrl(RecordModel record, string filename, FileOptions? options = null);

    string GetURL(RecordModel record, string filename, FileOptions? options = null);

    [Obsolete("Use GetURL().")]
    string BuildFileUrl(string collection, string recordId, string fileName, string? thumb = null, string? token = null);

    Task<string> GetTokenAsync(CommonOptions? options = null, CancellationToken cancellationToken = default);
}

public interface ILogsService
{
    Task<ResultList<LogModel>> GetListAsync(int page = 1, int perPage = 30, ListOptions? options = null, CancellationToken cancellationToken = default);
    Task<LogModel> GetOneAsync(string id, CommonOptions? options = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LogStat>> GetStatsAsync(LogStatsOptions? options = null, CancellationToken cancellationToken = default);
    Task TruncateAsync(CommonOptions? options = null, CancellationToken cancellationToken = default);
}

public interface IBackupsService
{
    Task<IReadOnlyList<BackupFileInfo>> GetFullListAsync(CommonOptions? options = null, CancellationToken cancellationToken = default);
    Task CreateAsync(string basename = "", CommonOptions? options = null, CancellationToken cancellationToken = default);
    Task UploadAsync(object body, CommonOptions? options = null, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CommonOptions? options = null, CancellationToken cancellationToken = default);
    Task RestoreAsync(string key, CommonOptions? options = null, CancellationToken cancellationToken = default);

    [Obsolete("Use GetDownloadURL().")]
    string GetDownloadUrl(string token, string key, IDictionary<string, object?>? query = null);

    string GetDownloadURL(string token, string key, IDictionary<string, object?>? query = null);
}

public interface ICronsService
{
    Task<IReadOnlyList<CronJob>> GetFullListAsync(CommonOptions? options = null, CancellationToken cancellationToken = default);
    Task RunAsync(string id, CommonOptions? options = null, CancellationToken cancellationToken = default);
}

public interface ISqlService
{
    Task<SqlResult> RunAsync(string query, CommonOptions? options = null, CancellationToken cancellationToken = default);
}

public interface IHealthService
{
    Task<HealthCheck> CheckAsync(CommonOptions? options = null, CancellationToken cancellationToken = default);
}

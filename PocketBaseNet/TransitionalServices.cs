using System.Text.Json;
using System.Text.Json.Nodes;
using PocketBaseNet.Abstractions;
using FileUrlOptions = PocketBaseNet.Abstractions.FileOptions;

namespace PocketBaseNet;

internal static class ServiceJson
{
    public static T Decode<T>(JsonElement data)
    {
        var value = data.Deserialize<T>();
        return value is null ? throw new JsonException($"Unable to deserialize {typeof(T).Name}.") : value;
    }

    public static JsonElement NotFound(string message) => JsonSerializer.SerializeToElement(new
    {
        code = 404,
        message,
        data = new { },
    });
}

public sealed class CollectionsService(IPocketBaseTransport client) : ICollectionsService
{
    private const string BasePath = "/api/collections";

    public async Task<IReadOnlyList<CollectionModel>> GetFullListAsync(FullListOptions? options = null, CancellationToken cancellationToken = default)
    {
        var batch = options?.Batch is > 0 ? options.Batch.Value : 500;
        var listOptions = new ListOptions
        {
            Fields = options?.Fields,
            Filter = options?.Filter,
            Sort = options?.Sort,
            SkipTotal = options?.SkipTotal,
        };

        if (options is not null)
        {
            foreach (var (k, v) in options.Query) listOptions.Query[k] = v;
            foreach (var (k, v) in options.Headers) listOptions.Headers[k] = v;
        }

        if (!listOptions.Query.ContainsKey("skipTotal") && listOptions.SkipTotal is null) listOptions.Query["skipTotal"] = 1;

        var result = new List<CollectionModel>();
        for (var page = 1; ; page++)
        {
            var list = await GetListAsync(page, batch, listOptions, cancellationToken).ConfigureAwait(false);
            result.AddRange(list.Items);
            if (list.Items.Count != list.PerPage) break;
        }

        return result;
    }

    public async Task<ResultList<CollectionModel>> GetListAsync(int page = 1, int perPage = 30, ListOptions? options = null, CancellationToken cancellationToken = default)
    {
        var send = RequestBuilder.Build(HttpMethod.Get, options, defaultQuery: new Dictionary<string, object?>
        {
            ["page"] = page,
            ["perPage"] = perPage,
        });

        var data = await client.SendAsync(BasePath, send, cancellationToken).ConfigureAwait(false);
        return ServiceJson.Decode<ResultList<CollectionModel>>(data);
    }

    public async Task<CollectionModel> GetFirstListItemAsync(string filter, CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        var send = RequestBuilder.Build(HttpMethod.Get, options, defaultQuery: new Dictionary<string, object?>
        {
            ["page"] = 1,
            ["perPage"] = 1,
            ["filter"] = filter,
            ["skipTotal"] = 1,
        });

        var data = await client.SendAsync(BasePath, send, cancellationToken).ConfigureAwait(false);
        var list = ServiceJson.Decode<ResultList<CollectionModel>>(data);

        if (list.Items.Count == 0)
            throw new PocketBaseException(string.Empty, 404, ServiceJson.NotFound("The requested resource wasn't found."));

        return list.Items[0];
    }

    public async Task<CollectionModel> GetOneAsync(string idOrName, CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(idOrName))
            throw new PocketBaseException(string.Empty, 404, ServiceJson.NotFound("Missing required record id."));

        var send = RequestBuilder.Build(HttpMethod.Get, options);
        var data = await client.SendAsync($"{BasePath}/{Uri.EscapeDataString(idOrName)}", send, cancellationToken).ConfigureAwait(false);
        return ServiceJson.Decode<CollectionModel>(data);
    }

    public async Task<CollectionModel> CreateAsync(object body, CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        var send = RequestBuilder.Build(HttpMethod.Post, options, body: null);
        send.Body = body;
        var data = await client.SendAsync(BasePath, send, cancellationToken).ConfigureAwait(false);
        return ServiceJson.Decode<CollectionModel>(data);
    }

    public async Task<CollectionModel> UpdateAsync(string idOrName, object body, CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(idOrName))
            throw new PocketBaseException(string.Empty, 404, ServiceJson.NotFound("Missing required record id."));

        var send = RequestBuilder.Build(HttpMethod.Patch, options, body: null);
        send.Body = body;
        var data = await client.SendAsync($"{BasePath}/{Uri.EscapeDataString(idOrName)}", send, cancellationToken).ConfigureAwait(false);
        return ServiceJson.Decode<CollectionModel>(data);
    }

    public Task DeleteAsync(string idOrName, CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(idOrName))
            throw new PocketBaseException(string.Empty, 404, ServiceJson.NotFound("Missing required record id."));

        return client.SendAsync($"{BasePath}/{Uri.EscapeDataString(idOrName)}", RequestBuilder.Build(HttpMethod.Delete, options), cancellationToken);
    }

    public Task ImportAsync(IEnumerable<object> collections, bool deleteMissing = false, CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        var send = RequestBuilder.Build(HttpMethod.Put, options, new Dictionary<string, object?>
        {
            ["collections"] = collections,
            ["deleteMissing"] = deleteMissing,
        });

        return client.SendAsync(BasePath + "/import", send, cancellationToken);
    }

    public Task TruncateAsync(string idOrName, CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(idOrName))
            throw new PocketBaseException(string.Empty, 404, ServiceJson.NotFound("Missing required record id."));

        return client.SendAsync($"{BasePath}/{Uri.EscapeDataString(idOrName)}/truncate", RequestBuilder.Build(HttpMethod.Delete, options), cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, CollectionModel>> GetScaffoldsAsync(CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        var data = await client.SendAsync($"{BasePath}/meta/scaffolds", RequestBuilder.Build(HttpMethod.Get, options), cancellationToken).ConfigureAwait(false);
        return ServiceJson.Decode<Dictionary<string, CollectionModel>>(data);
    }

    public async Task<IReadOnlyList<ConfigurableOAuth2Provider>> GetAllOAuth2ProvidersAsync(CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        var data = await client.SendAsync($"{BasePath}/meta/oauth2-providers", RequestBuilder.Build(HttpMethod.Get, options), cancellationToken).ConfigureAwait(false);
        return ServiceJson.Decode<List<ConfigurableOAuth2Provider>>(data);
    }

    public async Task<JsonObject> DryRunViewQueryAsync(string query, CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        var send = RequestBuilder.Build(HttpMethod.Post, options, new Dictionary<string, object?> { ["query"] = query });
        var data = await client.SendAsync($"{BasePath}/meta/dry-run-view", send, cancellationToken).ConfigureAwait(false);
        return JsonNode.Parse(data.GetRawText()) as JsonObject ?? [];
    }
}

public sealed class SettingsService(IPocketBaseTransport client) : ISettingsService
{
    public async Task<JsonObject> GetAllAsync(CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        var data = await client.SendAsync("/api/settings", RequestBuilder.Build(HttpMethod.Get, options), cancellationToken).ConfigureAwait(false);
        return JsonNode.Parse(data.GetRawText()) as JsonObject ?? [];
    }

    public async Task<JsonObject> UpdateAsync(object body, CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        var send = RequestBuilder.Build(HttpMethod.Patch, options, body: null);
        send.Body = body;
        var data = await client.SendAsync("/api/settings", send, cancellationToken).ConfigureAwait(false);
        return JsonNode.Parse(data.GetRawText()) as JsonObject ?? [];
    }

    public Task TestS3Async(string filesystem = "storage", CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        var send = RequestBuilder.Build(HttpMethod.Post, options, new Dictionary<string, object?> { ["filesystem"] = filesystem });
        return client.SendAsync("/api/settings/test/s3", send, cancellationToken);
    }

    public Task TestEmailAsync(string collectionIdOrName, string toEmail, string template = "verification", CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        var send = RequestBuilder.Build(HttpMethod.Post, options, new Dictionary<string, object?>
        {
            ["collection"] = collectionIdOrName,
            ["email"] = toEmail,
            ["template"] = template,
        });

        return client.SendAsync("/api/settings/test/email", send, cancellationToken);
    }

    public async Task<AppleClientSecret> GenerateAppleClientSecretAsync(string clientId, string teamId, string keyId, string privateKey, int duration = 3600, CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        var send = RequestBuilder.Build(HttpMethod.Post, options, new Dictionary<string, object?>
        {
            ["clientId"] = clientId,
            ["teamId"] = teamId,
            ["keyId"] = keyId,
            ["privateKey"] = privateKey,
            ["duration"] = duration,
        });

        var data = await client.SendAsync("/api/settings/apple/generate-client-secret", send, cancellationToken).ConfigureAwait(false);
        return ServiceJson.Decode<AppleClientSecret>(data);
    }
}

public sealed class FilesService(IPocketBaseTransport client) : IFilesService
{
    [Obsolete("Use GetURL().")]
    public string GetUrl(RecordModel record, string filename, FileUrlOptions? options = null) => GetURL(record, filename, options);

    public string GetURL(RecordModel record, string filename, FileUrlOptions? options = null)
    {
        var collection = record.CollectionId;
        if (string.IsNullOrEmpty(collection)) collection = record.CollectionName;
        return BuildFileUrl(collection, record.Id, filename, options?.Thumb, options?.Token, options);
    }

    [Obsolete("Use GetURL().")]
    public string BuildFileUrl(string collection, string recordId, string fileName, string? thumb = null, string? token = null) =>
        BuildFileUrl(collection, recordId, fileName, thumb, token, null);

    private string BuildFileUrl(string collection, string recordId, string fileName, string? thumb, string? token, FileUrlOptions? options)
    {
        if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(recordId) || string.IsNullOrEmpty(collection)) return string.Empty;

        var url = client.BuildUrl(string.Join("/", "api", "files",
            Uri.EscapeDataString(collection), Uri.EscapeDataString(recordId), Uri.EscapeDataString(fileName)));

        var query = new Dictionary<string, object?>(options?.Query ?? new Dictionary<string, object?>())
        {
            ["thumb"] = thumb,
            ["token"] = token,
        };

        // same as Dart SDK: download=true maps to download=""
        if (options?.Download == true) query["download"] = "";
        else query.Remove("download");

        var paramsValue = PocketBaseQuery.Serialize(query, new JsonSerializerOptions());
        if (paramsValue.Length > 0) url += (url.Contains('?') ? "&" : "?") + paramsValue;

        return url;
    }

    public async Task<string> GetTokenAsync(CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        var data = await client.SendAsync("/api/files/token", RequestBuilder.Build(HttpMethod.Post, options), cancellationToken).ConfigureAwait(false);
        return data.TryGetProperty("token", out var token) && token.ValueKind == JsonValueKind.String ? token.GetString() ?? "" : "";
    }
}

public sealed class LogsService(IPocketBaseTransport client) : ILogsService
{
    public async Task<ResultList<LogModel>> GetListAsync(int page = 1, int perPage = 30, ListOptions? options = null, CancellationToken cancellationToken = default)
    {
        var send = RequestBuilder.Build(HttpMethod.Get, options, defaultQuery: new Dictionary<string, object?>
        {
            ["page"] = page,
            ["perPage"] = perPage,
        });

        var data = await client.SendAsync("/api/logs", send, cancellationToken).ConfigureAwait(false);
        return ServiceJson.Decode<ResultList<LogModel>>(data);
    }

    public async Task<LogModel> GetOneAsync(string id, CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(id))
            throw new PocketBaseException(string.Empty, 404, ServiceJson.NotFound("Missing required log id."));

        var data = await client.SendAsync($"/api/logs/{Uri.EscapeDataString(id)}", RequestBuilder.Build(HttpMethod.Get, options), cancellationToken).ConfigureAwait(false);
        return ServiceJson.Decode<LogModel>(data);
    }

    public async Task<IReadOnlyList<LogStat>> GetStatsAsync(LogStatsOptions? options = null, CancellationToken cancellationToken = default)
    {
        var data = await client.SendAsync("/api/logs/stats", RequestBuilder.Build(HttpMethod.Get, options), cancellationToken).ConfigureAwait(false);
        return ServiceJson.Decode<List<LogStat>>(data);
    }

    public Task TruncateAsync(CommonOptions? options = null, CancellationToken cancellationToken = default) =>
        client.SendAsync("/api/logs", RequestBuilder.Build(HttpMethod.Delete, options), cancellationToken);
}

public sealed class BackupsService(IPocketBaseTransport client) : IBackupsService
{
    public async Task<IReadOnlyList<BackupFileInfo>> GetFullListAsync(CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        var data = await client.SendAsync("/api/backups", RequestBuilder.Build(HttpMethod.Get, options), cancellationToken).ConfigureAwait(false);
        return ServiceJson.Decode<List<BackupFileInfo>>(data);
    }

    public Task CreateAsync(string basename = "", CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        var send = RequestBuilder.Build(HttpMethod.Post, options, new Dictionary<string, object?> { ["name"] = basename });
        return client.SendAsync("/api/backups", send, cancellationToken);
    }

    public Task UploadAsync(object body, CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        var send = RequestBuilder.Build(HttpMethod.Post, options, body: null);
        send.Body = body;
        return client.SendAsync("/api/backups/upload", send, cancellationToken);
    }

    public Task DeleteAsync(string key, CommonOptions? options = null, CancellationToken cancellationToken = default) =>
        client.SendAsync($"/api/backups/{Uri.EscapeDataString(key)}", RequestBuilder.Build(HttpMethod.Delete, options), cancellationToken);

    public Task RestoreAsync(string key, CommonOptions? options = null, CancellationToken cancellationToken = default) =>
        client.SendAsync($"/api/backups/{Uri.EscapeDataString(key)}/restore", RequestBuilder.Build(HttpMethod.Post, options), cancellationToken);

    [Obsolete("Use GetDownloadURL().")]
    public string GetDownloadUrl(string token, string key, IDictionary<string, object?>? query = null) => GetDownloadURL(token, key, query);

    public string GetDownloadURL(string token, string key, IDictionary<string, object?>? query = null)
    {
        var parameters = new Dictionary<string, object?>(query ?? new Dictionary<string, object?>());
        if (!parameters.TryGetValue("token", out var existing) || existing is null) parameters["token"] = token;

        var url = client.BuildUrl($"/api/backups/{Uri.EscapeDataString(key)}");
        var queryValue = PocketBaseQuery.Serialize(parameters, new JsonSerializerOptions());
        if (queryValue.Length > 0) url += (url.Contains('?') ? "&" : "?") + queryValue;

        return url;
    }
}

public sealed class CronsService(IPocketBaseTransport client) : ICronsService
{
    public async Task<IReadOnlyList<CronJob>> GetFullListAsync(CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        var data = await client.SendAsync("/api/crons", RequestBuilder.Build(HttpMethod.Get, options), cancellationToken).ConfigureAwait(false);
        return ServiceJson.Decode<List<CronJob>>(data);
    }

    public Task RunAsync(string id, CommonOptions? options = null, CancellationToken cancellationToken = default) =>
        client.SendAsync($"/api/crons/{Uri.EscapeDataString(id)}", RequestBuilder.Build(HttpMethod.Post, options), cancellationToken);
}

public sealed class SqlService(IPocketBaseTransport client) : ISqlService
{
    public async Task<SqlResult> RunAsync(string query, CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        var send = RequestBuilder.Build(HttpMethod.Post, options, new Dictionary<string, object?> { ["query"] = query });
        var data = await client.SendAsync("/api/sql", send, cancellationToken).ConfigureAwait(false);
        return ServiceJson.Decode<SqlResult>(data);
    }
}

public sealed class HealthService(IPocketBaseTransport client) : IHealthService
{
    public async Task<HealthCheck> CheckAsync(CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        var data = await client.SendAsync("/api/health", RequestBuilder.Build(HttpMethod.Get, options), cancellationToken).ConfigureAwait(false);
        return ServiceJson.Decode<HealthCheck>(data);
    }
}

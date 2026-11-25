using System.Collections;
using System.Text;
using System.Text.Json;
using PocketBaseNet.Abstractions;

namespace PocketBaseNet;

/// <summary>Port of the official SDKs <c>BatchService</c>.</summary>
public sealed class BatchService : IBatchService
{
    private readonly PocketBaseClient _client;
    private readonly List<BatchRequest> _requests = new();
    private readonly Dictionary<string, SubBatchService> _subs = new();

    internal BatchService(PocketBaseClient client) => _client = client;

    public ISubBatchService Collection(string collectionIdOrName)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionIdOrName);

        if (!_subs.TryGetValue(collectionIdOrName, out var sub))
        {
            sub = new SubBatchService(_requests, collectionIdOrName, _client.Options.JsonSerializerOptions);
            _subs[collectionIdOrName] = sub;
        }

        return sub;
    }

    public async Task<IReadOnlyList<BatchResult>> SendAsync(CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        var json = _client.Options.JsonSerializerOptions;
        var form = new MultipartFormDataContent();
        var jsonData = new List<Dictionary<string, object?>>();

        for (var i = 0; i < _requests.Count; i++)
        {
            var req = _requests[i];

            var entry = new Dictionary<string, object?>
            {
                ["method"] = req.Method,
                ["url"] = req.Url,
            };
            if (req.Headers is not null) entry["headers"] = req.Headers;
            entry["body"] = req.Json;
            jsonData.Add(entry);

            foreach (var (key, files) in req.Files)
            {
                foreach (var file in files) PocketBaseFormData.AddValue(form, "requests." + i + "." + key, file, json);
            }
        }

        form.Add(new StringContent(JsonSerializer.Serialize(new Dictionary<string, object?> { ["requests"] = jsonData }, json), Encoding.UTF8), "@jsonPayload");

        var send = RequestBuilder.Build(HttpMethod.Post, options);
        send.Body = form;

        var data = await _client.SendAsync("/api/batch", send, cancellationToken).ConfigureAwait(false);

        return data.ValueKind == JsonValueKind.Array
            ? data.Deserialize<List<BatchResult>>(json) ?? []
            : [];
    }

    internal sealed class BatchRequest
    {
        public required string Method { get; init; }
        public required string Url { get; set; }
        public IDictionary<string, string>? Headers { get; set; }
        public Dictionary<string, object?> Json { get; } = new();
        public Dictionary<string, List<FileUpload>> Files { get; } = new();
    }

    private sealed class SubBatchService(List<BatchRequest> requests, string collectionIdOrName, JsonSerializerOptions json) : ISubBatchService
    {
        private string RecordsUrl => "/api/collections/" + Uri.EscapeDataString(collectionIdOrName) + "/records";

        public void Upsert(object? body = null, RecordOptions? options = null) =>
            Add("PUT", RecordsUrl, body, options);

        public void Create(object? body = null, RecordOptions? options = null) =>
            Add("POST", RecordsUrl, body, options);

        public void Update(string id, object? body = null, RecordOptions? options = null) =>
            Add("PATCH", RecordsUrl + "/" + Uri.EscapeDataString(id), body, options);

        public void Delete(string id, CommonOptions? options = null) =>
            Add("DELETE", RecordsUrl + "/" + Uri.EscapeDataString(id), null, options);

        private void Add(string method, string url, object? body, CommonOptions? options)
        {
            var request = new BatchRequest { Method = method, Url = url };

            if (options is not null)
            {
                if (options.Headers.Count > 0) request.Headers = new Dictionary<string, string>(options.Headers);

                var query = new Dictionary<string, object?>(options.Query);
                RequestBuilder.ApplyTypedQuery(query, options);

                var serialized = PocketBaseQuery.Serialize(query, json);
                if (serialized.Length > 0) request.Url += (request.Url.Contains('?') ? "&" : "?") + serialized;
            }

            if (body is HttpContent)
            {
                // the JS SDK converts a FormData body back to an object; .NET multipart content can't be read back reliably
                throw new ArgumentException("Batch request bodies must be objects/dictionaries (use FileUpload values for files), not HttpContent.", nameof(body));
            }

            var fields = PocketBaseFormData.GetFields(body, json);
            if (body is not null && fields is null)
            {
                throw new ArgumentException("Batch request bodies must be objects or dictionaries.", nameof(body));
            }

            // extract json and files body data
            foreach (var (key, val) in fields ?? [])
            {
                if (val is FileUpload file)
                {
                    AddFiles(request, key, [file]);
                }
                else if (val is IEnumerable enumerable and not string and not IDictionary && val is not JsonElement && val is not System.Text.Json.Nodes.JsonNode)
                {
                    var items = enumerable.Cast<object?>().ToList();
                    var foundFiles = items.OfType<FileUpload>().ToList();
                    var foundRegular = items.Where(v => v is not FileUpload).ToList();

                    if (foundFiles.Count > 0 && foundFiles.Count == items.Count)
                    {
                        // only files
                        AddFiles(request, key, foundFiles);
                    }
                    else
                    {
                        // empty or mixed array (both regular and file values)
                        request.Json[key] = foundRegular;

                        if (foundFiles.Count > 0)
                        {
                            // add "+" to append, since otherwise the existing regular files will be deleted
                            // (the mixed values order is preserved only within their corresponding groups)
                            var fileKey = key.StartsWith('+') || key.EndsWith('+') ? key : key + "+";
                            AddFiles(request, fileKey, foundFiles);
                        }
                    }
                }
                else
                {
                    request.Json[key] = val;
                }
            }

            requests.Add(request);
        }

        private static void AddFiles(BatchRequest request, string key, IEnumerable<FileUpload> files)
        {
            if (!request.Files.TryGetValue(key, out var list))
            {
                list = new List<FileUpload>();
                request.Files[key] = list;
            }
            list.AddRange(files);
        }
    }
}

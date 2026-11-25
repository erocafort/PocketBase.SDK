using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PocketBase.SDK.Abstractions;

namespace PocketBase.SDK;

/// <summary>
/// Request pipeline shared by all services: port of the SDKs <c>pb.send()</c> / <c>pb.buildURL()</c>.
/// Registered as a typed <see cref="HttpClient"/>; to customize requests/responses
/// (equivalent of the JS <c>beforeSend</c>/<c>afterSend</c> hooks) add a <see cref="DelegatingHandler"/>
/// to the <c>IHttpClientBuilder</c> returned by <c>AddPocketBase</c>.
/// </summary>
public sealed class PocketBaseTransport : IPocketBaseTransport
{
    private readonly HttpClient _http;
    private readonly PocketBaseOptions _options;

    public PocketBaseTransport(HttpClient http, PocketBaseOptions options, IAuthStore authStore)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        AuthStore = authStore ?? throw new ArgumentNullException(nameof(authStore));
    }

    public IAuthStore AuthStore { get; }

    internal PocketBaseOptions Options => _options;

    /// <summary>Creates a new transport sharing the same HttpClient and options but with a different auth store (used by impersonate).</summary>
    internal PocketBaseTransport WithAuthStore(IAuthStore authStore) => new(_http, _options, authStore);

    public string BuildUrl(string path)
    {
        var url = _options.BaseUrl;

        if (!string.IsNullOrEmpty(path))
        {
            url += url.EndsWith('/') ? "" : "/";
            url += path.StartsWith('/') ? path[1..] : path;
        }

        return url;
    }

    public async Task<T?> SendAsync<T>(string path, SendOptions? options = null, CancellationToken cancellationToken = default)
    {
        var data = await SendAsync(path, options, cancellationToken).ConfigureAwait(false);

        if (data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return default;
        if (typeof(T) == typeof(JsonElement)) return (T)(object)data;

        return data.Deserialize<T>(_options.JsonSerializerOptions);
    }

    public async Task<JsonElement> SendAsync(string path, SendOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new SendOptions();

        // HttpClient doesn't check an already cancelled token consistently across runtimes
        cancellationToken.ThrowIfCancellationRequested();

        var url = BuildUrl(path);
        var query = PocketBaseQuery.Serialize(options.Query, _options.JsonSerializerOptions);
        if (query.Length > 0) url += (url.Contains('?') ? "&" : "?") + query;

        using var request = new HttpRequestMessage(options.Method, url);
        request.Content = CreateContent(options.Body);

        foreach (var (name, value) in options.Headers)
        {
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Content is not null) request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(value);
                continue;
            }

            if (!request.Headers.TryAddWithoutValidation(name, value))
                request.Content?.Headers.TryAddWithoutValidation(name, value);
        }

        if (!options.Headers.ContainsKey("Accept-Language"))
            request.Headers.TryAddWithoutValidation("Accept-Language", _options.Lang);

        // Same as the Dart SDK (and the JS SDK v0.29 changelog): the token is sent only if still valid,
        // otherwise the expired auth state is cleared (when AutoClearAuthStore is enabled).
        if (!options.Headers.ContainsKey("Authorization"))
        {
            if (AuthStore.IsValid)
                request.Headers.TryAddWithoutValidation("Authorization", AuthStore.Token); // raw token, no "Bearer" scheme
            else if (_options.AutoClearAuthStore && AuthStore.Token.Length > 0)
                AuthStore.Clear();
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // cancelled by the caller: keep the standard .NET behavior
        }
        catch (OperationCanceledException e)
        {
            throw new PocketBaseException(url, 0, default, isAbort: true, e); // e.g. HttpClient.Timeout
        }
        catch (HttpRequestException e)
        {
            throw new PocketBaseException(url, 0, default, isAbort: false, e);
        }

        using (response)
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var data = ParseBody(raw);
            var status = (int)response.StatusCode;

            if (status >= 400)
            {
                if (_options.AutoClearAuthStore && status == 401) AuthStore.Clear();

                throw new PocketBaseException(response.RequestMessage?.RequestUri?.ToString() ?? url, status, data);
            }

            return data;
        }
    }

    /// <summary>
    /// Opens the realtime "text/event-stream" response (headers read only, the body is streamed).
    /// Same as the browser EventSource / Dart SseClient: no Authorization header is sent
    /// (the realtime client is authorized by the subscriptions POST).
    /// </summary>
    internal async Task<HttpResponseMessage> OpenEventStreamAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var url = BuildUrl(path);
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

        HttpResponseMessage response;
        try
        {
            // with ResponseHeadersRead HttpClient.Timeout only applies until the headers are received
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            request.Dispose();
            throw;
        }
        catch (OperationCanceledException e)
        {
            request.Dispose();
            throw new PocketBaseException(url, 0, default, isAbort: true, e);
        }
        catch (HttpRequestException e)
        {
            request.Dispose();
            throw new PocketBaseException(url, 0, default, isAbort: false, e);
        }

        var status = (int)response.StatusCode;
        if (status >= 400)
        {
            using (response)
            {
                var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new PocketBaseException(url, status, ParseBody(raw));
            }
        }

        return response;
    }

    private HttpContent? CreateContent(object? body) => body switch
    {
        null => null,
        HttpContent content => content,
        string s => new StringContent(s, Encoding.UTF8, "application/json"),
        // auto convert to multipart/form-data if the body has FileUpload values (same as the SDKs)
        _ => PocketBaseFormData.ConvertIfNeeded(body, _options.JsonSerializerOptions)
            ?? (HttpContent)new StringContent(JsonSerializer.Serialize(body, body.GetType(), _options.JsonSerializerOptions), Encoding.UTF8, "application/json"),
    };

    private static JsonElement ParseBody(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return default;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            // custom non-json response (same as the Dart SDK: returned as string)
            return JsonSerializer.SerializeToElement(raw);
        }
    }
}

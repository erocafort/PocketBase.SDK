using System.Text.Json;
using System.Text.Json.Nodes;
using PocketBase.SDK.Abstractions;

namespace PocketBase.SDK;

/// <summary>Port of the official SDKs <c>RecordService</c> (CRUD + auth). Obtain it with <c>pb.Collection("name")</c>.</summary>
public sealed class RecordService : IRecordService
{
    private readonly PocketBaseClient _client;

    internal RecordService(PocketBaseClient client, string collectionIdOrName)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionIdOrName);
        _client = client;
        CollectionIdOrName = collectionIdOrName;
    }

    public string CollectionIdOrName { get; }

    public string BaseCollectionPath => "/api/collections/" + Uri.EscapeDataString(CollectionIdOrName);

    public string BaseCrudPath => BaseCollectionPath + "/records";

    private IAuthStore AuthStore => _client.AuthStore;

    // -----------------------------------------------------------------
    // Realtime
    // -----------------------------------------------------------------

    public Task<UnsubscribeFunc> SubscribeAsync(string topic, Action<RecordSubscriptionEvent<RecordModel>> callback, RealtimeSubscribeOptions? options = null, CancellationToken cancellationToken = default) =>
        SubscribeAsync<RecordModel>(topic, callback, options, cancellationToken);

    public Task<UnsubscribeFunc> SubscribeAsync(string topic, Func<RecordSubscriptionEvent<RecordModel>, Task> callback, RealtimeSubscribeOptions? options = null, CancellationToken cancellationToken = default) =>
        SubscribeAsync<RecordModel>(topic, callback, options, cancellationToken);

    public Task<UnsubscribeFunc> SubscribeAsync<T>(string topic, Action<RecordSubscriptionEvent<T>> callback, RealtimeSubscribeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return SubscribeCoreAsync<T>(topic, callback, e => { callback(e); return Task.CompletedTask; }, options, cancellationToken);
    }

    public Task<UnsubscribeFunc> SubscribeAsync<T>(string topic, Func<RecordSubscriptionEvent<T>, Task> callback, RealtimeSubscribeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return SubscribeCoreAsync<T>(topic, callback, callback, options, cancellationToken);
    }

    private Task<UnsubscribeFunc> SubscribeCoreAsync<T>(string topic, Delegate original, Func<RecordSubscriptionEvent<T>, Task> handler, RealtimeSubscribeOptions? options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(topic)) throw new ArgumentException("Missing topic.", nameof(topic));

        var fullTopic = CollectionIdOrName + "/" + topic;
        Func<SseMessage, Task> listener = e => handler(DecodeSubscriptionEvent<T>(e));

        if (_client.Realtime is RealtimeService realtime)
        {
            // keeps the user delegate as listener identity (for UnsubscribeByTopicAndListenerAsync)
            return realtime.SubscribeCoreAsync(fullTopic, new RealtimeService.Listener(original, listener), options, cancellationToken);
        }

        return _client.Realtime.SubscribeAsync(fullTopic, listener, options, cancellationToken);
    }

    /// <summary>Same as the Dart SDK <c>RecordSubscriptionEvent.fromJson(e.jsonData())</c>.</summary>
    private RecordSubscriptionEvent<T> DecodeSubscriptionEvent<T>(SseMessage e)
    {
        var data = e.JsonData();
        var record = data["record"];

        return new RecordSubscriptionEvent<T>
        {
            Action = data["action"] is JsonValue a && a.TryGetValue<string>(out var action) ? action : "",
            Record = record is JsonObject ? Decode<T>(JsonSerializer.SerializeToElement(record)) : default,
        };
    }

    public Task UnsubscribeAsync(string? topic = null)
    {
        // unsubscribe from the specified topic
        if (!string.IsNullOrEmpty(topic)) return _client.Realtime.UnsubscribeAsync(CollectionIdOrName + "/" + topic);

        // unsubscribe from everything related to the collection
        // (the SDKs use only the collection name as prefix, which would also match e.g. "posts2/*" when unsubscribing "posts")
        return _client.Realtime.UnsubscribeByPrefixAsync(CollectionIdOrName + "/");
    }

    // -----------------------------------------------------------------
    // CRUD
    // -----------------------------------------------------------------

    public Task<IReadOnlyList<RecordModel>> GetFullListAsync(RecordFullListOptions? options = null, CancellationToken cancellationToken = default) =>
        GetFullListAsync<RecordModel>(options, cancellationToken);

    public async Task<IReadOnlyList<T>> GetFullListAsync<T>(RecordFullListOptions? options = null, CancellationToken cancellationToken = default)
    {
        var batch = options?.Batch is > 0 ? options.Batch.Value : 1000;
        var listOptions = RequestBuilder.ToListOptions(options);

        // same as the SDKs: skip the total counts query by default
        if (!listOptions.Query.ContainsKey("skipTotal") && listOptions.SkipTotal is null) listOptions.Query["skipTotal"] = 1;

        var result = new List<T>();
        for (var page = 1; ; page++)
        {
            var list = await GetListAsync<T>(page, batch, listOptions, cancellationToken).ConfigureAwait(false);
            result.AddRange(list.Items);

            if (list.Items.Count != list.PerPage) break;
        }

        return result;
    }

    public Task<ResultList<RecordModel>> GetListAsync(int page = 1, int perPage = 30, RecordListOptions? options = null, CancellationToken cancellationToken = default) =>
        GetListAsync<RecordModel>(page, perPage, options, cancellationToken);

    public async Task<ResultList<T>> GetListAsync<T>(int page = 1, int perPage = 30, RecordListOptions? options = null, CancellationToken cancellationToken = default)
    {
        var send = RequestBuilder.Build(HttpMethod.Get, options, defaultQuery: new Dictionary<string, object?>
        {
            ["page"] = page,
            ["perPage"] = perPage,
        });

        var data = await _client.SendAsync(BaseCrudPath, send, cancellationToken).ConfigureAwait(false);

        return Decode<ResultList<T>>(data) ?? new ResultList<T>();
    }

    public Task<RecordModel> GetFirstListItemAsync(string filter, RecordListOptions? options = null, CancellationToken cancellationToken = default) =>
        GetFirstListItemAsync<RecordModel>(filter, options, cancellationToken);

    public async Task<T> GetFirstListItemAsync<T>(string filter, RecordListOptions? options = null, CancellationToken cancellationToken = default)
    {
        var send = RequestBuilder.Build(HttpMethod.Get, options, defaultQuery: new Dictionary<string, object?>
        {
            ["page"] = 1,
            ["perPage"] = 1,
            ["filter"] = filter,
            ["skipTotal"] = 1,
        });

        var data = await _client.SendAsync(BaseCrudPath, send, cancellationToken).ConfigureAwait(false);
        var list = Decode<ResultList<T>>(data);

        if (list is null || list.Items.Count == 0)
        {
            throw new PocketBaseException(string.Empty, 404, NotFound("The requested resource wasn't found."));
        }

        return list.Items[0];
    }

    public Task<RecordModel> GetOneAsync(string id, RecordOptions? options = null, CancellationToken cancellationToken = default) =>
        GetOneAsync<RecordModel>(id, options, cancellationToken);

    public async Task<T> GetOneAsync<T>(string id, RecordOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new PocketBaseException(_client.BuildUrl(BaseCrudPath + "/"), 404, NotFound("Missing required record id."));
        }

        var data = await _client.SendAsync(RecordPath(id), RequestBuilder.Build(HttpMethod.Get, options), cancellationToken).ConfigureAwait(false);

        return Decode<T>(data)!;
    }

    public Task<RecordModel> CreateAsync(object? body = null, RecordOptions? options = null, CancellationToken cancellationToken = default) =>
        CreateAsync<RecordModel>(body, options, cancellationToken);

    public async Task<T> CreateAsync<T>(object? body = null, RecordOptions? options = null, CancellationToken cancellationToken = default)
    {
        var send = RequestBuilder.Build(HttpMethod.Post, options);
        send.Body = body;

        var data = await _client.SendAsync(BaseCrudPath, send, cancellationToken).ConfigureAwait(false);

        return Decode<T>(data)!;
    }

    public Task<RecordModel> UpdateAsync(string id, object? body = null, RecordOptions? options = null, CancellationToken cancellationToken = default) =>
        UpdateAsync<RecordModel>(id, body, options, cancellationToken);

    public async Task<T> UpdateAsync<T>(string id, object? body = null, RecordOptions? options = null, CancellationToken cancellationToken = default)
    {
        var send = RequestBuilder.Build(HttpMethod.Patch, options);
        send.Body = body;

        var data = await _client.SendAsync(RecordPath(id), send, cancellationToken).ConfigureAwait(false);

        var item = RecordModel.FromJson(data);
        var authRecord = AuthStore.Record;

        if (authRecord is not null && authRecord.Id == item.Id && IsCurrentCollection(authRecord))
        {
            // merge the response fields with the current auth record (for now only the top-level expand keys)
            var merged = authRecord.Clone();
            var oldExpand = merged.Data["expand"] as JsonObject;

            foreach (var (k, v) in item.Data) merged.Data[k] = v?.DeepClone();

            if (oldExpand is { Count: > 0 })
            {
                var expand = (JsonObject)oldExpand.DeepClone();
                if (item.Data["expand"] is JsonObject newExpand)
                {
                    foreach (var (k, v) in newExpand) expand[k] = v?.DeepClone();
                }
                merged.Data["expand"] = expand;
            }

            AuthStore.Save(AuthStore.Token, merged);
        }

        return Decode<T>(data)!;
    }

    public async Task DeleteAsync(string id, CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        await _client.SendAsync(RecordPath(id), RequestBuilder.Build(HttpMethod.Delete, options), cancellationToken).ConfigureAwait(false);

        var authRecord = AuthStore.Record;
        if (authRecord is not null && authRecord.Id == id && IsCurrentCollection(authRecord))
        {
            AuthStore.Clear();
        }
    }

    // -----------------------------------------------------------------
    // Auth
    // -----------------------------------------------------------------

    public async Task<AuthMethodsList> ListAuthMethodsAsync(CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        // @todo (official SDKs) remove after deleting the pre v0.23 API response fields
        var send = RequestBuilder.Build(HttpMethod.Get, options, defaultQuery: new Dictionary<string, object?>
        {
            ["fields"] = "mfa,otp,password,oauth2",
        });

        var data = await _client.SendAsync(BaseCollectionPath + "/auth-methods", send, cancellationToken).ConfigureAwait(false);

        return Decode<AuthMethodsList>(data) ?? new AuthMethodsList();
    }

    public Task<RecordAuthResponse<RecordModel>> AuthWithPasswordAsync(string usernameOrEmail, string password, RecordOptions? options = null, CancellationToken cancellationToken = default) =>
        AuthWithPasswordAsync<RecordModel>(usernameOrEmail, password, options, cancellationToken);

    public async Task<RecordAuthResponse<T>> AuthWithPasswordAsync<T>(string usernameOrEmail, string password, RecordOptions? options = null, CancellationToken cancellationToken = default)
    {
        var autoRefreshThreshold = default(double?);
        var resetAutoRefresh = false;

        if (CollectionIdOrName == PocketBase.SDK.AuthStore.SuperusersCollectionName)
        {
            autoRefreshThreshold = options?.AutoRefreshThreshold;

            if (options?.Query.TryGetValue("autoRefresh", out var autoRefreshValue) != true || !IsTruthy(autoRefreshValue))
                resetAutoRefresh = true;

            if (resetAutoRefresh)
                _client.ResetAutoRefresh();
        }

        var auth = await AuthSendAsync<T>("/auth-with-password", options, new Dictionary<string, object?>
        {
            ["identity"] = usernameOrEmail,
            ["password"] = password,
        }, cancellationToken).ConfigureAwait(false);

        if (autoRefreshThreshold is > 0 && CollectionIdOrName == PocketBase.SDK.AuthStore.SuperusersCollectionName)
        {
            _client.RegisterAutoRefresh(
                autoRefreshThreshold.Value,
                refresh: ct => AuthRefreshAsync(new RecordOptions
                {
                    Query = { ["autoRefresh"] = true },
                }, ct),
                reauthenticate: ct => AuthWithPasswordAsync<T>(usernameOrEmail, password, new RecordOptions
                {
                    AutoRefreshThreshold = autoRefreshThreshold,
                    Query = { ["autoRefresh"] = true },
                }, ct));
        }

        return auth;
    }

    public Task<RecordAuthResponse<RecordModel>> AuthWithOAuth2CodeAsync(string provider, string code, string codeVerifier, string redirectUrl, object? createData = null, RecordOptions? options = null, CancellationToken cancellationToken = default) =>
        AuthWithOAuth2CodeAsync<RecordModel>(provider, code, codeVerifier, redirectUrl, createData, options, cancellationToken);

    public Task<RecordAuthResponse<T>> AuthWithOAuth2CodeAsync<T>(string provider, string code, string codeVerifier, string redirectUrl, object? createData = null, RecordOptions? options = null, CancellationToken cancellationToken = default) =>
        AuthSendAsync<T>("/auth-with-oauth2", options, new Dictionary<string, object?>
        {
            ["provider"] = provider,
            ["code"] = code,
            ["codeVerifier"] = codeVerifier,
            ["redirectURL"] = redirectUrl,
            ["createData"] = createData,
        }, cancellationToken);

    public Task<RecordAuthResponse<RecordModel>> AuthWithOAuth2Async(string provider, Func<string, Task> urlCallback, OAuth2AuthOptions? options = null, CancellationToken cancellationToken = default) =>
        AuthWithOAuth2Async<RecordModel>(provider, urlCallback, options, cancellationToken);

    public async Task<RecordAuthResponse<T>> AuthWithOAuth2Async<T>(string provider, Func<string, Task> urlCallback, OAuth2AuthOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(urlCallback);

        var authMethods = await ListAuthMethodsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        var providerInfo = authMethods.OAuth2.Providers.FirstOrDefault(p => p.Name == provider)
            ?? throw new PocketBaseException(string.Empty, 0, default, false, new InvalidOperationException($"Missing or invalid provider \"{provider}\"."));

        var redirectUrl = _client.BuildUrl("/api/oauth2-redirect");

        // initialize a one-off realtime service
        using var realtime = new RealtimeService(_client.Transport);
        var completion = new TaskCompletionSource<RecordAuthResponse<T>>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Cleanup() => _ = realtime.UnsubscribeAsync();

        // disconnected due to network/server error
        realtime.OnDisconnect = activeSubscriptions =>
        {
            if (activeSubscriptions.Count > 0 &&
                completion.TrySetException(new PocketBaseException(string.Empty, 0, default, false, new IOException("realtime connection interrupted"))))
            {
                Cleanup();
            }
        };

        try
        {
            await realtime.SubscribeAsync("@oauth2", async e =>
            {
                var oldState = realtime.ClientId;

                try
                {
                    var data = e.JsonData();
                    string Str(string name) => data[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

                    var state = Str("state");
                    var code = Str("code");
                    var error = Str("error");

                    if (state.Length == 0 || state != oldState) throw new InvalidOperationException("State parameters don't match.");

                    if (error.Length > 0 || code.Length == 0) throw new InvalidOperationException("OAuth2 redirect error or missing code: " + error);

                    var auth = await AuthWithOAuth2CodeAsync<T>(providerInfo.Name, code, providerInfo.CodeVerifier, redirectUrl, options?.CreateData, options, cancellationToken).ConfigureAwait(false);

                    completion.TrySetResult(auth);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex as PocketBaseException ?? new PocketBaseException(string.Empty, 0, default, false, ex));
                }

                Cleanup();
            }, cancellationToken: cancellationToken).ConfigureAwait(false);

            var replacements = new Dictionary<string, string?> { ["state"] = realtime.ClientId };
            if (options?.Scopes.Count > 0) replacements["scope"] = string.Join(" ", options.Scopes);

            await urlCallback(ReplaceQueryParams(providerInfo.AuthUrl + redirectUrl, replacements)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Cleanup();
            throw;
        }
        catch (Exception ex)
        {
            Cleanup();
            throw ex as PocketBaseException ?? new PocketBaseException(string.Empty, 0, default, false, ex);
        }

        try
        {
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Cleanup();
        }
    }

    /// <summary>Port of the JS SDK <c>_replaceQueryParams()</c>: replaces (or removes if null) the specified url query parameters.</summary>
    internal static string ReplaceQueryParams(string url, IReadOnlyDictionary<string, string?> replacements)
    {
        var urlPath = url;
        var query = "";

        var queryIndex = url.IndexOf('?');
        if (queryIndex >= 0)
        {
            urlPath = url[..queryIndex];
            query = url[(queryIndex + 1)..];
        }

        var parsed = new List<KeyValuePair<string, string>>(); // keeps the original order
        foreach (var param in query.Split('&'))
        {
            if (param.Length == 0) continue;

            var pair = param.Split('=');
            var key = Uri.UnescapeDataString(pair[0].Replace('+', ' '));
            var value = Uri.UnescapeDataString((pair.Length > 1 ? pair[1] : "").Replace('+', ' '));

            var existing = parsed.FindIndex(kv => kv.Key == key);
            if (existing >= 0) parsed[existing] = new(key, value);
            else parsed.Add(new(key, value));
        }

        foreach (var (key, value) in replacements)
        {
            var existing = parsed.FindIndex(kv => kv.Key == key);
            if (value is null)
            {
                if (existing >= 0) parsed.RemoveAt(existing);
            }
            else if (existing >= 0)
            {
                parsed[existing] = new(key, value);
            }
            else
            {
                parsed.Add(new(key, value));
            }
        }

        query = string.Join("&", parsed.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));

        return query.Length > 0 ? urlPath + "?" + query : urlPath;
    }

    public Task<RecordAuthResponse<RecordModel>> AuthRefreshAsync(RecordOptions? options = null, CancellationToken cancellationToken = default) =>
        AuthRefreshAsync<RecordModel>(options, cancellationToken);

    public Task<RecordAuthResponse<T>> AuthRefreshAsync<T>(RecordOptions? options = null, CancellationToken cancellationToken = default) =>
        AuthSendAsync<T>("/auth-refresh", options, new Dictionary<string, object?>(), cancellationToken);

    public async Task<OtpResponse> RequestOtpAsync(string email, CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        var send = RequestBuilder.Build(HttpMethod.Post, options, new Dictionary<string, object?> { ["email"] = email });
        var data = await _client.SendAsync(BaseCollectionPath + "/request-otp", send, cancellationToken).ConfigureAwait(false);
        return Decode<OtpResponse>(data) ?? new OtpResponse();
    }

    public Task<RecordAuthResponse<RecordModel>> AuthWithOtpAsync(string otpId, string password, RecordOptions? options = null, CancellationToken cancellationToken = default) =>
        AuthWithOtpAsync<RecordModel>(otpId, password, options, cancellationToken);

    public Task<RecordAuthResponse<T>> AuthWithOtpAsync<T>(string otpId, string password, RecordOptions? options = null, CancellationToken cancellationToken = default) =>
        AuthSendAsync<T>("/auth-with-otp", options, new Dictionary<string, object?>
        {
            ["otpId"] = otpId,
            ["password"] = password,
        }, cancellationToken);

    public Task RequestPasswordResetAsync(string email, CommonOptions? options = null, CancellationToken cancellationToken = default) =>
        PostAsync("/request-password-reset", options, new Dictionary<string, object?> { ["email"] = email }, cancellationToken);

    public Task ConfirmPasswordResetAsync(string passwordResetToken, string password, string passwordConfirm, CommonOptions? options = null, CancellationToken cancellationToken = default) =>
        PostAsync("/confirm-password-reset", options, new Dictionary<string, object?>
        {
            ["token"] = passwordResetToken,
            ["password"] = password,
            ["passwordConfirm"] = passwordConfirm,
        }, cancellationToken);

    public Task RequestVerificationAsync(string email, CommonOptions? options = null, CancellationToken cancellationToken = default) =>
        PostAsync("/request-verification", options, new Dictionary<string, object?> { ["email"] = email }, cancellationToken);

    public async Task ConfirmVerificationAsync(string verificationToken, CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        await PostAsync("/confirm-verification", options, new Dictionary<string, object?> { ["token"] = verificationToken }, cancellationToken).ConfigureAwait(false);

        // on success manually update the current auth record verified state
        var payload = PocketBaseJwt.GetTokenPayload(verificationToken);
        var model = AuthStore.Record;
        if (model is not null && !model.Get<bool>("verified") && MatchesTokenRecord(model, payload))
        {
            var updated = model.Clone();
            updated.Set("verified", true);
            AuthStore.Save(AuthStore.Token, updated);
        }
    }

    public Task RequestEmailChangeAsync(string newEmail, CommonOptions? options = null, CancellationToken cancellationToken = default) =>
        PostAsync("/request-email-change", options, new Dictionary<string, object?> { ["newEmail"] = newEmail }, cancellationToken);

    public async Task ConfirmEmailChangeAsync(string emailChangeToken, string password, CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        await PostAsync("/confirm-email-change", options, new Dictionary<string, object?>
        {
            ["token"] = emailChangeToken,
            ["password"] = password,
        }, cancellationToken).ConfigureAwait(false);

        var payload = PocketBaseJwt.GetTokenPayload(emailChangeToken);
        var model = AuthStore.Record;
        if (model is not null && MatchesTokenRecord(model, payload))
        {
            AuthStore.Clear();
        }
    }

    public async Task<IPocketBaseClient> ImpersonateAsync(string recordId, double duration, RecordOptions? options = null, CancellationToken cancellationToken = default)
    {
        var send = RequestBuilder.Build(HttpMethod.Post, options, new Dictionary<string, object?>());

        // same as the Dart SDK: an explicit "duration" body param has precedence
        var body = (Dictionary<string, object?>)send.Body!;
        if (!body.ContainsKey("duration")) body["duration"] = duration;

        if (!send.Headers.ContainsKey("Authorization")) send.Headers["Authorization"] = AuthStore.Token;

        // create a new client loaded with the impersonated auth state
        var newClient = _client.WithAuthStore(new AuthStore());

        var data = await newClient.SendAsync(BaseCollectionPath + "/impersonate/" + Uri.EscapeDataString(recordId), send, cancellationToken).ConfigureAwait(false);

        newClient.AuthStore.Save(PbJsonHelpers.GetString(data, "token"), RecordModel.FromJson(PbJsonHelpers.GetProperty(data, "record")));

        return newClient;
    }

#pragma warning disable CS0618 // deprecated in the official JS SDK too
    [Obsolete("Use Collection(\"_externalAuths\") instead (deprecated in the official JS SDK).")]
    public Task<IReadOnlyList<RecordModel>> ListExternalAuthsAsync(string recordId, CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        var listOptions = new RecordFullListOptions
        {
            Fields = options?.Fields,
            Filter = _client.Filter("recordRef = {:id}", new Dictionary<string, object?> { ["id"] = recordId }),
        };
        CopyQueryAndHeaders(options, listOptions);

        return _client.Collection("_externalAuths").GetFullListAsync(listOptions, cancellationToken);
    }

    [Obsolete("Use Collection(\"_externalAuths\") instead (deprecated in the official JS SDK).")]
    public async Task UnlinkExternalAuthAsync(string recordId, string provider, CommonOptions? options = null, CancellationToken cancellationToken = default)
    {
        var externalAuths = _client.Collection("_externalAuths");

        var ea = await externalAuths.GetFirstListItemAsync(
            _client.Filter("recordRef = {:recordId} && provider = {:provider}", new Dictionary<string, object?>
            {
                ["recordId"] = recordId,
                ["provider"] = provider,
            }),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await externalAuths.DeleteAsync(ea.Id, options, cancellationToken).ConfigureAwait(false);
    }
#pragma warning restore CS0618

    // -----------------------------------------------------------------
    // helpers
    // -----------------------------------------------------------------

    private string RecordPath(string id) => BaseCrudPath + "/" + Uri.EscapeDataString(id);

    private bool IsCurrentCollection(RecordModel authRecord) =>
        authRecord.CollectionId == CollectionIdOrName || authRecord.CollectionName == CollectionIdOrName;

    private static bool MatchesTokenRecord(RecordModel model, IReadOnlyDictionary<string, JsonElement> payload) =>
        payload.TryGetValue("id", out var id) && id.ValueKind == JsonValueKind.String && model.Id == id.GetString() &&
        payload.TryGetValue("collectionId", out var cid) && cid.ValueKind == JsonValueKind.String && model.CollectionId == cid.GetString();

    private async Task PostAsync(string action, CommonOptions? options, Dictionary<string, object?> body, CancellationToken cancellationToken)
    {
        var send = RequestBuilder.Build(HttpMethod.Post, options, body);
        await _client.SendAsync(BaseCollectionPath + action, send, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends an auth request and, on success, updates the auth store (port of the SDKs <c>authResponse()</c>).</summary>
    private async Task<RecordAuthResponse<T>> AuthSendAsync<T>(string action, RecordOptions? options, Dictionary<string, object?> body, CancellationToken cancellationToken)
    {
        // auth-refresh has no body params (an empty explicit body only forwards the options body, if any)
        var hasBody = body.Count > 0 || options?.Body.Count > 0;
        var send = RequestBuilder.Build(HttpMethod.Post, options, hasBody ? body : null);

        var data = await _client.SendAsync(BaseCollectionPath + action, send, cancellationToken).ConfigureAwait(false);

        var token = PbJsonHelpers.GetString(data, "token");
        var recordJson = PbJsonHelpers.GetProperty(data, "record");

        AuthStore.Save(token, RecordModel.FromJson(recordJson));

        return new RecordAuthResponse<T>
        {
            Token = token,
            Record = Decode<T>(recordJson.ValueKind == JsonValueKind.Object ? recordJson : JsonSerializer.SerializeToElement(new { }))!,
            Meta = PbJsonHelpers.GetProperty(data, "meta") is { ValueKind: JsonValueKind.Object } meta
                ? (JsonObject)JsonNode.Parse(meta.GetRawText())!
                : null,
        };
    }

    private T? Decode<T>(JsonElement data)
    {
        if (data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return default;
        if (typeof(T) == typeof(JsonElement)) return (T)(object)data;
        return data.Deserialize<T>(_client.Options.JsonSerializerOptions);
    }

    private static JsonElement NotFound(string message) =>
        JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["code"] = 404,
            ["message"] = message,
            ["data"] = new Dictionary<string, object>(),
        });

    private static void CopyQueryAndHeaders(CommonOptions? from, CommonOptions to)
    {
        if (from is null) return;
        foreach (var (k, v) in from.Query) to.Query[k] = v;
        foreach (var (k, v) in from.Headers) to.Headers[k] = v;
    }

    private static bool IsTruthy(object? value) => value switch
    {
        true => true,
        string s when bool.TryParse(s, out var b) => b,
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

internal static class PbJsonHelpers
{
    public static JsonElement GetProperty(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) ? p : default;

    public static string GetString(JsonElement e, string name) =>
        GetProperty(e, name) is { ValueKind: JsonValueKind.String } p ? p.GetString() ?? "" : "";
}

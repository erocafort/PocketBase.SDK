namespace PocketBaseNet.Abstractions;

/// <summary>
/// Record CRUD and auth APIs of a single collection (port of the SDKs <c>RecordService</c>).
/// Obtain it with <see cref="IPocketBaseClient.Collection(string)"/>, e.g. <c>pb.Collection("posts")</c>.
/// Every method has a generic overload that deserializes the record(s) to a custom type
/// and a non-generic one that returns <see cref="RecordModel"/>.
/// </summary>
/// <exception cref="PocketBaseException">Thrown by every method on API/network errors.</exception>
public interface IRecordService
{
    /// <summary>The collection id or name.</summary>
    string CollectionIdOrName { get; }

    /// <summary><c>/api/collections/{collectionIdOrName}</c></summary>
    string BaseCollectionPath { get; }

    /// <summary><c>/api/collections/{collectionIdOrName}/records</c></summary>
    string BaseCrudPath { get; }

    // -----------------------------------------------------------------
    // Realtime
    // -----------------------------------------------------------------

    /// <summary>
    /// Subscribes to realtime changes of the specified topic: <c>"*"</c> (any record of the collection) or a record id.
    /// It's OK to subscribe multiple times to the same topic; use the returned <see cref="UnsubscribeFunc"/>
    /// to remove only this subscription, or <see cref="UnsubscribeAsync"/> to remove all of the topic.
    /// </summary>
    Task<UnsubscribeFunc> SubscribeAsync(string topic, Action<RecordSubscriptionEvent<RecordModel>> callback, RealtimeSubscribeOptions? options = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="SubscribeAsync(string, Action{RecordSubscriptionEvent{RecordModel}}, RealtimeSubscribeOptions?, CancellationToken)"/>
    Task<UnsubscribeFunc> SubscribeAsync(string topic, Func<RecordSubscriptionEvent<RecordModel>, Task> callback, RealtimeSubscribeOptions? options = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="SubscribeAsync(string, Action{RecordSubscriptionEvent{RecordModel}}, RealtimeSubscribeOptions?, CancellationToken)"/>
    Task<UnsubscribeFunc> SubscribeAsync<T>(string topic, Action<RecordSubscriptionEvent<T>> callback, RealtimeSubscribeOptions? options = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="SubscribeAsync(string, Action{RecordSubscriptionEvent{RecordModel}}, RealtimeSubscribeOptions?, CancellationToken)"/>
    Task<UnsubscribeFunc> SubscribeAsync<T>(string topic, Func<RecordSubscriptionEvent<T>, Task> callback, RealtimeSubscribeOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsubscribes from all subscriptions of the specified topic (<c>"*"</c> or record id),
    /// or from all subscriptions of the collection if <paramref name="topic"/> is null/empty.
    /// </summary>
    Task UnsubscribeAsync(string? topic = null);

    // -----------------------------------------------------------------
    // CRUD
    // -----------------------------------------------------------------

    /// <summary>Returns all records batch fetched at once (1000 per request by default, see <see cref="RecordFullListOptions.Batch"/>).</summary>
    Task<IReadOnlyList<RecordModel>> GetFullListAsync(RecordFullListOptions? options = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="GetFullListAsync(RecordFullListOptions?, CancellationToken)"/>
    Task<IReadOnlyList<T>> GetFullListAsync<T>(RecordFullListOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Returns a paginated records list.</summary>
    Task<ResultList<RecordModel>> GetListAsync(int page = 1, int perPage = 30, RecordListOptions? options = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="GetListAsync(int, int, RecordListOptions?, CancellationToken)"/>
    Task<ResultList<T>> GetListAsync<T>(int page = 1, int perPage = 30, RecordListOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Returns the first record matching the filter. Throws a 404 <see cref="PocketBaseException"/> if none is found.</summary>
    Task<RecordModel> GetFirstListItemAsync(string filter, RecordListOptions? options = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="GetFirstListItemAsync(string, RecordListOptions?, CancellationToken)"/>
    Task<T> GetFirstListItemAsync<T>(string filter, RecordListOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Returns a single record by its id. Throws a 404 <see cref="PocketBaseException"/> if <paramref name="id"/> is empty.</summary>
    Task<RecordModel> GetOneAsync(string id, RecordOptions? options = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="GetOneAsync(string, RecordOptions?, CancellationToken)"/>
    Task<T> GetOneAsync<T>(string id, RecordOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new record. <paramref name="body"/> can be any object/dictionary (sent as JSON),
    /// a <see cref="HttpContent"/> (sent as-is), and if it contains <see cref="FileUpload"/> values it is sent as multipart/form-data.
    /// </summary>
    Task<RecordModel> CreateAsync(object? body = null, RecordOptions? options = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="CreateAsync(object?, RecordOptions?, CancellationToken)"/>
    Task<T> CreateAsync<T>(object? body = null, RecordOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing record. If it is the current auth record, the auth store record is updated too.
    /// See <see cref="CreateAsync(object?, RecordOptions?, CancellationToken)"/> for the supported body types.
    /// </summary>
    Task<RecordModel> UpdateAsync(string id, object? body = null, RecordOptions? options = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="UpdateAsync(string, object?, RecordOptions?, CancellationToken)"/>
    Task<T> UpdateAsync<T>(string id, object? body = null, RecordOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes a record. If it is the current auth record, the auth store is cleared.</summary>
    Task DeleteAsync(string id, CommonOptions? options = null, CancellationToken cancellationToken = default);

    // -----------------------------------------------------------------
    // Auth (auth collections only)
    // -----------------------------------------------------------------

    /// <summary>Returns all available collection auth methods.</summary>
    Task<AuthMethodsList> ListAuthMethodsAsync(CommonOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Authenticates with username/email and password. On success the auth store is updated.</summary>
    Task<RecordAuthResponse<RecordModel>> AuthWithPasswordAsync(string usernameOrEmail, string password, RecordOptions? options = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="AuthWithPasswordAsync(string, string, RecordOptions?, CancellationToken)"/>
    Task<RecordAuthResponse<T>> AuthWithPasswordAsync<T>(string usernameOrEmail, string password, RecordOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authenticates with an OAuth2 code (manual code exchange flow; the <paramref name="codeVerifier"/> comes from
    /// <see cref="ListAuthMethodsAsync"/> providers). On success the auth store is updated.
    /// </summary>
    Task<RecordAuthResponse<RecordModel>> AuthWithOAuth2CodeAsync(string provider, string code, string codeVerifier, string redirectUrl, object? createData = null, RecordOptions? options = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="AuthWithOAuth2CodeAsync(string, string, string, string, object?, RecordOptions?, CancellationToken)"/>
    Task<RecordAuthResponse<T>> AuthWithOAuth2CodeAsync<T>(string provider, string code, string codeVerifier, string redirectUrl, object? createData = null, RecordOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// All-in-one OAuth2 flow without custom redirects or deeplinks (port of the realtime form of the SDKs <c>authWithOAuth2()</c>):
    /// opens a one-off realtime connection, calls <paramref name="urlCallback"/> with the provider auth url
    /// (e.g. to open it in the browser) and waits until the OAuth2 redirect sends the code back through the
    /// realtime connection. On success the auth store is updated.
    /// <para>Configure <c>https://yourdomain.com/api/oauth2-redirect</c> as redirect URL in the provider dashboard.</para>
    /// The method waits until the flow completes, the realtime connection is interrupted or <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    Task<RecordAuthResponse<RecordModel>> AuthWithOAuth2Async(string provider, Func<string, Task> urlCallback, OAuth2AuthOptions? options = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="AuthWithOAuth2Async(string, Func{string, Task}, OAuth2AuthOptions?, CancellationToken)"/>
    Task<RecordAuthResponse<T>> AuthWithOAuth2Async<T>(string provider, Func<string, Task> urlCallback, OAuth2AuthOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Refreshes the current auth record and token. On success the auth store is updated.</summary>
    Task<RecordAuthResponse<RecordModel>> AuthRefreshAsync(RecordOptions? options = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="AuthRefreshAsync(RecordOptions?, CancellationToken)"/>
    Task<RecordAuthResponse<T>> AuthRefreshAsync<T>(RecordOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Sends an OTP to the provided email.</summary>
    Task<OtpResponse> RequestOtpAsync(string email, CommonOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Authenticates with an OTP. On success the auth store is updated.</summary>
    Task<RecordAuthResponse<RecordModel>> AuthWithOtpAsync(string otpId, string password, RecordOptions? options = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="AuthWithOtpAsync(string, string, RecordOptions?, CancellationToken)"/>
    Task<RecordAuthResponse<T>> AuthWithOtpAsync<T>(string otpId, string password, RecordOptions? options = null, CancellationToken cancellationToken = default);

    Task RequestPasswordResetAsync(string email, CommonOptions? options = null, CancellationToken cancellationToken = default);

    Task ConfirmPasswordResetAsync(string passwordResetToken, string password, string passwordConfirm, CommonOptions? options = null, CancellationToken cancellationToken = default);

    Task RequestVerificationAsync(string email, CommonOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Confirms the email verification. If it matches the current auth record, its <c>verified</c> field is set to true.</summary>
    Task ConfirmVerificationAsync(string verificationToken, CommonOptions? options = null, CancellationToken cancellationToken = default);

    Task RequestEmailChangeAsync(string newEmail, CommonOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Confirms the new email. If it matches the current auth record, the auth store is cleared.</summary>
    Task ConfirmEmailChangeAsync(string emailChangeToken, string password, CommonOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authenticates as the specified record (requires superuser privileges) and returns a NEW client
    /// with the impersonated auth state in an in-memory store. The current client is not modified.
    /// If <paramref name="duration"/> is 0, the collection default auth token duration is used.
    /// </summary>
    Task<IPocketBaseClient> ImpersonateAsync(string recordId, double duration, RecordOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Lists the linked external auth providers of a record.</summary>
    [Obsolete("Use Collection(\"_externalAuths\") instead (deprecated in the official JS SDK).")]
    Task<IReadOnlyList<RecordModel>> ListExternalAuthsAsync(string recordId, CommonOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Unlinks a single external auth provider from a record.</summary>
    [Obsolete("Use Collection(\"_externalAuths\") instead (deprecated in the official JS SDK).")]
    Task UnlinkExternalAuthAsync(string recordId, string provider, CommonOptions? options = null, CancellationToken cancellationToken = default);
}

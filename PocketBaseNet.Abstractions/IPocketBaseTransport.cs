using System.Text.Json;

namespace PocketBaseNet.Abstractions;

/// <summary>Low level request pipeline shared by all services (equivalent to the SDKs <c>pb.send()</c> / <c>pb.buildURL()</c>).</summary>
public interface IPocketBaseTransport
{
    /// <summary>The auth store used to authorize the requests.</summary>
    IAuthStore AuthStore { get; }

    /// <summary>Builds a full client url by safely concatenating the provided path to the base url.</summary>
    string BuildUrl(string path);

    /// <summary>
    /// Sends an API request. Returns the parsed JSON response
    /// (<see cref="JsonValueKind.Undefined"/> when the response has no body, e.g. 204).
    /// </summary>
    /// <exception cref="PocketBaseException">On any API/network error.</exception>
    Task<JsonElement> SendAsync(string path, SendOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Sends an API request and deserializes the JSON response to <typeparamref name="T"/>.</summary>
    /// <exception cref="PocketBaseException">On any API/network error.</exception>
    Task<T?> SendAsync<T>(string path, SendOptions? options = null, CancellationToken cancellationToken = default);
}

namespace PocketBaseNet.Abstractions;

/// <summary>Callback invoked on every auth store change (save/clear).</summary>
public delegate void AuthStoreChangeHandler(string token, RecordModel? record);

/// <summary>
/// Keeps track of the authenticated token and auth record.
/// Equivalent to <c>BaseAuthStore</c> (JS SDK) / <c>AuthStore</c> (Dart SDK).
/// </summary>
public interface IAuthStore
{
    /// <summary>The stored auth token (empty string if none).</summary>
    string Token { get; }

    /// <summary>The stored auth record (if any).</summary>
    RecordModel? Record { get; }

    /// <summary>@deprecated Use <see cref="Record"/>.</summary>
    [Obsolete("Use Record instead.")]
    RecordModel? Model { get; }

    /// <summary>Loosely checks if the store has a valid token (existing and with unexpired <c>exp</c> claim).</summary>
    bool IsValid { get; }

    /// <summary>Loosely checks whether the current store state is for a superuser.</summary>
    bool IsSuperuser { get; }

    /// <summary>@deprecated Use <see cref="IsSuperuser"/>.</summary>
    [Obsolete("Use IsSuperuser instead.")]
    bool IsAdmin { get; }

    /// <summary>@deprecated Use <c>!IsSuperuser</c>.</summary>
    [Obsolete("Use !IsSuperuser instead.")]
    bool IsAuthRecord { get; }

    /// <summary>Saves the provided token and record and notifies the listeners.</summary>
    void Save(string? token, RecordModel? record = null);

    /// <summary>Removes the stored token and record ("logout") and notifies the listeners.</summary>
    void Clear();

    /// <summary>Loads and applies auth state from a cookie string.</summary>
    void LoadFromCookie(string cookie, string key = "pb_auth");

    /// <summary>Exports the current auth state as cookie string.</summary>
    string ExportToCookie(CookieSerializeOptions? options = null, string key = "pb_auth");

    /// <summary>
    /// Registers a change listener. Dispose the returned object to remove it
    /// (equivalent to calling the removal function returned by the JS SDK).
    /// </summary>
    IDisposable OnChange(AuthStoreChangeHandler callback, bool fireImmediately = false);
}

public sealed class CookieSerializeOptions
{
    public double? MaxAge { get; set; }
    public string? Domain { get; set; }
    public string? Path { get; set; }
    public DateTimeOffset? Expires { get; set; }
    public bool? HttpOnly { get; set; }
    public bool? Secure { get; set; }
    public string? Priority { get; set; }
    public string? SameSite { get; set; }
}

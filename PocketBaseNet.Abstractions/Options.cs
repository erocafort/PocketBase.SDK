namespace PocketBaseNet.Abstractions;

/// <summary>
/// Common request options (JS <c>CommonOptions</c>; Dart named <c>fields/query/headers/body</c> arguments).
/// Typed properties (<see cref="Fields"/>, <c>Expand</c>, <c>Filter</c>...) take precedence over the same keys in <see cref="Query"/>.
/// </summary>
public class CommonOptions
{
    /// <summary>Comma separated fields to return in the JSON response (e.g. <c>"*,expand.author.name"</c>).</summary>
    public string? Fields { get; set; }

    /// <summary>Additional query parameters.</summary>
    public IDictionary<string, object?> Query { get; init; } = new Dictionary<string, object?>();

    /// <summary>Additional request headers.</summary>
    public IDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Additional body parameters merged into the body of the auth/action methods
    /// (e.g. <c>AuthWithPasswordAsync</c>). The explicit method arguments always win.
    /// Ignored by the CRUD methods, whose body is passed explicitly.
    /// </summary>
    public IDictionary<string, object?> Body { get; init; } = new Dictionary<string, object?>();
}

public class RecordOptions : CommonOptions
{
    /// <summary>Comma separated relations to auto expand (e.g. <c>"author,comments.user"</c>).</summary>
    public string? Expand { get; set; }

    /// <summary>
    /// Optional auto-refresh threshold in seconds for superuser <c>AuthWithPasswordAsync</c>.
    /// When set, the client will try to refresh/reauthenticate automatically before requests
    /// if the token is close to expiry (port of JS <c>autoRefreshThreshold</c> behavior).
    /// </summary>
    public double? AutoRefreshThreshold { get; set; }
}

/// <summary>Options of the all-in-one OAuth2 flow (JS <c>OAuth2AuthConfig</c>; Dart <c>authWithOAuth2</c> named arguments).</summary>
public class OAuth2AuthOptions : RecordOptions
{
    /// <summary>Custom scopes to overwrite the default ones.</summary>
    public IList<string> Scopes { get; init; } = new List<string>();

    /// <summary>Optional data for the new record if OAuth2 creates it (sent as <c>createData</c>).</summary>
    public object? CreateData { get; set; }
}

public class ListOptions : CommonOptions
{
    public string? Sort { get; set; }
    public string? Filter { get; set; }

    /// <summary>Skips the total counts query (<c>totalItems</c>/<c>totalPages</c> are returned as -1). Faster for large collections.</summary>
    public bool? SkipTotal { get; set; }
}

public class FullListOptions : ListOptions
{
    /// <summary>Items per request (default 1000).</summary>
    public int? Batch { get; set; }
}

public class RecordListOptions : ListOptions
{
    /// <inheritdoc cref="RecordOptions.Expand"/>
    public string? Expand { get; set; }
}

public class RecordFullListOptions : RecordListOptions
{
    /// <summary>Items per request (default 1000).</summary>
    public int? Batch { get; set; }
}

public class LogStatsOptions : CommonOptions
{
    public string? Filter { get; set; }
}

/// <summary>URL query options for files API URLs.</summary>
public class FileOptions
{
    public string? Thumb { get; set; }
    public bool? Download { get; set; }
    public string? Token { get; set; }
    public IDictionary<string, object?> Query { get; init; } = new Dictionary<string, object?>();
}

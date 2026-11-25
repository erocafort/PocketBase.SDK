using System.Globalization;
using System.Text;
using System.Text.Json;
using PocketBase.SDK.Abstractions;

namespace PocketBase.SDK;

/// <summary>
/// In-memory auth store (port of the JS SDK <c>BaseAuthStore</c> / Dart SDK <c>AuthStore</c>).
/// Thread-safe. Can be extended (e.g. to persist the state) by overriding <see cref="Save"/> and <see cref="Clear"/>.
/// </summary>
public class AuthStore : IAuthStore
{
    /// <summary>Default id of the "_superusers" system collection (crc32 checksum based).</summary>
    public const string SuperusersCollectionId = "pbc_3142635823";
    public const string SuperusersCollectionName = "_superusers";

    private readonly object _sync = new();
    private readonly List<AuthStoreChangeHandler> _callbacks = new();
    private string _token = string.Empty;
    private RecordModel? _record;

    public string Token { get { lock (_sync) return _token; } }

    public RecordModel? Record { get { lock (_sync) return _record; } }

#pragma warning disable CS0618
    [Obsolete("Use Record instead.")]
    public RecordModel? Model => Record;
#pragma warning restore CS0618

    public bool IsValid => !PocketBaseJwt.IsTokenExpired(Token);

    public bool IsSuperuser
    {
        get
        {
            string token; RecordModel? record;
            lock (_sync) { token = _token; record = _record; }

            var payload = PocketBaseJwt.GetTokenPayload(token);
            if (!IsString(payload, "type", "auth")) return false;

            return record?.CollectionName == SuperusersCollectionName
                // fallback in case the record field is not populated
                || (string.IsNullOrEmpty(record?.CollectionName) && IsString(payload, "collectionId", SuperusersCollectionId));
        }
    }

#pragma warning disable CS0618
    [Obsolete("Use IsSuperuser instead.")]
    public bool IsAdmin => IsSuperuser;

    [Obsolete("Use !IsSuperuser instead.")]
    public bool IsAuthRecord => IsString(PocketBaseJwt.GetTokenPayload(Token), "type", "auth") && !IsSuperuser;
#pragma warning restore CS0618

    public virtual void Save(string? token, RecordModel? record = null)
    {
        lock (_sync)
        {
            _token = token ?? string.Empty;
            _record = record;
        }
        TriggerChange();
    }

    public virtual void Clear()
    {
        lock (_sync)
        {
            _token = string.Empty;
            _record = null;
        }
        TriggerChange();
    }

    public virtual void LoadFromCookie(string cookie, string key = "pb_auth")
    {
        var values = CookieTools.Parse(cookie ?? string.Empty);
        values.TryGetValue(key, out var raw);

        Dictionary<string, JsonElement>? data = null;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(raw);
            }
            catch (JsonException)
            {
                // ignored by design (same as the official SDKs)
            }
        }

        var token = "";
        RecordModel? record = null;

        if (data is not null)
        {
            if (data.TryGetValue("token", out var tokenJson) && tokenJson.ValueKind == JsonValueKind.String)
                token = tokenJson.GetString() ?? "";

            var hasRecord = data.TryGetValue("record", out var recordJson) || data.TryGetValue("model", out recordJson);
            if (hasRecord && recordJson.ValueKind == JsonValueKind.Object)
                record = RecordModel.FromJson(recordJson);
        }

        Save(token, record);
    }

    public virtual string ExportToCookie(CookieSerializeOptions? options = null, string key = "pb_auth")
    {
        var token = Token;
        var payload = PocketBaseJwt.GetTokenPayload(token);

        var mergedOptions = new CookieSerializeOptions
        {
            Secure = true,
            SameSite = "strict",
            HttpOnly = true,
            Path = "/",
            Expires = payload.TryGetValue("exp", out var exp) && TryReadNumber(exp, out var expNum)
                ? DateTimeOffset.FromUnixTimeSeconds((long)expNum)
                : new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero),
            MaxAge = options?.MaxAge,
            Domain = options?.Domain,
            Priority = options?.Priority,
        };

        if (options is not null)
        {
            if (options.Path is not null) mergedOptions.Path = options.Path;
            if (options.Expires.HasValue) mergedOptions.Expires = options.Expires;
            if (options.HttpOnly.HasValue) mergedOptions.HttpOnly = options.HttpOnly;
            if (options.Secure.HasValue) mergedOptions.Secure = options.Secure;
            if (options.SameSite is not null) mergedOptions.SameSite = options.SameSite;
        }

        var rawData = new
        {
            token,
            record = Record is null ? null : JsonSerializer.Deserialize<object>(JsonSerializer.Serialize(Record.Data)),
        };

        var serialized = JsonSerializer.Serialize(rawData);
        var result = CookieTools.Serialize(key, serialized, mergedOptions);

        if (Record is not null && Encoding.UTF8.GetByteCount(result) > 4096)
        {
            var minRecord = new Dictionary<string, object?>
            {
                ["id"] = Record.Id,
                ["email"] = Record.Get<string>("email"),
            };

            if (!string.IsNullOrEmpty(Record.CollectionId)) minRecord["collectionId"] = Record.CollectionId;
            if (!string.IsNullOrEmpty(Record.CollectionName)) minRecord["collectionName"] = Record.CollectionName;

            if (Record.Data.TryGetPropertyValue("verified", out var verifiedNode) && verifiedNode is not null)
                minRecord["verified"] = JsonSerializer.Deserialize<object>(verifiedNode.ToJsonString());

            serialized = JsonSerializer.Serialize(new { token, record = minRecord });
            result = CookieTools.Serialize(key, serialized, mergedOptions);
        }

        return result;
    }

    public IDisposable OnChange(AuthStoreChangeHandler callback, bool fireImmediately = false)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_sync) _callbacks.Add(callback);

        if (fireImmediately) callback(Token, Record);

        return new Subscription(this, callback);
    }

    /// <summary>Notifies all registered listeners with the current state.</summary>
    protected void TriggerChange()
    {
        AuthStoreChangeHandler[] callbacks;
        string token; RecordModel? record;
        lock (_sync)
        {
            callbacks = _callbacks.ToArray();
            token = _token;
            record = _record;
        }

        foreach (var cb in callbacks) cb(token, record);
    }

    private static bool IsString(IReadOnlyDictionary<string, JsonElement> payload, string key, string expected) =>
        payload.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() == expected;

    private static bool TryReadNumber(JsonElement e, out double value)
    {
        value = e.ValueKind switch
        {
            JsonValueKind.Number => e.GetDouble(),
            JsonValueKind.String when double.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => double.NaN,
        };

        return !double.IsNaN(value);
    }

    private sealed class Subscription(AuthStore store, AuthStoreChangeHandler callback) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            lock (store._sync)
            {
                var i = store._callbacks.LastIndexOf(callback);
                if (i >= 0) store._callbacks.RemoveAt(i);
            }
        }
    }
}

internal static class CookieTools
{
    public static Dictionary<string, string> Parse(string cookie)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(cookie)) return result;

        var index = 0;
        while (index < cookie.Length)
        {
            var eqIdx = cookie.IndexOf('=', index);
            if (eqIdx == -1) break;

            var endIdx = cookie.IndexOf(';', index);
            if (endIdx == -1) endIdx = cookie.Length;
            else if (endIdx < eqIdx)
            {
                index = cookie.LastIndexOf(';', eqIdx - 1) + 1;
                continue;
            }

            var key = cookie[index..eqIdx].Trim();
            if (!result.ContainsKey(key))
            {
                var val = cookie[(eqIdx + 1)..endIdx].Trim();
                if (val.Length > 1 && val[0] == '"' && val[^1] == '"') val = val[1..^1];

                try { result[key] = Uri.UnescapeDataString(val); }
                catch (UriFormatException) { result[key] = val; }
            }

            index = endIdx + 1;
        }

        return result;
    }

    public static string Serialize(string name, string value, CookieSerializeOptions? options)
    {
        static string Enc(string s) => Uri.EscapeDataString(s);

        var sb = new StringBuilder();
        sb.Append(name).Append('=').Append(Enc(value));

        if (options?.MaxAge is double maxAge)
            sb.Append("; Max-Age=").Append(Math.Floor(maxAge).ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(options?.Domain))
            sb.Append("; Domain=").Append(options.Domain);

        if (!string.IsNullOrWhiteSpace(options?.Path))
            sb.Append("; Path=").Append(options.Path);

        if (options?.Expires is DateTimeOffset expires)
            sb.Append("; Expires=").Append(expires.UtcDateTime.ToString("R", CultureInfo.InvariantCulture));

        if (options?.HttpOnly == true) sb.Append("; HttpOnly");
        if (options?.Secure == true) sb.Append("; Secure");

        if (!string.IsNullOrWhiteSpace(options?.Priority))
        {
            var p = options.Priority.Trim().ToLowerInvariant();
            sb.Append(p switch
            {
                "low" => "; Priority=Low",
                "medium" => "; Priority=Medium",
                "high" => "; Priority=High",
                _ => throw new ArgumentException("Invalid cookie priority.", nameof(options)),
            });
        }

        if (!string.IsNullOrWhiteSpace(options?.SameSite))
        {
            var s = options.SameSite.Trim().ToLowerInvariant();
            sb.Append(s switch
            {
                "true" or "strict" => "; SameSite=Strict",
                "lax" => "; SameSite=Lax",
                "none" => "; SameSite=None",
                _ => throw new ArgumentException("Invalid SameSite value.", nameof(options)),
            });
        }

        return sb.ToString();
    }
}

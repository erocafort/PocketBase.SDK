using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PocketBase.SDK.Abstractions;

/// <summary>Paginated list response (JS <c>ListResult</c> / Dart <c>ResultList</c>).</summary>
public sealed class ResultList<T>
{
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("perPage")] public int PerPage { get; set; }
    [JsonPropertyName("totalItems")] public int TotalItems { get; set; }
    [JsonPropertyName("totalPages")] public int TotalPages { get; set; }
    [JsonPropertyName("items")] public IReadOnlyList<T> Items { get; set; } = [];
}

/// <summary>Record auth response (JS <c>RecordAuthResponse</c> / Dart <c>RecordAuth</c>).</summary>
public sealed class RecordAuthResponse<T>
{
    /// <summary>The PocketBase record auth token.</summary>
    [JsonPropertyName("token")] public string Token { get; set; } = "";

    /// <summary>The authenticated record.</summary>
    [JsonPropertyName("record")] public T Record { get; set; } = default!;

    /// <summary>Auth meta data, usually filled when OAuth2 is used (e.g. <c>meta.accessToken</c>, <c>meta.refreshToken</c>, <c>meta.isNew</c>).</summary>
    [JsonPropertyName("meta")] public JsonObject? Meta { get; set; }
}

public sealed class AuthMethodsList
{
    [JsonPropertyName("mfa")] public AuthMethodMfa Mfa { get; set; } = new();
    [JsonPropertyName("otp")] public AuthMethodOtp Otp { get; set; } = new();
    [JsonPropertyName("password")] public AuthMethodPassword Password { get; set; } = new();
    [JsonPropertyName("oauth2")] public AuthMethodOAuth2 OAuth2 { get; set; } = new();
}

public sealed class AuthMethodMfa
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("duration")] public double Duration { get; set; }
}

public sealed class AuthMethodOtp
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("duration")] public double Duration { get; set; }
}

public sealed class AuthMethodPassword
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("identityFields")] public List<string> IdentityFields { get; set; } = [];
}

public sealed class AuthMethodOAuth2
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("providers")] public List<AuthMethodProvider> Providers { get; set; } = [];
}

public sealed class AuthMethodProvider
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("state")] public string State { get; set; } = "";
    [JsonPropertyName("authURL")] public string AuthUrl { get; set; } = "";
    [JsonPropertyName("codeVerifier")] public string CodeVerifier { get; set; } = "";
    [JsonPropertyName("codeChallenge")] public string CodeChallenge { get; set; } = "";
    [JsonPropertyName("codeChallengeMethod")] public string CodeChallengeMethod { get; set; } = "";
    [JsonPropertyName("pkce")] public bool? Pkce { get; set; }
}

public sealed class OtpResponse
{
    [JsonPropertyName("otpId")] public string OtpId { get; set; } = "";
}

/// <summary>Result of a single batch sub-request.</summary>
public sealed class BatchResult
{
    [JsonPropertyName("status")] public int Status { get; set; }
    [JsonPropertyName("body")] public JsonElement Body { get; set; }
}

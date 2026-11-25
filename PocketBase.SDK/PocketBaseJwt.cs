using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PocketBase.SDK;

/// <summary>JWT helpers (port of the JS SDK <c>tools/jwt.ts</c>). The token signature is NOT verified.</summary>
public static class PocketBaseJwt
{
    private static readonly IReadOnlyDictionary<string, JsonElement> Empty = new Dictionary<string, JsonElement>();

    /// <summary>Returns the JWT payload claims (empty if the token is missing or malformed).</summary>
    public static IReadOnlyDictionary<string, JsonElement> GetTokenPayload(string? token)
    {
        if (string.IsNullOrEmpty(token)) return Empty;

        var parts = token.Split('.');
        if (parts.Length < 2) return Empty;

        try
        {
            var b64 = parts[1].Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(b64));

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Empty;

            var result = new Dictionary<string, JsonElement>();
            foreach (var p in doc.RootElement.EnumerateObject()) result[p.Name] = p.Value.Clone();
            return result;
        }
        catch (Exception e) when (e is FormatException or JsonException or ArgumentException)
        {
            return Empty;
        }
    }

    /// <summary>
    /// Checks whether a JWT token is expired.
    /// Tokens without <c>exp</c> claim are considered valid; tokens with empty payload (invalid strings) are considered expired.
    /// </summary>
    /// <param name="token">The token to check.</param>
    /// <param name="expirationThreshold">Seconds subtracted from the token <c>exp</c> claim.</param>
    public static bool IsTokenExpired(string? token, double expirationThreshold = 0)
    {
        var payload = GetTokenPayload(token);
        if (payload.Count == 0) return true;

        if (!payload.TryGetValue("exp", out var expElement)) return false;

        double exp = expElement.ValueKind switch
        {
            JsonValueKind.Number => expElement.GetDouble(),
            JsonValueKind.String when double.TryParse(expElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) => v,
            _ => 0,
        };

        // JS: `!payload.exp` (0/null/empty) is treated as "no expiration"
        if (exp == 0) return false;

        return exp - expirationThreshold <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
    }
}

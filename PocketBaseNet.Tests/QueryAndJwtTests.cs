using System.Text.Json;

namespace PocketBaseNet.Tests;

public class QueryAndJwtTests
{
    // Ported from the official js-sdk tests/tools/options.spec.ts
    [Fact]
    public void SerializeQueryParams_Empty()
    {
        Assert.Equal("", PocketBaseQuery.Serialize(new Dictionary<string, object?>(), new JsonSerializerOptions()));
        Assert.Equal("", PocketBaseQuery.Serialize(new Dictionary<string, object?> { ["a"] = null }, new JsonSerializerOptions()));
    }

    [Fact]
    public void SerializeQueryParams_AllTypes()
    {
        var p = new Dictionary<string, object?>
        {
            ["@test_null"] = null,
            ["@test_date"] = new DateTimeOffset(2025, 1, 1, 1, 2, 3, 456, TimeSpan.Zero),
            ["@test_object"] = new Dictionary<string, object> { ["a"] = 123, ["@b"] = 456 },
            ["@test_number"] = 123.456,
            ["@test_string"] = "@test_str",
            ["@test_bool"] = false,
            ["@test_array"] = new object[]
            {
                123,
                "@test_arr",
                new Dictionary<string, object> { ["@c"] = 789 },
                new DateTimeOffset(2025, 1, 2, 1, 2, 3, 456, TimeSpan.Zero),
                new[] { 4, 5, 6 },
            },
        };

        Assert.Equal(
            "%40test_date=2025-01-01%2001%3A02%3A03.456Z&%40test_object=%7B%22a%22%3A123%2C%22%40b%22%3A456%7D&%40test_number=123.456&%40test_string=%40test_str&%40test_bool=false&%40test_array=123&%40test_array=%40test_arr&%40test_array=%7B%22%40c%22%3A789%7D&%40test_array=2025-01-02%2001%3A02%3A03.456Z&%40test_array=%5B4%2C5%2C6%5D",
            PocketBaseQuery.Serialize(p, new JsonSerializerOptions()));
    }

    // Ported from the official js-sdk tests/tools/jwt.spec.ts
    [Fact]
    public void GetTokenPayload()
    {
        var payload = PocketBaseJwt.GetTokenPayload(Jwt.Create("{\"test\":123}"));
        Assert.Equal(123, payload["test"].GetInt32());

        foreach (var invalid in new[] { "", "abc", "a.b.c" })
            Assert.Empty(PocketBaseJwt.GetTokenPayload(invalid));
    }

    [Fact]
    public void IsTokenExpired()
    {
        Assert.True(PocketBaseJwt.IsTokenExpired(""));
        Assert.True(PocketBaseJwt.IsTokenExpired(Jwt.Create("{}")));                              // empty payload
        Assert.False(PocketBaseJwt.IsTokenExpired(Jwt.Create("{\"test\":123}")));                // no exp
        Assert.True(PocketBaseJwt.IsTokenExpired(Jwt.WithExp(DateTimeOffset.UtcNow.AddMinutes(-1))));
        Assert.False(PocketBaseJwt.IsTokenExpired(Jwt.WithExp(DateTimeOffset.UtcNow.AddMinutes(10))));
        Assert.True(PocketBaseJwt.IsTokenExpired(Jwt.WithExp(DateTimeOffset.UtcNow.AddMinutes(10)), expirationThreshold: 3600));
    }
}

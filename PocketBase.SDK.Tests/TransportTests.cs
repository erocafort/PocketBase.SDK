using System.Net;
using System.Text.Json;
using PocketBase.SDK.Abstractions;

namespace PocketBase.SDK.Tests;

public class TransportTests
{
    private static (PocketBaseTransport Transport, FakeHandler Handler, AuthStore Store) Create(Action<PocketBaseOptions>? configure = null)
    {
        var handler = new FakeHandler();
        var options = new PocketBaseOptions { BaseUrl = "http://127.0.0.1:8090/pb" };
        configure?.Invoke(options);
        var store = new AuthStore();
        return (new PocketBaseTransport(new HttpClient(handler), options, store), handler, store);
    }

    [Theory]
    [InlineData("http://h/pb", "/api/x", "http://h/pb/api/x")]
    [InlineData("http://h/pb/", "api/x", "http://h/pb/api/x")]
    [InlineData("http://h", "", "http://h")]
    public void BuildUrl_ConcatenatesSafely(string baseUrl, string path, string expected)
    {
        var t = new PocketBaseTransport(new HttpClient(), new PocketBaseOptions { BaseUrl = baseUrl }, new AuthStore());
        Assert.Equal(expected, t.BuildUrl(path));
    }

    [Fact]
    public async Task Send_BuildsRequest()
    {
        var (t, h, store) = Create(o => o.Lang = "es-ES");
        var token = Jwt.WithExp(DateTimeOffset.UtcNow.AddHours(1));
        store.Save(token);

        await t.SendAsync("/api/test", new SendOptions
        {
            Method = HttpMethod.Post,
            Query = { ["a"] = 1, ["b"] = null, ["c"] = new[] { "x", "y" } },
            Body = new { Title = "x" },
            Headers = { ["X-Custom"] = "1" },
        });

        var req = h.Last;
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("http://127.0.0.1:8090/pb/api/test?a=1&c=x&c=y", req.RequestUri!.ToString());
        Assert.Equal(token, req.Headers.GetValues("Authorization").Single()); // raw token, no "Bearer"
        Assert.Equal("es-ES", req.Headers.GetValues("Accept-Language").Single());
        Assert.Equal("1", req.Headers.GetValues("X-Custom").Single());
        Assert.Equal("application/json", req.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("{\"Title\":\"x\"}", h.Requests[^1].Body); // names sent as declared
    }

    [Fact]
    public async Task Send_ExplicitHeadersAreNotOverwritten()
    {
        var (t, h, store) = Create();
        store.Save(Jwt.WithExp(DateTimeOffset.UtcNow.AddHours(1)));

        await t.SendAsync("/api/test", new SendOptions { Headers = { ["authorization"] = "custom", ["Accept-Language"] = "fr" } });

        Assert.Equal("custom", h.Last.Headers.GetValues("Authorization").Single());
        Assert.Equal("fr", h.Last.Headers.GetValues("Accept-Language").Single());
    }

    [Fact]
    public async Task Send_ExpiredToken_IsNotSentAndStoreIsCleared()
    {
        var (t, h, store) = Create();
        store.Save(Jwt.WithExp(DateTimeOffset.UtcNow.AddMinutes(-1)));

        await t.SendAsync("/api/test");

        Assert.False(h.Last.Headers.Contains("Authorization"));
        Assert.Equal("", store.Token);
    }

    [Fact]
    public async Task Send_ExpiredToken_KeptWhenAutoClearDisabled()
    {
        var (t, h, store) = Create(o => o.AutoClearAuthStore = false);
        var token = Jwt.WithExp(DateTimeOffset.UtcNow.AddMinutes(-1));
        store.Save(token);

        await t.SendAsync("/api/test");

        Assert.False(h.Last.Headers.Contains("Authorization"));
        Assert.Equal(token, store.Token);
    }

    [Fact]
    public async Task Send_ErrorResponse_ThrowsNormalizedException()
    {
        var (t, h, _) = Create();
        h.Status = HttpStatusCode.BadRequest;
        h.ResponseBody = "{\"status\":400,\"message\":\"Failed to create record.\",\"data\":{\"title\":{\"code\":\"validation_required\"}}}";

        var ex = await Assert.ThrowsAsync<PocketBaseException>(() => t.SendAsync("/api/test"));

        Assert.Equal(400, ex.Status);
        Assert.Equal("Failed to create record.", ex.Message);
        Assert.Equal("validation_required", ex.Response.GetProperty("data").GetProperty("title").GetProperty("code").GetString());
        Assert.False(ex.IsAbort);
        Assert.Equal("http://127.0.0.1:8090/pb/api/test", ex.Url);
    }

    [Fact]
    public async Task Send_401_ClearsStore_AndExposesMfaId()
    {
        var (t, h, store) = Create();
        store.Save(Jwt.WithExp(DateTimeOffset.UtcNow.AddHours(1)));
        h.Status = HttpStatusCode.Unauthorized;
        h.ResponseBody = "{\"mfaId\":\"abc\"}";

        var ex = await Assert.ThrowsAsync<PocketBaseException>(() => t.SendAsync("/api/test"));

        Assert.Equal("abc", ex.Response.GetProperty("mfaId").GetString());
        Assert.Equal("Something went wrong.", ex.Message);
        Assert.Equal("", store.Token);
    }

    [Fact]
    public async Task Send_NetworkError_IsWrapped()
    {
        var (t, h, _) = Create();
        h.Throw = new HttpRequestException("connection refused");

        var ex = await Assert.ThrowsAsync<PocketBaseException>(() => t.SendAsync("/api/test"));

        Assert.Equal(0, ex.Status);
        Assert.IsType<HttpRequestException>(ex.OriginalError);
        Assert.Equal(JsonValueKind.Object, ex.Response.ValueKind);
    }

    [Fact]
    public async Task Send_CallerCancellation_PropagatesOperationCanceled()
    {
        var (t, _, _) = Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => t.SendAsync("/api/test", cancellationToken: cts.Token));
    }

    [Fact]
    public async Task Send_EmptyResponse_ReturnsUndefined()
    {
        var (t, h, _) = Create();
        h.Status = HttpStatusCode.NoContent;
        h.ResponseBody = "";

        var data = await t.SendAsync("/api/test");

        Assert.Equal(JsonValueKind.Undefined, data.ValueKind);
    }

    [Fact]
    public async Task SendGeneric_Deserializes()
    {
        var (t, h, _) = Create();
        h.ResponseBody = "{\"code\":200,\"message\":\"API is healthy.\"}";

        var data = await t.SendAsync<Health>("/api/health");

        Assert.Equal("API is healthy.", data!.Message);
    }

    private sealed record Health(int Code, string Message);
}

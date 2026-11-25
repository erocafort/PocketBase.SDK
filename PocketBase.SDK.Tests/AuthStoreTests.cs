using PocketBase.SDK.Abstractions;

namespace PocketBase.SDK.Tests;

public class AuthStoreTests
{
    private static RecordModel Record(string collectionName) =>
        RecordModel.FromJson($$"""{"id":"id1","collectionId":"cid","collectionName":"{{collectionName}}"}""");


    [Fact]
    public void SaveAndClear()
    {
        var store = new AuthStore();
        var token = Jwt.WithExp(DateTimeOffset.UtcNow.AddHours(1));

        store.Save(token, Record("users"));
        Assert.Equal(token, store.Token);
        Assert.Equal("id1", store.Record?.Id);
        Assert.True(store.IsValid);

        store.Clear();
        Assert.Equal("", store.Token);
        Assert.Null(store.Record);
        Assert.False(store.IsValid);
    }

    [Fact]
    public void IsSuperuser()
    {
        var store = new AuthStore();
        var authToken = Jwt.Create("{\"type\":\"auth\",\"collectionId\":\"" + AuthStore.SuperusersCollectionId + "\"}");

        store.Save(authToken, Record("users"));
        Assert.False(store.IsSuperuser);

        store.Save(authToken, Record("_superusers"));
        Assert.True(store.IsSuperuser);

        store.Save(authToken, null); // fallback to the token collectionId
        Assert.True(store.IsSuperuser);

        store.Save(Jwt.Create("{\"type\":\"file\"}"), Record("_superusers"));
        Assert.False(store.IsSuperuser);
    }

    [Fact]
    public void OnChange_FireImmediatelyAndRemove()
    {
        var store = new AuthStore();
        var calls = new List<string>();

        var sub = store.OnChange((t, _) => calls.Add("1:" + t), fireImmediately: true);
        store.OnChange((t, _) => calls.Add("2:" + t));

        store.Save("a");
        sub.Dispose();
        store.Clear();

        Assert.Equal(new[] { "1:", "1:a", "2:a", "2:" }, calls);
    }

    [Fact]
    public void DeprecatedAliases_AndCookieRoundtrip()
    {
#pragma warning disable CS0618
        var store = new AuthStore();
        var token = Jwt.WithExp(DateTimeOffset.UtcNow.AddHours(1), ",\"type\":\"auth\",\"collectionId\":\"users\"");
        var record = Record("users");

        store.Save(token, record);
        Assert.Equal(record.Id, store.Model?.Id);
        Assert.False(store.IsAdmin);
        Assert.True(store.IsAuthRecord);

        var cookie = store.ExportToCookie();
        var loaded = new AuthStore();
        loaded.LoadFromCookie(cookie);

        Assert.Equal(token, loaded.Token);
        Assert.Equal("id1", loaded.Record?.Id);
#pragma warning restore CS0618
    }

    [Fact]
    public void LoadFromCookie_InvalidJsonFallsBackToEmpty()
    {
        var store = new AuthStore();
        store.LoadFromCookie("pb_auth=not-json");

        Assert.Equal("", store.Token);
        Assert.Null(store.Record);
    }

    [Fact]
    public void ExportToCookie_UsesCustomOptions()
    {
        var store = new AuthStore();
        store.Save(Jwt.WithExp(DateTimeOffset.UtcNow.AddHours(1)), Record("users"));

        var cookie = store.ExportToCookie(new CookieSerializeOptions
        {
            Path = "/api",
            SameSite = "lax",
            Secure = false,
            HttpOnly = false,
            Domain = "example.com",
            MaxAge = 10,
            Priority = "high",
        }, "pb_custom");

        Assert.Contains("pb_custom=", cookie);
        Assert.Contains("; Path=/api", cookie);
        Assert.Contains("; SameSite=Lax", cookie);
        Assert.Contains("; Domain=example.com", cookie);
        Assert.Contains("; Max-Age=10", cookie);
        Assert.DoesNotContain("; Secure", cookie);
        Assert.DoesNotContain("; HttpOnly", cookie);
    }
}

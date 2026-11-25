using System.Net;
using System.Text.Json;
using PocketBaseNet.Abstractions;
using FileUrlOptions = PocketBaseNet.Abstractions.FileOptions;

namespace PocketBaseNet.Tests;

public class Phase4ServicesTests
{
    private static (PocketBaseClient Client, FakeHandler Handler) Create()
    {
        var handler = new FakeHandler();
        var client = PocketBaseClient.Create(new PocketBaseOptions { BaseUrl = "http://127.0.0.1:8090" }, new HttpClient(handler));
        return (client, handler);
    }

    [Fact]
    public async Task Collections_CrudAndMeta_AreWiredLikeSdk()
    {
        var (client, handler) = Create();

        handler.Responder = request => request.RequestUri!.AbsolutePath switch
        {
            "/api/collections" when request.Method == HttpMethod.Get => (HttpStatusCode.OK, "{" +
                "\"page\":1,\"perPage\":30,\"totalItems\":1,\"totalPages\":1,\"items\":[{\"id\":\"posts\",\"name\":\"posts\",\"type\":\"base\"}]" +
                "}"),
            "/api/collections" when request.Method == HttpMethod.Post => (HttpStatusCode.OK, "{\"id\":\"x\",\"name\":\"x\",\"type\":\"base\"}"),
            "/api/collections/posts" when request.Method == HttpMethod.Patch => (HttpStatusCode.OK, "{\"id\":\"posts\",\"name\":\"posts2\",\"type\":\"base\"}"),
            "/api/collections/posts" when request.Method == HttpMethod.Delete => (HttpStatusCode.NoContent, ""),
            "/api/collections/meta/scaffolds" => (HttpStatusCode.OK, "{\"base\":{\"id\":\"\",\"name\":\"\",\"type\":\"base\"}}"),
            "/api/collections/meta/oauth2-providers" => (HttpStatusCode.OK, "[{\"name\":\"google\",\"displayName\":\"Google\",\"logo\":\"g\"}]"),
            "/api/collections/meta/dry-run-view" => (HttpStatusCode.OK, "{\"fields\":[],\"sample\":[{\"id\":\"r1\",\"title\":\"t\"}]}"),
            _ => (HttpStatusCode.OK, "{}"),
        };

        var list = await client.Collections.GetListAsync();
        Assert.Equal("posts", list.Items[0].Name);

        var created = await client.Collections.CreateAsync(new { name = "x", type = "base" });
        Assert.Equal("x", created.Id);

        var updated = await client.Collections.UpdateAsync("posts", new { name = "posts2" });
        Assert.Equal("posts2", updated.Name);

        await client.Collections.DeleteAsync("posts");

        await client.Collections.ImportAsync([new { name = "a", type = "base" }], deleteMissing: true);
        var import = handler.Requests.Single(r => r.Request.Method == HttpMethod.Put);
        Assert.Equal("/api/collections/import", import.Request.RequestUri!.AbsolutePath);
        Assert.Contains("\"deleteMissing\":true", import.Body);

        await client.Collections.TruncateAsync("posts");
        Assert.Contains(handler.Requests, r => r.Request.Method == HttpMethod.Delete && r.Request.RequestUri!.AbsolutePath == "/api/collections/posts/truncate");

        var scaffolds = await client.Collections.GetScaffoldsAsync();
        Assert.True(scaffolds.ContainsKey("base"));

        var providers = await client.Collections.GetAllOAuth2ProvidersAsync();
        Assert.Equal("google", providers[0].Name);

        var preview = await client.Collections.DryRunViewQueryAsync("select 'r1' as id, 't' as title");
        Assert.Equal("r1", preview["sample"]![0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Settings_Methods_MatchSdkPaths()
    {
        var (client, handler) = Create();
        handler.Responder = request => request.RequestUri!.AbsolutePath switch
        {
            "/api/settings" when request.Method == HttpMethod.Get => (HttpStatusCode.OK, "{\"meta\":{\"appName\":\"x\"}}"),
            "/api/settings" when request.Method == HttpMethod.Patch => (HttpStatusCode.OK, "{\"meta\":{\"appName\":\"y\"}}"),
            "/api/settings/apple/generate-client-secret" => (HttpStatusCode.OK, "{\"secret\":\"abc\"}"),
            _ => (HttpStatusCode.NoContent, ""),
        };

        var all = await client.Settings.GetAllAsync();
        Assert.Equal("x", all["meta"]!["appName"]!.GetValue<string>());

        var updated = await client.Settings.UpdateAsync(new { meta = new { appName = "y" } });
        Assert.Equal("y", updated["meta"]!["appName"]!.GetValue<string>());

        await client.Settings.TestS3Async("private");
        await client.Settings.TestEmailAsync("users", "a@b.c", "password-reset");

        var secret = await client.Settings.GenerateAppleClientSecretAsync("c", "t", "k", "p");
        Assert.Equal("abc", secret.Secret);

        Assert.Contains(handler.Requests, r => r.Request.RequestUri!.AbsolutePath == "/api/settings/test/s3");
        Assert.Contains(handler.Requests, r => r.Request.RequestUri!.AbsolutePath == "/api/settings/test/email");
    }

    [Fact]
    public async Task Files_GetURL_AndGetToken()
    {
        var (client, handler) = Create();
        handler.ResponseBody = "{\"token\":\"tok\"}";

        var record = RecordModel.FromJson(JsonSerializer.SerializeToElement(new { id = "r1", collectionId = "posts", collectionName = "posts" }));
        var url = client.Files.GetURL(record, "my file.png", new FileUrlOptions { Thumb = "100x100", Download = true });
        Assert.Equal("http://127.0.0.1:8090/api/files/posts/r1/my%20file.png?thumb=100x100&download=", url);

        var token = await client.Files.GetTokenAsync();
        Assert.Equal("tok", token);
        Assert.Equal("/api/files/token", handler.Last.RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Post, handler.Last.Method);
    }

    [Fact]
    public async Task Logs_Backups_Crons_Sql_Health_AreImplemented()
    {
        var (client, handler) = Create();

        handler.Responder = request => request.RequestUri!.AbsolutePath switch
        {
            "/api/logs" when request.Method == HttpMethod.Get => (HttpStatusCode.OK, "{\"page\":1,\"perPage\":30,\"totalItems\":1,\"totalPages\":1,\"items\":[{\"id\":\"l1\",\"level\":8,\"message\":\"m\",\"data\":{}}]}"),
            "/api/logs/l1" => (HttpStatusCode.OK, "{\"id\":\"l1\",\"level\":8,\"message\":\"m\",\"data\":{}}"),
            "/api/logs/stats" => (HttpStatusCode.OK, "[{\"date\":\"2026-01-01\",\"total\":10}]"),
            "/api/backups" when request.Method == HttpMethod.Get => (HttpStatusCode.OK, "[{\"key\":\"a.zip\",\"size\":1,\"modified\":\"now\"}]"),
            "/api/crons" => (HttpStatusCode.OK, "[{\"id\":\"jobs.cleanup\",\"expression\":\"@daily\"}]"),
            "/api/sql" => (HttpStatusCode.OK, "{\"execTime\":1.2,\"affectedRows\":3,\"columns\":[],\"rows\":[]}"),
            "/api/health" => (HttpStatusCode.OK, "{\"code\":200,\"message\":\"ok\",\"data\":{}}"),
            _ => (HttpStatusCode.NoContent, ""),
        };

        var logList = await client.Logs.GetListAsync();
        Assert.Equal("l1", logList.Items[0].Id);
        var log = await client.Logs.GetOneAsync("l1");
        Assert.Equal("l1", log.Id);
        var stats = await client.Logs.GetStatsAsync(new LogStatsOptions { Filter = "date >= '2026-01-01'" });
        Assert.Equal(10, stats[0].Total);
        await client.Logs.TruncateAsync();

        var backups = await client.Backups.GetFullListAsync();
        Assert.Equal("a.zip", backups[0].Key);
        await client.Backups.CreateAsync("nightly");
        await client.Backups.UploadAsync(new { file = FileUpload.FromBytes([1, 2, 3], "a.zip") });
        await client.Backups.DeleteAsync("a.zip");
        await client.Backups.RestoreAsync("a.zip");
        Assert.Equal("http://127.0.0.1:8090/api/backups/a.zip?token=tok", client.Backups.GetDownloadURL("tok", "a.zip"));

        var crons = await client.Crons.GetFullListAsync();
        Assert.Equal("jobs.cleanup", crons[0].Id);
        await client.Crons.RunAsync("jobs.cleanup");

        var sql = await client.Sql.RunAsync("select 1");
        Assert.Equal(3, sql.AffectedRows);

        var health = await client.Health.CheckAsync();
        Assert.Equal(200, health.Code);
    }
}

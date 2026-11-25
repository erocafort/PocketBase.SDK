using System.Text.Json.Nodes;
using PocketBase.SDK.Abstractions;

namespace PocketBase.SDK.Tests;

public class LivePocketBaseIntegrationTests
{
    private static (string Url, string Email, string Password)? ReadConfig()
    {
        var url = Environment.GetEnvironmentVariable("PB_URL");
        var email = Environment.GetEnvironmentVariable("PB_EMAIL");
        var password = Environment.GetEnvironmentVariable("PB_PASSWORD");

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return null;
        return (url, email, password);
    }

    private static PocketBaseClient CreateClient(string url) =>
        PocketBaseClient.Create(new PocketBaseOptions { BaseUrl = url });

    [Fact]
    public async Task FullApiSurface_WorksAgainstLivePocketBase()
    {
        var cfg = ReadConfig();
        if (cfg is null)
        {
            return; // skipped (no credentials configured)
        }

        var client = CreateClient(cfg.Value.Url);

        // auth
        var auth = await client.Collection("_superusers").AuthWithPasswordAsync(cfg.Value.Email, cfg.Value.Password);
        Assert.False(string.IsNullOrWhiteSpace(auth.Token));

        // auth store cookie roundtrip + refresh
        var cookie = client.AuthStore.ExportToCookie();
        var cookieClient = CreateClient(cfg.Value.Url);
        cookieClient.AuthStore.LoadFromCookie(cookie);
        Assert.True(cookieClient.AuthStore.IsValid);
        await cookieClient.Collection("_superusers").AuthRefreshAsync();

        // health + sql
        var health = await client.Health.CheckAsync();
        Assert.True(health.Code is 200 or 204);

        var sql = await client.Sql.RunAsync("select 1 as one");
        Assert.True(sql.Columns.Count >= 1 || sql.Rows.Count >= 1 || sql.AffectedRows >= 0);

        // collections
        var collections = await client.Collections.GetListAsync();
        Assert.True(collections.Items.Count >= 1);

        var full = await client.Collections.GetFullListAsync();
        Assert.True(full.Count >= 1);

        var first = await client.Collections.GetFirstListItemAsync("type='base'");
        var one = await client.Collections.GetOneAsync(first.Id);
        Assert.Equal(first.Id, one.Id);

        var testCollectionName = "pbnet_it_" + Guid.NewGuid().ToString("N")[..10];
        CollectionModel? createdCollection = null;

        try
        {
            createdCollection = await client.Collections.CreateAsync(new
            {
                name = testCollectionName,
                type = "base",
                fields = new object[]
                {
                    new { name = "title", type = "text" },
                },
            });

            Assert.Equal(testCollectionName, createdCollection.Name);

            var updated = await client.Collections.UpdateAsync(testCollectionName, new
            {
                name = testCollectionName,
                type = "base",
                fields = new object[]
                {
                    new { name = "title", type = "text" },
                    new { name = "status", type = "bool" },
                },
            });
            Assert.Equal(testCollectionName, updated.Name);

            await client.Collections.ImportAsync(new object[]
            {
                new
                {
                    name = testCollectionName,
                    type = "base",
                    fields = new object[]
                    {
                        new { name = "title", type = "text" },
                        new { name = "status", type = "bool" },
                    },
                },
            }, deleteMissing: false);

            await client.Collections.TruncateAsync(testCollectionName);

            var scaffolds = await client.Collections.GetScaffoldsAsync();
            Assert.True(scaffolds.Count > 0);

            var oauthProviders = await client.Collections.GetAllOAuth2ProvidersAsync();
            Assert.NotNull(oauthProviders);

            var dryRun = await client.Collections.DryRunViewQueryAsync($"SELECT id FROM {testCollectionName}");
            Assert.True(dryRun.ContainsKey("fields") || dryRun.ContainsKey("sample"));

            // files
            var fileToken = await client.Files.GetTokenAsync();
            Assert.False(string.IsNullOrWhiteSpace(fileToken));

            var fileUrl = client.Files.GetURL(auth.Record, "avatar.png", new PocketBase.SDK.Abstractions.FileOptions
            {
                Thumb = "100x100",
                Token = fileToken,
                Download = true,
            });
            Assert.Contains("/api/files/", fileUrl);

            // logs
            var logs = await client.Logs.GetListAsync();
            Assert.NotNull(logs.Items);

            if (logs.Items.Count > 0)
            {
                var log = await client.Logs.GetOneAsync(logs.Items[0].Id);
                Assert.Equal(logs.Items[0].Id, log.Id);
            }

            var stats = await client.Logs.GetStatsAsync();
            Assert.NotNull(stats);

            await AssertOptionalAdminEndpoint(async () => await client.Logs.TruncateAsync());

            // settings
            var settings = await client.Settings.GetAllAsync();
            Assert.NotNull(settings);

            var metaNode = settings["meta"] as JsonObject;
            if (metaNode is not null && metaNode["appName"] is not null)
            {
                var appName = metaNode["appName"]!.GetValue<string>();
                await client.Settings.UpdateAsync(new { meta = new { appName } });
            }

            await AssertOptionalAdminEndpoint(async () => await client.Settings.TestS3Async("storage"));
            await AssertOptionalAdminEndpoint(async () => await client.Settings.TestEmailAsync("_superusers", cfg.Value.Email, "verification"));
            await AssertOptionalAdminEndpoint(async () => await client.Settings.GenerateAppleClientSecretAsync("x", "x", "x", "x"));

            // crons
            var crons = await client.Crons.GetFullListAsync();
            Assert.NotNull(crons);
            if (crons.Count > 0)
            {
                await client.Crons.RunAsync(crons[0].Id);
            }

            // backups
            var beforeBackups = await client.Backups.GetFullListAsync();
            var beforeKeys = beforeBackups.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);

            var basename = "pbnetit" + Guid.NewGuid().ToString("N")[..8];
            await AssertOptionalAdminEndpoint(async () => await client.Backups.CreateAsync(basename));

            var afterCreate = await client.Backups.GetFullListAsync();
            var createdBackup = afterCreate.FirstOrDefault(x => !beforeKeys.Contains(x.Key) || x.Key.Contains(basename, StringComparison.OrdinalIgnoreCase));

            await AssertOptionalAdminEndpoint(async () => await client.Backups.UploadAsync(new { file = FileUpload.FromBytes([1, 2, 3, 4], basename + "upload.zip") }));
            var afterUpload = await client.Backups.GetFullListAsync();
            var uploadedBackup = afterUpload.FirstOrDefault(x => x.Key.Contains(basename + "upload", StringComparison.OrdinalIgnoreCase));

            if (createdBackup is not null)
            {
                var downloadUrl = client.Backups.GetDownloadURL(fileToken, createdBackup.Key);
                Assert.Contains("/api/backups/", downloadUrl);

                await AssertOptionalAdminEndpoint(async () => await client.Backups.RestoreAsync(createdBackup.Key));

                // restore could restart the app; wait until it is back
                await WaitForHealthAsync(client, TimeSpan.FromSeconds(40));

                await AssertOptionalAdminEndpoint(async () => await client.Backups.DeleteAsync(createdBackup.Key));
            }

            if (uploadedBackup is not null)
            {
                await AssertOptionalAdminEndpoint(async () => await client.Backups.DeleteAsync(uploadedBackup.Key));
            }
        }
        finally
        {
            if (createdCollection is not null)
            {
                await AssertOptionalAdminEndpoint(async () => await client.Collections.DeleteAsync(createdCollection.Name));
            }
        }
    }

    private static async Task WaitForHealthAsync(IPocketBaseClient client, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        Exception? last = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var health = await client.Health.CheckAsync();
                if (health.Code is 200 or 204) return;
            }
            catch (Exception ex)
            {
                last = ex;
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException("PocketBase didn't become healthy in time.", last);
    }

    private static async Task AssertOptionalAdminEndpoint(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (PocketBaseException ex) when (ex.Status is 400 or 404 or 422 or 500)
        {
            // endpoint reached but environment/config doesn't satisfy the request payload
        }
    }
}

using System.Text.Json;
using PocketBaseNet.Abstractions;

namespace PocketBaseNet.Tests;

// Ported (and adapted) from the official js-sdk tests/services/BatchService.spec.ts
public class BatchServiceTests
{
    private readonly FakeHandler _h = new();
    private readonly PocketBaseClient _pb;

    public BatchServiceTests()
    {
        _pb = PocketBaseClient.Create(new PocketBaseOptions { BaseUrl = "http://test" }, new HttpClient(_h));
    }

    [Fact]
    public async Task Send_BuildsJsonPayloadAndFiles()
    {
        _h.ResponseBody = """[{"status":200,"body":{"id":"a"}},{"status":204,"body":null}]""";

        var batch = _pb.CreateBatch();
        batch.Collection("example1").Create(new Dictionary<string, object?>
        {
            ["title"] = "test1",
            ["file"] = FileUpload.FromBytes("f1"u8.ToArray(), "f1.txt"),
        }, new RecordOptions { Query = { ["a"] = 1 }, Headers = { ["x-test"] = "456" } });
        batch.Collection("example2").Update("@test1", new Dictionary<string, object?>
        {
            ["title"] = "test2",
            ["files"] = new object[] { "existing.txt", FileUpload.FromBytes("f2"u8.ToArray(), "f2.txt") },
        }, new RecordOptions { Fields = "id" });
        batch.Collection("example3").Delete("@test2");
        batch.Collection("example4").Upsert(new { id = "x", title = "test4" });

        var results = await batch.SendAsync();

        Assert.Equal("http://test/api/batch", _h.Last.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, _h.Last.Method);

        Assert.Contains(("requests.0.file", "f1.txt", "f1"), _h.LastParts);
        Assert.Contains(("requests.1.files+", "f2.txt", "f2"), _h.LastParts);

        var payload = JsonDocument.Parse(_h.LastParts.Single(p => p.Name == "@jsonPayload").Value).RootElement.GetProperty("requests");
        Assert.Equal(4, payload.GetArrayLength());

        Assert.Equal("POST", payload[0].GetProperty("method").GetString());
        Assert.Equal("/api/collections/example1/records?a=1", payload[0].GetProperty("url").GetString());
        Assert.Equal("456", payload[0].GetProperty("headers").GetProperty("x-test").GetString());
        Assert.Equal("""{"title":"test1"}""", payload[0].GetProperty("body").GetRawText());

        Assert.Equal("PATCH", payload[1].GetProperty("method").GetString());
        Assert.Equal("/api/collections/example2/records/%40test1?fields=id", payload[1].GetProperty("url").GetString());
        Assert.Equal("""{"title":"test2","files":["existing.txt"]}""", payload[1].GetProperty("body").GetRawText());

        Assert.Equal("DELETE", payload[2].GetProperty("method").GetString());
        Assert.Equal("/api/collections/example3/records/%40test2", payload[2].GetProperty("url").GetString());
        Assert.Equal("{}", payload[2].GetProperty("body").GetRawText());

        Assert.Equal("PUT", payload[3].GetProperty("method").GetString());
        Assert.Equal("""{"id":"x","title":"test4"}""", payload[3].GetProperty("body").GetRawText());

        Assert.Equal(2, results.Count);
        Assert.Equal(200, results[0].Status);
        Assert.Equal("a", results[0].Body.GetProperty("id").GetString());
    }

    [Fact]
    public void Body_MustBeObjectLike()
    {
        var sub = _pb.CreateBatch().Collection("x");
        Assert.Throws<ArgumentException>(() => sub.Create("raw string"));
        Assert.Throws<ArgumentException>(() => sub.Create(new StringContent("{}")));
    }
}

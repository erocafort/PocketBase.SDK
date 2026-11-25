using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using PocketBase.SDK.Abstractions;

namespace PocketBase.SDK.Tests;

// Ported (and adapted) from the official js-sdk tests/services/RecordService.spec.ts and suites.ts
public class RecordServiceTests
{
    private readonly FakeHandler _h = new();
    private readonly PocketBaseClient _pb;
    private readonly IRecordService _service;
    private readonly string _validToken = Jwt.WithExp(DateTimeOffset.UtcNow.AddHours(1));

    public RecordServiceTests()
    {
        _pb = PocketBaseClient.Create(new PocketBaseOptions { BaseUrl = "http://test/" }, new HttpClient(_h));
        _service = _pb.Collection("sub=");
    }

    private static RecordModel Rec(string json) => RecordModel.FromJson(json);

    private string LastUrl => _h.Last.RequestUri!.ToString();

    // ---------------- CRUD ----------------

    [Fact]
    public void Paths_AreEscaped()
    {
        Assert.Equal("/api/collections/sub%3D", _service.BaseCollectionPath);
        Assert.Equal("/api/collections/sub%3D/records", _service.BaseCrudPath);
    }

    [Fact]
    public async Task GetList_SendsQueryAndParsesResult()
    {
        _h.ResponseBody = """{"page":2,"perPage":1,"totalItems":3,"totalPages":3,"items":[{"id":"item3","title":"t"}]}""";

        var result = await _service.GetListAsync(2, 1, new RecordListOptions { Filter = "a=1", Sort = "-created", Expand = "rel", Query = { ["q1"] = "abc" } });

        Assert.Equal("http://test/api/collections/sub%3D/records?page=2&perPage=1&q1=abc&filter=a%3D1&sort=-created&expand=rel", LastUrl);
        Assert.Equal(HttpMethod.Get, _h.Last.Method);
        Assert.Equal(3, result.TotalItems);
        Assert.Equal("item3", result.Items.Single().Id);
        Assert.Equal("t", result.Items.Single().Get<string>("title"));
    }

    [Fact]
    public async Task GetFullList_PaginatesUntilPartialPage()
    {
        _h.Responder = req => req.RequestUri!.Query.Contains("page=1&")
            ? (HttpStatusCode.OK, """{"page":1,"perPage":2,"items":[{"id":"a"},{"id":"b"}]}""")
            : (HttpStatusCode.OK, """{"page":2,"perPage":2,"items":[{"id":"c"}]}""");

        var items = await _service.GetFullListAsync(new RecordFullListOptions { Batch = 2, Filter = "x" });

        Assert.Equal(new[] { "a", "b", "c" }, items.Select(i => i.Id));
        Assert.Equal(2, _h.Requests.Count);
        Assert.Equal("http://test/api/collections/sub%3D/records?page=1&perPage=2&skipTotal=1&filter=x", _h.Requests[0].Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetFirstListItem_ReturnsFirstOr404()
    {
        _h.ResponseBody = """{"page":1,"perPage":1,"items":[{"id":"first"}]}""";
        var item = await _service.GetFirstListItemAsync("name='x'");
        Assert.Equal("first", item.Id);
        Assert.Equal("http://test/api/collections/sub%3D/records?page=1&perPage=1&filter=name%3D%27x%27&skipTotal=1", LastUrl);

        _h.ResponseBody = """{"page":1,"perPage":1,"items":[]}""";
        var ex = await Assert.ThrowsAsync<PocketBaseException>(() => _service.GetFirstListItemAsync("x"));
        Assert.Equal(404, ex.Status);
    }

    [Fact]
    public async Task GetOne_EmptyIdThrows404WithoutRequest()
    {
        var ex = await Assert.ThrowsAsync<PocketBaseException>(() => _service.GetOneAsync(""));
        Assert.Equal(404, ex.Status);
        Assert.Equal("Missing required record id.", ex.Message);
        Assert.Empty(_h.Requests);
    }

    [Fact]
    public async Task GetOne_Typed()
    {
        _h.ResponseBody = """{"id":"abc","title":"Hello","count":2}""";

        var post = await _service.GetOneAsync<Post>("a/b", new RecordOptions { Expand = "author", Fields = "id,title" });

        Assert.Equal("http://test/api/collections/sub%3D/records/a%2Fb?fields=id%2Ctitle&expand=author", LastUrl);
        Assert.Equal("Hello", post.Title);
        Assert.Equal(2, post.Count);
    }

    [Fact]
    public async Task Create_SendsJson()
    {
        _h.ResponseBody = """{"id":"new"}""";

        var created = await _service.CreateAsync(new Dictionary<string, object?> { ["title"] = "x" });

        Assert.Equal(HttpMethod.Post, _h.Last.Method);
        Assert.Equal("""{"title":"x"}""", _h.LastBody);
        Assert.Equal("new", created.Id);
    }

    [Fact]
    public async Task Create_WithFiles_SendsMultipartWithJsonPayload()
    {
        _h.ResponseBody = """{"id":"new"}""";

        await _service.CreateAsync(new
        {
            title = "x",
            count = 5,
            tags = new[] { "a", "b" },
            document = FileUpload.FromBytes("hello"u8.ToArray(), "a.txt", "text/plain"),
        });

        Assert.IsType<MultipartFormDataContent>(_h.Last.Content);
        Assert.Contains(("title", null, "x"), _h.LastParts);
        Assert.Contains(("count", null, "5"), _h.LastParts);
        Assert.Contains(("@jsonPayload", null, """{"tags":["a","b"]}"""), _h.LastParts);
        Assert.Contains(("document", "a.txt", "hello"), _h.LastParts);
    }

    [Fact]
    public async Task Update_MergesAuthRecordOnMatchingIdAndCollection()
    {
        _h.ResponseBody = """{"id":"test123","email":"new@example.com","name":"name_new","expand":{"b":3}}""";
        _pb.AuthStore.Save(_validToken, Rec("""{"id":"test123","collectionName":"sub=","name":"abc","expand":{"a":1,"b":2}}"""));

        await _service.UpdateAsync("test123", new { });

        var r = _pb.AuthStore.Record!;
        Assert.Equal("test123", r.Id);
        Assert.Equal("sub=", r.CollectionName);
        Assert.Equal("name_new", r.Get<string>("name"));
        Assert.Equal("new@example.com", r.Get<string>("email"));
        Assert.Equal(1, r.Get<int>("expand.a"));
        Assert.Equal(3, r.Get<int>("expand.b"));
        Assert.Equal(_validToken, _pb.AuthStore.Token);
    }

    [Theory]
    [InlineData("""{"id":"test123","email":"old@example.com","collectionName":"diff"}""")] // mismatched collection
    [InlineData("""{"id":"test456","email":"old@example.com","collectionName":"sub="}""")] // mismatched id
    public async Task Update_DoesNotTouchAuthRecordOnMismatch(string authRecord)
    {
        _h.ResponseBody = """{"id":"test123","email":"new@example.com"}""";
        _pb.AuthStore.Save(_validToken, Rec(authRecord));

        await _service.UpdateAsync("test123", new { });

        Assert.Equal("old@example.com", _pb.AuthStore.Record!.Get<string>("email"));
    }

    [Fact]
    public async Task Delete_ClearsAuthStoreOnlyOnMatch()
    {
        _h.Status = HttpStatusCode.NoContent;
        _h.ResponseBody = "";

        _pb.AuthStore.Save(_validToken, Rec("""{"id":"test123","collectionName":"diff"}"""));
        await _service.DeleteAsync("test123");
        Assert.NotNull(_pb.AuthStore.Record);
        Assert.Equal(HttpMethod.Delete, _h.Last.Method);

        _pb.AuthStore.Save(_validToken, Rec("""{"id":"test123","collectionName":"sub="}"""));
        await _service.DeleteAsync("test123");
        Assert.Null(_pb.AuthStore.Record);
        Assert.Equal("", _pb.AuthStore.Token);
    }

    // ---------------- Auth ----------------

    [Fact]
    public async Task ListAuthMethods()
    {
        _h.ResponseBody = """{"password":{"enabled":true,"identityFields":["email"]},"oauth2":{"enabled":true,"providers":[{"name":"google","authURL":"https://x","codeVerifier":"cv"}]},"mfa":{"enabled":false,"duration":0},"otp":{"enabled":true,"duration":180}}""";

        var methods = await _service.ListAuthMethodsAsync(new CommonOptions { Query = { ["q1"] = 1 } });

        Assert.Equal("http://test/api/collections/sub%3D/auth-methods?fields=mfa%2Cotp%2Cpassword%2Coauth2&q1=1", LastUrl);
        Assert.True(methods.Password.Enabled);
        Assert.Equal("email", methods.Password.IdentityFields.Single());
        Assert.Equal("https://x", methods.OAuth2.Providers.Single().AuthUrl);
        Assert.Equal(180, methods.Otp.Duration);
    }

    [Fact]
    public async Task AuthWithPassword_SavesAuthStoreAndReturnsResponse()
    {
        var token = Jwt.WithExp(DateTimeOffset.UtcNow.AddHours(1));
        _h.ResponseBody = $$$"""{"token":"{{{token}}}","record":{"id":"id1","collectionName":"sub=","email":"a@b.c"}}""";

        var changes = 0;
        using var _ = _pb.AuthStore.OnChange((_, _) => changes++);

        var auth = await _service.AuthWithPasswordAsync("a@b.c", "123456", new RecordOptions
        {
            Expand = "rel",
            Body = { ["extra"] = "e", ["identity"] = "ignored" }, // explicit args win
        });

        Assert.Equal("http://test/api/collections/sub%3D/auth-with-password?expand=rel", LastUrl);
        Assert.Equal("""{"extra":"e","identity":"a@b.c","password":"123456"}""", _h.LastBody);
        Assert.Equal(token, auth.Token);
        Assert.Equal("id1", auth.Record.Id);
        Assert.Equal(token, _pb.AuthStore.Token);
        Assert.Equal("a@b.c", _pb.AuthStore.Record!.Get<string>("email"));
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task AuthWithOAuth2Code_SendsExpectedBody_AndExposesMeta()
    {
        _h.ResponseBody = $$$"""{"token":"{{{_validToken}}}","record":{"id":"id1"},"meta":{"isNew":true,"accessToken":"at"}}""";

        var auth = await _service.AuthWithOAuth2CodeAsync("google", "c", "cv", "https://r", new { name = "n" });

        Assert.Equal("""{"provider":"google","code":"c","codeVerifier":"cv","redirectURL":"https://r","createData":{"name":"n"}}""", _h.LastBody);
        Assert.True(auth.Meta!["isNew"]!.GetValue<bool>());
        Assert.Equal("at", auth.Meta!["accessToken"]!.GetValue<string>());
    }

    [Fact]
    public async Task AuthWithPassword_SuperuserAutoRefreshThreshold_RefreshesBeforeRequest()
    {
        var superuser = _pb.Collection("_superusers");
        var tokenExpiringSoon = Jwt.WithExp(DateTimeOffset.UtcNow.AddSeconds(5));
        var refreshedToken = Jwt.WithExp(DateTimeOffset.UtcNow.AddHours(1));

        _h.Responder = req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/auth-with-password"))
                return (HttpStatusCode.OK, $$$"""{"token":"{{{tokenExpiringSoon}}}","record":{"id":"su1","collectionName":"_superusers"}}""");

            if (path.EndsWith("/auth-refresh"))
                return (HttpStatusCode.OK, $$$"""{"token":"{{{refreshedToken}}}","record":{"id":"su1","collectionName":"_superusers"}}""");

            if (path.EndsWith("/records"))
                return (HttpStatusCode.OK, "{\"items\":[]}");

            return (HttpStatusCode.NotFound, "{}");
        };

        await superuser.AuthWithPasswordAsync("root@example.com", "secret", new RecordOptions { AutoRefreshThreshold = 3600 });
        await superuser.GetListAsync();

        Assert.Equal(3, _h.Requests.Count);
        Assert.EndsWith("/auth-with-password", _h.Requests[0].Request.RequestUri!.AbsolutePath);
        Assert.EndsWith("/auth-refresh?autoRefresh=true", _h.Requests[1].Request.RequestUri!.PathAndQuery);
        Assert.Equal(refreshedToken, _h.Requests[2].Request.Headers.GetValues("Authorization").Single());
    }

    [Fact]
    public async Task AuthRefresh_SendsCurrentTokenAndUpdatesStore()
    {
        var newToken = Jwt.WithExp(DateTimeOffset.UtcNow.AddHours(2));
        _pb.AuthStore.Save(_validToken, Rec("""{"id":"id1"}"""));
        _h.ResponseBody = $$$"""{"token":"{{{newToken}}}","record":{"id":"id1","name":"n"}}""";

        await _service.AuthRefreshAsync();

        Assert.Equal(HttpMethod.Post, _h.Last.Method);
        Assert.Equal("http://test/api/collections/sub%3D/auth-refresh", LastUrl);
        Assert.Equal(_validToken, _h.Last.Headers.GetValues("Authorization").Single());
        Assert.Equal(newToken, _pb.AuthStore.Token);
        Assert.Equal("n", _pb.AuthStore.Record!.Get<string>("name"));
    }

    [Fact]
    public async Task Otp_RequestAndAuth()
    {
        _h.ResponseBody = """{"otpId":"otp1"}""";
        var otp = await _service.RequestOtpAsync("a@b.c");
        Assert.Equal("otp1", otp.OtpId);
        Assert.Equal("""{"email":"a@b.c"}""", _h.LastBody);
        Assert.Equal("http://test/api/collections/sub%3D/request-otp", LastUrl);

        _h.ResponseBody = $$$"""{"token":"{{{_validToken}}}","record":{"id":"id1"}}""";
        await _service.AuthWithOtpAsync("otp1", "123");
        Assert.Equal("""{"otpId":"otp1","password":"123"}""", _h.LastBody);
        Assert.Equal(_validToken, _pb.AuthStore.Token);
    }

    [Fact]
    public async Task MfaRequired_401ExposesMfaId_AndCanBeForwardedInBody()
    {
        _h.Status = HttpStatusCode.Unauthorized;
        _h.ResponseBody = """{"mfaId":"mfa1"}""";

        var ex = await Assert.ThrowsAsync<PocketBaseException>(() => _service.AuthWithPasswordAsync("a", "b"));
        var mfaId = ex.Response.GetProperty("mfaId").GetString();

        _h.Status = HttpStatusCode.OK;
        _h.ResponseBody = $$$"""{"token":"{{{_validToken}}}","record":{"id":"id1"}}""";
        await _service.AuthWithOtpAsync("otp1", "123", new RecordOptions { Body = { ["mfaId"] = mfaId } });

        Assert.Equal("""{"mfaId":"mfa1","otpId":"otp1","password":"123"}""", _h.LastBody);
    }

    [Theory]
    [InlineData("request-password-reset")]
    [InlineData("confirm-password-reset")]
    [InlineData("request-verification")]
    [InlineData("request-email-change")]
    public async Task ActionEndpoints_SendExpectedBodies(string action)
    {
        _h.Status = HttpStatusCode.NoContent;
        _h.ResponseBody = "";

        var (task, body) = action switch
        {
            "request-password-reset" => (_service.RequestPasswordResetAsync("a@b.c"), """{"email":"a@b.c"}"""),
            "confirm-password-reset" => (_service.ConfirmPasswordResetAsync("tk", "p1", "p2"), """{"token":"tk","password":"p1","passwordConfirm":"p2"}"""),
            "request-verification" => (_service.RequestVerificationAsync("a@b.c"), """{"email":"a@b.c"}"""),
            _ => (_service.RequestEmailChangeAsync("n@b.c"), """{"newEmail":"n@b.c"}"""),
        };
        await task;

        Assert.Equal(HttpMethod.Post, _h.Last.Method);
        Assert.Equal("http://test/api/collections/sub%3D/" + action, LastUrl);
        Assert.Equal(body, _h.LastBody);
    }

    [Fact]
    public async Task ConfirmVerification_MarksAuthRecordVerifiedOnMatch()
    {
        _h.Status = HttpStatusCode.NoContent;
        _h.ResponseBody = "";
        var verificationToken = Jwt.Create("""{"id":"id1","collectionId":"c1","type":"verification"}""");

        _pb.AuthStore.Save(_validToken, Rec("""{"id":"id1","collectionId":"c1","verified":false}"""));
        await _service.ConfirmVerificationAsync(verificationToken);

        Assert.True(_pb.AuthStore.Record!.Get<bool>("verified"));
        Assert.Equal($$$"""{"token":"{{{verificationToken}}}"}""", _h.LastBody);
    }

    [Fact]
    public async Task ConfirmEmailChange_ClearsAuthStoreOnMatch()
    {
        _h.Status = HttpStatusCode.NoContent;
        _h.ResponseBody = "";
        var changeToken = Jwt.Create("""{"id":"id1","collectionId":"c1","type":"emailChange"}""");

        _pb.AuthStore.Save(_validToken, Rec("""{"id":"id2","collectionId":"c1"}"""));
        await _service.ConfirmEmailChangeAsync(changeToken, "pass");
        Assert.NotNull(_pb.AuthStore.Record);

        _pb.AuthStore.Save(_validToken, Rec("""{"id":"id1","collectionId":"c1"}"""));
        await _service.ConfirmEmailChangeAsync(changeToken, "pass");
        Assert.Null(_pb.AuthStore.Record);
    }

    [Fact]
    public async Task Impersonate_ReturnsNewClientWithoutTouchingCurrent()
    {
        var impersonateToken = Jwt.WithExp(DateTimeOffset.UtcNow.AddHours(1));
        _h.ResponseBody = $$$"""{"token":"{{{impersonateToken}}}","record":{"id":"impersonate_record_id"}}""";
        _pb.AuthStore.Save(_validToken);

        var other = await _service.ImpersonateAsync("@test", 3600, new RecordOptions
        {
            Query = { ["q1"] = 456 },
            Headers = { ["x-test"] = "789" },
        });

        Assert.Equal("http://test/api/collections/sub%3D/impersonate/%40test?q1=456", LastUrl);
        Assert.Equal("""{"duration":3600}""", _h.LastBody);
        Assert.Equal(_validToken, _h.Last.Headers.GetValues("Authorization").Single());
        Assert.Equal("789", _h.Last.Headers.GetValues("x-test").Single());

        Assert.Equal(impersonateToken, other.AuthStore.Token);
        Assert.Equal("impersonate_record_id", other.AuthStore.Record!.Id);
        Assert.Equal(_validToken, _pb.AuthStore.Token); // current client untouched
        Assert.NotSame(_pb.AuthStore, other.AuthStore);
    }

    // ---------------- RecordModel ----------------

    [Fact]
    public void RecordModel_GetWithDotNotationAndCasting()
    {
        var r = Rec("""{"id":"x","n":"12","b":1,"list":[1,"2"],"expand":{"author":{"id":"a1","name":"Ana"},"tags":[{"id":"t1"},{"id":"t2"}]}}""");

        Assert.Equal("Ana", r.Get<string>("expand.author.name"));
        Assert.Equal("t2", r.Get<string>("expand.tags.1.id"));
        Assert.Equal("a1", r.Get<RecordModel>("expand.author").Id);
        Assert.Equal(new[] { "t1", "t2" }, r.Get<List<RecordModel>>("expand.tags").Select(t => t.Id));
        Assert.Equal(12, r.Get<int>("n"));
        Assert.True(r.Get<bool>("b"));
        Assert.Equal(new[] { 1, 2 }, r.Get<List<int>>("list"));
        Assert.Equal("", r.Get<string>("missing"));
        Assert.Equal("def", r.Get("missing", "def"));
        Assert.Null(r.Get<int?>("missing"));
        Assert.Empty(r.Get<List<string>>("missing"));
    }

    [Fact]
    public void RecordModel_JsonRoundTrip()
    {
        var r = Rec("""{"id":"x","title":"t"}""");
        Assert.Equal("""{"id":"x","title":"t"}""", JsonSerializer.Serialize(r));
        Assert.Equal("t", JsonSerializer.Deserialize<RecordModel>("""{"title":"t"}""")!.Get<string>("title"));
        Assert.Equal("t", r.ToObject<Post>()!.Title);
    }

    private sealed class Post
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public int Count { get; set; }
    }
}

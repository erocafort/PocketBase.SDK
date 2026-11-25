using System.Net;
using System.Text.Json;
using PocketBaseNet.Abstractions;

namespace PocketBaseNet.Tests;

public class SseParserTests
{
    private static List<SseMessage> Parse(params string[] lines)
    {
        var parser = new SseParser();
        return lines.Select(parser.ProcessLine).Where(m => m is not null).Select(m => m!).ToList();
    }

    [Fact]
    public void ParsesEvents_FollowingTheEventSourceRules()
    {
        var messages = Parse(
            ": keep-alive comment",
            "id:abc",
            "event:PB_CONNECT",
            "data:{\"clientId\":\"abc\"}",
            "",
            "event: posts/*",
            "data: line1",
            "data:line2",
            "",
            "",
            "data",
            "");

        Assert.Equal(3, messages.Count);

        Assert.Equal("abc", messages[0].Id);
        Assert.Equal("PB_CONNECT", messages[0].Event);
        Assert.Equal("abc", messages[0].JsonData()["clientId"]!.GetValue<string>());

        Assert.Equal("posts/*", messages[1].Event);
        Assert.Equal("line1\nline2", messages[1].Data);
        Assert.Equal("abc", messages[1].Id); // the last event id persists

        Assert.Equal("message", messages[2].Event); // default event name
        Assert.Equal("", messages[2].Data);
    }

    [Fact]
    public void JsonData_NormalizesLikeTheDartSdk()
    {
        Assert.Empty(new SseMessage().JsonData());
        Assert.Equal(123, new SseMessage { Data = "123" }.JsonData()["data"]!.GetValue<int>());
        Assert.Equal("not json", new SseMessage { Data = "not json" }.JsonData()["data"]!.GetValue<string>());
    }
}

public class RealtimeServiceTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static (PocketBaseClient Client, SseFakeHandler Handler) CreateClient()
    {
        var handler = new SseFakeHandler();
        var client = PocketBaseClient.Create(new PocketBaseOptions { BaseUrl = "http://test.local" }, new HttpClient(handler));
        var realtime = (RealtimeService)client.Realtime;
        realtime.ReconnectDelay = _ => TimeSpan.FromMilliseconds(10);
        return (client, handler);
    }

    private static string[] Subscriptions(string body) =>
        JsonDocument.Parse(body).RootElement.GetProperty("subscriptions").EnumerateArray().Select(e => e.GetString()!).ToArray();

    private static string PostedClientId(string body) => JsonDocument.Parse(body).RootElement.GetProperty("clientId").GetString()!;

    /// <summary>Subscribes and completes the connect handshake (PB_CONNECT + subscriptions POST).</summary>
    private static async Task<(Task<UnsubscribeFunc> Subscribe, SseTestStream Stream, string Body)> ConnectAsync(
        SseFakeHandler handler, Func<Task<UnsubscribeFunc>> subscribe, string clientId = "client1")
    {
        var task = subscribe();
        var stream = await handler.NextConnectionAsync();
        stream.Send("PB_CONNECT", "{\"clientId\":\"" + clientId + "\"}", clientId);
        var (_, body) = await handler.NextSubscriptionsPostAsync();
        await task.WaitAsync(Wait);
        return (task, stream, body);
    }

    [Fact]
    public async Task Subscribe_ConnectsSubmitsAndDispatchesRecordEvents()
    {
        var (client, handler) = CreateClient();
        var received = new TaskCompletionSource<RecordSubscriptionEvent<RecordModel>>(TaskCreationOptions.RunContinuationsAsynchronously);

        var (_, stream, body) = await ConnectAsync(handler, () => client.Collection("posts").SubscribeAsync("*", e => received.TrySetResult(e)));

        Assert.Equal("client1", PostedClientId(body));
        Assert.Equal(["posts/*"], Subscriptions(body));
        Assert.True(client.Realtime.IsConnected);
        Assert.Equal("client1", client.Realtime.ClientId);

        var get = handler.Requests.First(r => r.Request.Method == HttpMethod.Get).Request;
        Assert.Equal("http://test.local/api/realtime", get.RequestUri!.ToString());
        Assert.Contains("text/event-stream", get.Headers.Accept.ToString());

        stream.Send("posts/*", "{\"action\":\"create\",\"record\":{\"id\":\"r1\",\"title\":\"hello\"}}");

        var e = await received.Task.WaitAsync(Wait);
        Assert.Equal("create", e.Action);
        Assert.Equal("r1", e.Record!.Id);
        Assert.Equal("hello", e.Record.Get<string>("title"));
    }

    private sealed class Post
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
    }

    [Fact]
    public async Task Subscribe_Generic_AsyncCallback()
    {
        var (client, handler) = CreateClient();
        var received = new TaskCompletionSource<RecordSubscriptionEvent<Post>>(TaskCreationOptions.RunContinuationsAsynchronously);

        var (_, stream, _) = await ConnectAsync(handler, () => client.Collection("posts").SubscribeAsync<Post>("r1", async e =>
        {
            await Task.Yield();
            received.TrySetResult(e);
        }));

        stream.Send("posts/r1", "{\"action\":\"update\",\"record\":{\"id\":\"r1\",\"title\":\"x\"}}");

        var e = await received.Task.WaitAsync(Wait);
        Assert.Equal("update", e.Action);
        Assert.Equal("x", e.Record!.Title);
    }

    [Fact]
    public async Task Subscribe_WithOptions_SerializesThemInTheTopic()
    {
        var (client, handler) = CreateClient();

        var (_, _, body) = await ConnectAsync(handler, () => client.Collection("posts").SubscribeAsync("*", _ => { }, new RealtimeSubscribeOptions
        {
            Expand = "author",
            Query = { ["expand"] = "ignored", ["abc"] = 123 },
            Headers = { ["x-token"] = "t" },
        }));

        var topic = Assert.Single(Subscriptions(body));
        Assert.StartsWith("posts/*?options=", topic);

        var options = JsonDocument.Parse(Uri.UnescapeDataString(topic["posts/*?options=".Length..])).RootElement;
        Assert.Equal("author", options.GetProperty("query").GetProperty("expand").GetString());
        Assert.Equal(123, options.GetProperty("query").GetProperty("abc").GetInt32());
        Assert.Equal("t", options.GetProperty("headers").GetProperty("x-token").GetString());
    }

    [Fact]
    public async Task SubscriptionsPost_IsAuthorized()
    {
        var (client, handler) = CreateClient();
        var token = Jwt.WithExp(DateTimeOffset.UtcNow.AddHours(1));
        client.AuthStore.Save(token);

        await ConnectAsync(handler, () => client.Collection("posts").SubscribeAsync("*", _ => { }));

        var post = handler.Requests.First(r => r.Request.Method == HttpMethod.Post).Request;
        Assert.Equal(token, post.Headers.GetValues("Authorization").Single());
    }

    [Fact]
    public async Task SecondTopic_IsSubmitted_AndSameTopicListener_IsNot()
    {
        var (client, handler) = CreateClient();
        var posts = client.Collection("posts");

        await ConnectAsync(handler, () => posts.SubscribeAsync("*", _ => { }));

        await posts.SubscribeAsync("*", _ => { }).WaitAsync(Wait); // same topic: no new submit
        Assert.False(handler.HasPendingSubscriptionsPost);

        var second = posts.SubscribeAsync("r1", _ => { });
        var (_, body) = await handler.NextSubscriptionsPostAsync();
        await second.WaitAsync(Wait);
        Assert.Equal(["posts/*", "posts/r1"], Subscriptions(body).Order().ToArray());
    }

    [Fact]
    public async Task UnsubscribeFunc_RemovesOnlyItsListener_AndLastOneClosesTheConnection()
    {
        var (client, handler) = CreateClient();
        var posts = client.Collection("posts");
        var disconnects = new List<IReadOnlyList<string>>();
        client.Realtime.OnDisconnect = subs => disconnects.Add(subs);

        var calls = 0;
        var (first, stream, _) = await ConnectAsync(handler, () => posts.SubscribeAsync("*", _ => Interlocked.Increment(ref calls)));
        var unsubscribe2 = await posts.SubscribeAsync("*", _ => Interlocked.Increment(ref calls));

        await unsubscribe2();
        Assert.True(client.Realtime.IsConnected); // still one listener, unchanged topics -> no submit
        Assert.False(handler.HasPendingSubscriptionsPost);

        var unsubscribe1 = await first;
        await unsubscribe1();

        await stream.Disposed.Task.WaitAsync(Wait); // the SSE response was closed
        Assert.False(client.Realtime.IsConnected);
        Assert.Equal("", client.Realtime.ClientId);
        Assert.Single(disconnects);
        Assert.Empty(disconnects[0]); // unsubscribed -> no active subscriptions
    }

    [Fact]
    public async Task CollectionUnsubscribe_OnlyAffectsThatCollection()
    {
        var (client, handler) = CreateClient();

        await ConnectAsync(handler, () => client.Collection("posts").SubscribeAsync("*", _ => { }));

        var other = client.Collection("posts2").SubscribeAsync("*", _ => { });
        await handler.NextSubscriptionsPostAsync();
        await other.WaitAsync(Wait);

        await client.Collection("posts").UnsubscribeAsync();

        var (_, body) = await handler.NextSubscriptionsPostAsync();
        Assert.Equal(["posts2/*"], Subscriptions(body));
    }

    [Fact]
    public async Task Unsubscribe_ByTopicAndListener_UsesTheUserDelegate()
    {
        var (client, handler) = CreateClient();
        Action<SseMessage> callback = _ => { };

        var (_, stream, _) = await ConnectAsync(handler, () => client.Realtime.SubscribeAsync("custom", callback));

        await client.Realtime.UnsubscribeByTopicAndListenerAsync("custom", callback);

        await stream.Disposed.Task.WaitAsync(Wait);
        Assert.False(client.Realtime.IsConnected);
    }

    [Fact]
    public async Task InterruptedConnection_Reconnects_AndResubmits()
    {
        var (client, handler) = CreateClient();
        var disconnected = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Realtime.OnDisconnect = subs => disconnected.TrySetResult(subs);

        var connects = 0;
        var connectSubscription = client.Realtime.SubscribeAsync("PB_CONNECT", _ => Interlocked.Increment(ref connects));
        var (_, stream, _) = await ConnectAsync(handler, () => client.Collection("posts").SubscribeAsync("*", _ => { }));
        await connectSubscription.WaitAsync(Wait);
        await WaitUntil(() => Volatile.Read(ref connects) == 1);

        stream.End(); // server closed the connection

        Assert.Equal(["PB_CONNECT", "posts/*"], (await disconnected.Task.WaitAsync(Wait)).Order().ToArray());

        var stream2 = await handler.NextConnectionAsync();
        stream2.Send("PB_CONNECT", "{\"clientId\":\"client2\"}", "client2");
        var (_, body) = await handler.NextSubscriptionsPostAsync();

        Assert.Equal("client2", PostedClientId(body));
        Assert.Equal(["PB_CONNECT", "posts/*"], Subscriptions(body).Order().ToArray());

        await WaitUntil(() => client.Realtime.IsConnected && Volatile.Read(ref connects) == 2);
        Assert.Equal("client2", client.Realtime.ClientId);
    }

    [Fact]
    public async Task PbConnectListener_IsCalledOnConnect()
    {
        var (client, handler) = CreateClient();
        var connected = new TaskCompletionSource<SseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        await ConnectAsync(handler, () => client.Realtime.SubscribeAsync("PB_CONNECT", e => connected.TrySetResult(e)));

        var e = await connected.Task.WaitAsync(Wait);
        Assert.Equal("client1", e.Id);
    }

    [Fact]
    public async Task FailedFirstConnection_Throws()
    {
        var (client, handler) = CreateClient();
        handler.ConnectStatus = HttpStatusCode.InternalServerError;

        var ex = await Assert.ThrowsAsync<PocketBaseException>(() => client.Collection("posts").SubscribeAsync("*", _ => { }).WaitAsync(Wait));
        Assert.Equal(500, ex.Status);
        Assert.False(client.Realtime.IsConnected);
    }

    [Fact]
    public async Task FailedSubscriptionsSubmit_Throws()
    {
        var (client, handler) = CreateClient();
        handler.SubscriptionsStatus = HttpStatusCode.Forbidden;

        var task = client.Collection("posts").SubscribeAsync("*", _ => { });
        var stream = await handler.NextConnectionAsync();
        stream.Send("PB_CONNECT", "{}", "client1");

        var ex = await Assert.ThrowsAsync<PocketBaseException>(() => task.WaitAsync(Wait));
        Assert.Equal(403, ex.Status);
        await stream.Disposed.Task.WaitAsync(Wait);
    }

    [Fact]
    public async Task ConnectTimeout_Throws()
    {
        var (client, handler) = CreateClient();
        ((RealtimeService)client.Realtime).MaxConnectTimeout = TimeSpan.FromMilliseconds(100);

        var task = client.Collection("posts").SubscribeAsync("*", _ => { });
        await handler.NextConnectionAsync(); // never sends PB_CONNECT

        var ex = await Assert.ThrowsAsync<PocketBaseException>(() => task.WaitAsync(Wait));
        Assert.IsType<TimeoutException>(ex.OriginalError);
    }

    [Fact]
    public async Task CallbackExceptions_DontBreakTheConnection()
    {
        var (client, handler) = CreateClient();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = true;

        var (_, stream, _) = await ConnectAsync(handler, () => client.Collection("posts").SubscribeAsync("*", _ =>
        {
            if (first)
            {
                first = false;
                throw new InvalidOperationException("boom");
            }
            received.TrySetResult();
        }));

        stream.Send("posts/*", "{\"action\":\"create\",\"record\":{\"id\":\"1\"}}");
        stream.Send("posts/*", "{\"action\":\"create\",\"record\":{\"id\":\"2\"}}");

        await received.Task.WaitAsync(Wait);
        Assert.True(client.Realtime.IsConnected);
    }

    [Fact]
    public async Task CancelledSubscribe_RemovesTheListener()
    {
        var (client, handler) = CreateClient();
        using var cts = new CancellationTokenSource();

        var task = client.Collection("posts").SubscribeAsync("*", _ => { }, cancellationToken: cts.Token);
        var stream = await handler.NextConnectionAsync();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(Wait));

        // the pending connect is resolved with no subscriptions left -> the connection is closed on PB_CONNECT
        stream.Send("PB_CONNECT", "{}", "client1");
        await stream.Disposed.Task.WaitAsync(Wait);
        Assert.False(handler.HasPendingSubscriptionsPost, string.Join("\n", handler.Requests.Select(r => r.Request.Method + " " + r.Request.RequestUri + " " + r.Body)));
    }

    [Fact]
    public async Task AuthWithOAuth2_FullRealtimeFlow()
    {
        var (client, handler) = CreateClient();
        handler.Responder = (request, body) => request.RequestUri!.AbsolutePath switch
        {
            "/api/collections/users/auth-methods" => (HttpStatusCode.OK,
                "{\"oauth2\":{\"enabled\":true,\"providers\":[{\"name\":\"google\",\"codeVerifier\":\"verifier\",\"authURL\":\"https://accounts.example.com/auth?client_id=1&state=old&redirect_uri=\"}]}}"),
            "/api/collections/users/auth-with-oauth2" => (HttpStatusCode.OK, "{\"token\":\"tok\",\"record\":{\"id\":\"u1\",\"collectionName\":\"users\"},\"meta\":{\"isNew\":true}}"),
            _ => (HttpStatusCode.NotFound, "{}"),
        };

        var urlReceived = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var authTask = client.Collection("users").AuthWithOAuth2Async("google", url =>
        {
            urlReceived.TrySetResult(url);
            return Task.CompletedTask;
        }, new OAuth2AuthOptions { Scopes = { "email", "profile" }, CreateData = new { name = "x" }, Expand = "team" });

        var stream = await handler.NextConnectionAsync();
        stream.Send("PB_CONNECT", "{}", "state123");
        var (_, subsBody) = await handler.NextSubscriptionsPostAsync();
        Assert.Equal(["@oauth2"], Subscriptions(subsBody));

        var url = await urlReceived.Task.WaitAsync(Wait);
        Assert.Equal(
            "https://accounts.example.com/auth?client_id=1&state=state123&redirect_uri=" +
            Uri.EscapeDataString("http://test.local/api/oauth2-redirect") + "&scope=email%20profile",
            url);

        stream.Send("@oauth2", "{\"state\":\"state123\",\"code\":\"thecode\"}");

        var auth = await authTask.WaitAsync(Wait);
        Assert.Equal("tok", auth.Token);
        Assert.Equal("u1", auth.Record.Id);
        Assert.Equal("tok", client.AuthStore.Token);

        var authRequest = handler.Requests.Single(r => r.Request.RequestUri!.AbsolutePath.EndsWith("/auth-with-oauth2"));
        Assert.Equal("expand=team", authRequest.Request.RequestUri!.Query.TrimStart('?'));
        var authBody = JsonDocument.Parse(authRequest.Body!).RootElement;
        Assert.Equal("google", authBody.GetProperty("provider").GetString());
        Assert.Equal("thecode", authBody.GetProperty("code").GetString());
        Assert.Equal("verifier", authBody.GetProperty("codeVerifier").GetString());
        Assert.Equal("http://test.local/api/oauth2-redirect", authBody.GetProperty("redirectURL").GetString());
        Assert.Equal("x", authBody.GetProperty("createData").GetProperty("name").GetString());

        await stream.Disposed.Task.WaitAsync(Wait); // the one-off realtime connection is closed
    }

    [Fact]
    public async Task AuthWithOAuth2_StateMismatch_Throws()
    {
        var (client, handler) = CreateClient();
        handler.Responder = (request, _) => (HttpStatusCode.OK,
            "{\"oauth2\":{\"providers\":[{\"name\":\"google\",\"authURL\":\"https://a/?x=1&redirect_uri=\"}]}}");

        var authTask = client.Collection("users").AuthWithOAuth2Async("google", _ => Task.CompletedTask);

        var stream = await handler.NextConnectionAsync();
        stream.Send("PB_CONNECT", "{}", "state123");
        await handler.NextSubscriptionsPostAsync();
        stream.Send("@oauth2", "{\"state\":\"other\",\"code\":\"c\"}");

        var ex = await Assert.ThrowsAsync<PocketBaseException>(() => authTask.WaitAsync(Wait));
        Assert.Equal("State parameters don't match.", ex.OriginalError!.Message);
    }

    [Fact]
    public async Task AuthWithOAuth2_UnknownProvider_Throws()
    {
        var (client, handler) = CreateClient();
        handler.Responder = (_, _) => (HttpStatusCode.OK, "{\"oauth2\":{\"providers\":[]}}");

        var ex = await Assert.ThrowsAsync<PocketBaseException>(() => client.Collection("users").AuthWithOAuth2Async("google", _ => Task.CompletedTask));
        Assert.Contains("google", ex.OriginalError!.Message);
    }

    [Fact]
    public void ReplaceQueryParams_PortOfTheJsSdk()
    {
        Assert.Equal("https://a/b?x=1&state=s%20t",
            RecordService.ReplaceQueryParams("https://a/b?x=1&state=old", new Dictionary<string, string?> { ["state"] = "s t" }));
        Assert.Equal("https://a/b",
            RecordService.ReplaceQueryParams("https://a/b?x=1", new Dictionary<string, string?> { ["x"] = null }));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met.");
            await Task.Delay(10);
        }
    }
}

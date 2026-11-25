using System.Net;
using System.Text;
using System.Threading.Channels;

namespace PocketBase.SDK.Tests;

/// <summary>A readable stream fed by the test (simulates a server event stream).</summary>
internal sealed class SseTestStream : Stream
{
    private readonly Channel<byte[]> _channel = Channel.CreateUnbounded<byte[]>();
    private byte[] _current = [];
    private int _offset;

    public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Write(string text) => _channel.Writer.TryWrite(Encoding.UTF8.GetBytes(text));

    public void Send(string eventName, string data, string? id = null) =>
        Write((id is null ? "" : "id:" + id + "\n") + "event:" + eventName + "\ndata:" + data + "\n\n");

    /// <summary>Ends the stream gracefully (server closed the connection).</summary>
    public void End() => _channel.Writer.TryComplete();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (_offset >= _current.Length)
        {
            if (!await _channel.Reader.WaitToReadAsync(cancellationToken)) return 0;
            if (_channel.Reader.TryRead(out var next))
            {
                _current = next;
                _offset = 0;
            }
        }

        var count = Math.Min(buffer.Length, _current.Length - _offset);
        _current.AsMemory(_offset, count).CopyTo(buffer);
        _offset += count;
        return count;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

    protected override void Dispose(bool disposing)
    {
        _channel.Writer.TryComplete();
        Disposed.TrySetResult();
        base.Dispose(disposing);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// Fake PocketBase server: <c>GET /api/realtime</c> opens a new <see cref="SseTestStream"/> (see <see cref="NextConnectionAsync"/>),
/// <c>POST /api/realtime</c> records the submitted subscriptions, any other request uses <see cref="Responder"/>.
/// </summary>
internal sealed class SseFakeHandler : HttpMessageHandler
{
    private readonly Channel<SseTestStream> _connections = Channel.CreateUnbounded<SseTestStream>();
    private readonly Channel<(HttpRequestMessage Request, string Body)> _posts = Channel.CreateUnbounded<(HttpRequestMessage, string)>();

    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = new();

    /// <summary>Status of the next GET /api/realtime (e.g. to simulate a failed connection).</summary>
    public HttpStatusCode ConnectStatus { get; set; } = HttpStatusCode.OK;

    public HttpStatusCode SubscriptionsStatus { get; set; } = HttpStatusCode.NoContent;

    public Func<HttpRequestMessage, string?, (HttpStatusCode Status, string Body)>? Responder { get; set; }

    public async Task<SseTestStream> NextConnectionAsync() =>
        await _connections.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

    public async Task<(HttpRequestMessage Request, string Body)> NextSubscriptionsPostAsync() =>
        await _posts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

    public bool HasPendingSubscriptionsPost => _posts.Reader.TryPeek(out _);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Requests) Requests.Add((request, body));

        if (request.RequestUri!.AbsolutePath == "/api/realtime")
        {
            if (request.Method == HttpMethod.Get)
            {
                if (ConnectStatus != HttpStatusCode.OK)
                {
                    return new HttpResponseMessage(ConnectStatus) { RequestMessage = request, Content = new StringContent("{\"message\":\"connect failed\"}") };
                }

                var stream = new SseTestStream();
                _connections.Writer.TryWrite(stream);
                return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new StreamContent(stream) };
            }

            _posts.Writer.TryWrite((request, body ?? ""));
            var failed = (int)SubscriptionsStatus >= 400;
            return new HttpResponseMessage(SubscriptionsStatus)
            {
                RequestMessage = request,
                Content = new StringContent(failed ? "{\"message\":\"subscriptions failed\"}" : ""),
            };
        }

        var (status, responseBody) = Responder?.Invoke(request, body) ?? (HttpStatusCode.OK, "{}");
        return new HttpResponseMessage(status) { RequestMessage = request, Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
    }
}

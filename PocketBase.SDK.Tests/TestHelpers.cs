using System.Net;
using System.Text;

namespace PocketBase.SDK.Tests;

internal static class Jwt
{
    /// <summary>Builds an unsigned test JWT with the provided JSON payload.</summary>
    public static string Create(string payloadJson) =>
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." + B64Url(payloadJson) + ".signature";

    public static string WithExp(DateTimeOffset exp, string extra = "") =>
        Create("{\"exp\":" + exp.ToUnixTimeSeconds() + extra + "}");

    private static string B64Url(string s) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Captures the outgoing requests and replies with a configurable response.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = new();

    /// <summary>Multipart parts of the last request (name, file name, string value), if it was multipart.</summary>
    public List<(string Name, string? FileName, string Value)> LastParts { get; } = new();

    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public string ResponseBody { get; set; } = "{}";
    public Exception? Throw { get; set; }

    /// <summary>Optional per-request responder (overrides <see cref="Status"/>/<see cref="ResponseBody"/>).</summary>
    public Func<HttpRequestMessage, (HttpStatusCode Status, string Body)>? Responder { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LastParts.Clear();
        if (request.Content is MultipartFormDataContent multipart)
        {
            foreach (var part in multipart)
            {
                var disposition = part.Headers.ContentDisposition!;
                LastParts.Add((disposition.Name!.Trim('"'), disposition.FileName?.Trim('"'), await part.ReadAsStringAsync(cancellationToken)));
            }
        }

        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));

        if (Throw is not null) throw Throw;

        var (status, responseBody) = Responder?.Invoke(request) ?? (Status, ResponseBody);

        return new HttpResponseMessage(status)
        {
            RequestMessage = request,
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
        };
    }

    public HttpRequestMessage Last => Requests[^1].Request;

    public string? LastBody => Requests[^1].Body;
}

using System.Net.Http.Headers;

namespace PocketBaseNet.Abstractions;

/// <summary>
/// A file to upload (equivalent of a JS <c>File</c>/<c>Blob</c> or a Dart <c>http.MultipartFile</c>).
/// Use it as a body field value (single or inside a collection) and the request is automatically
/// sent as <c>multipart/form-data</c>, e.g. <c>new { title = "x", documents = new[] { FileUpload.FromFile("a.pdf") } }</c>.
/// </summary>
public sealed class FileUpload
{
    private readonly Func<HttpContent> _factory;

    private FileUpload(string fileName, string? contentType, Func<HttpContent> factory)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("File name is required.", nameof(fileName));
        FileName = fileName;
        ContentType = contentType;
        _factory = factory;
    }

    public string FileName { get; }

    public string? ContentType { get; }

    public static FileUpload FromBytes(byte[] data, string fileName, string? contentType = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new FileUpload(fileName, contentType, () => new ByteArrayContent(data));
    }

    /// <summary>The caller keeps the ownership of <paramref name="stream"/> (it is not disposed).</summary>
    public static FileUpload FromStream(Stream stream, string fileName, string? contentType = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new FileUpload(fileName, contentType, () => new StreamContent(new NonClosingStream(stream)));
    }

    /// <summary>The file is opened when the request is sent and closed right after.</summary>
    public static FileUpload FromFile(string path, string? contentType = null, string? fileName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new FileUpload(fileName ?? Path.GetFileName(path), contentType, () => new StreamContent(File.OpenRead(path)));
    }

    /// <summary>Creates a new HttpContent for the file (called once per request).</summary>
    public HttpContent CreateContent()
    {
        var content = _factory();
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(ContentType ?? "application/octet-stream");
        return content;
    }

    private sealed class NonClosingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { /* the caller owns the inner stream */ }
    }
}

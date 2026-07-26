using System.Net;

namespace PolyAI.Tests.QaProbes.Fakes;

/// <summary>
/// Captures every outgoing request URI and body so probes can assert on the wire format.
/// </summary>
internal sealed class CapturingHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public Uri? LastUri { get; private set; }
    public string? LastBody { get; private set; }
    public System.Net.Http.Headers.HttpRequestHeaders? LastHeaders { get; private set; }

    public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    public static CapturingHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });

    public static CapturingHandler ServerSentEvents(string payload)
        => new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "text/event-stream")
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastUri = request.RequestUri;
        LastHeaders = request.Headers;
        if (request.Content is not null)
            LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
        return _respond(request);
    }
}

/// <summary>
/// A stream that returns <paramref name="prefix"/> on the first read and then blocks
/// indefinitely on every subsequent SYNCHRONOUS read — modelling an open, idle SSE
/// connection that has sent a chunk and is waiting for the next one.
/// </summary>
internal sealed class IdleAfterPrefixStream : Stream
{
    private readonly byte[] _prefix;
    private readonly ManualResetEventSlim _release = new(false);
    private int _position;

    public IdleAfterPrefixStream(string prefix) => _prefix = System.Text.Encoding.UTF8.GetBytes(prefix);

    /// <summary>Unblocks any parked reader so the probe never leaks a wedged thread.</summary>
    public void Release() => _release.Set();

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position < _prefix.Length)
        {
            var toCopy = Math.Min(count, _prefix.Length - _position);
            Array.Copy(_prefix, _position, buffer, offset, toCopy);
            _position += toCopy;
            return toCopy;
        }

        // Prefix drained: the connection is open but idle. A synchronous read parks here.
        _release.Wait();
        return 0;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position < _prefix.Length)
        {
            var toCopy = Math.Min(buffer.Length, _prefix.Length - _position);
            _prefix.AsSpan(_position, toCopy).CopyTo(buffer.Span);
            _position += toCopy;
            return toCopy;
        }

        // An async read honours the token, as a real network stream does.
        await Task.Run(() => _release.Wait(cancellationToken), cancellationToken).ConfigureAwait(false);
        return 0;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _position; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        _release.Set();
        base.Dispose(disposing);
    }
}

/// <summary>HttpContent that records whether it was disposed, to detect leaked responses.</summary>
internal sealed class DisposeTrackingContent : HttpContent
{
    private readonly byte[] _payload;

    public bool Disposed { get; private set; }

    public DisposeTrackingContent(string payload, string mediaType = "text/event-stream")
    {
        _payload = System.Text.Encoding.UTF8.GetBytes(payload);
        Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
    }

    protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        => stream.WriteAsync(_payload, 0, _payload.Length);

    protected override bool TryComputeLength(out long length)
    {
        length = _payload.Length;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}

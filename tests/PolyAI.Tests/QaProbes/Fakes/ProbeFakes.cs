using System.Net;

namespace PolyAI.Tests.QaProbes.Fakes;

/// <summary>
/// Captures every outgoing request URI, header set, and body so probes can assert on the
/// wire format. The shipped <c>FakeHttpMessageHandler</c> accepts any URI silently, which is
/// why a malformed Gemini URL survived the shipped suite (GRO-283).
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

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastUri = request.RequestUri;
        LastHeaders = request.Headers;
        if (request.Content is not null)
            LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
        return _respond(request);
    }
}

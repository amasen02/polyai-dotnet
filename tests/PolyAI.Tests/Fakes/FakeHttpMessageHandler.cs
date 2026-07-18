using System.Net;

namespace PolyAI.Tests.Fakes;

/// <summary>
/// Captures outgoing <see cref="HttpRequestMessage"/>s and returns a pre-canned response.
/// Thread-safe for single-call scenarios.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public HttpRequestMessage? LastRequest { get; private set; }

    /// <summary>Body of the last captured request, read before the request is disposed.</summary>
    public string? LastRequestBody { get; private set; }

    public FakeHttpMessageHandler(HttpResponseMessage response)
        : this(_ => response) { }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    public static FakeHttpMessageHandler WithJson(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        return new FakeHttpMessageHandler(response);
    }

    public static FakeHttpMessageHandler WithSse(string sseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(sseContent, System.Text.Encoding.UTF8, "text/event-stream")
        };
        return new FakeHttpMessageHandler(response);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        return _handler(request);
    }
}

using System.Net;
using FluentAssertions;
using PolyAI.Abstractions;
using PolyAI.Errors;
using PolyAI.Providers.Anthropic;
using PolyAI.Providers.Gemini;
using PolyAI.Providers.Ollama;
using PolyAI.Providers.OpenAI;
using PolyAI.Tests.QaProbes.Fakes;

namespace PolyAI.Tests.QaProbes;

/// <summary>
/// GRO-123 QA probes — streaming cancellation, malformed streams, and response lifetime.
/// The shipped suite has ZERO cancellation tests and ZERO malformed-stream tests.
/// </summary>
public sealed class P2_StreamingProbes
{
    /// <summary>How long a correctly-cancelling stream is allowed to take to unwind.</summary>
    private static readonly TimeSpan CancelBudget = TimeSpan.FromSeconds(5);

    /// <summary>How long a correctly-terminating stream is allowed to take to drain.</summary>
    private static readonly TimeSpan DrainBudget = TimeSpan.FromSeconds(5);

    private static HttpResponseMessage SseResponseThatGoesIdle(string prefix, out IdleAfterPrefixStream stream)
    {
        stream = new IdleAfterPrefixStream(prefix);
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StreamContent(stream)
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream") }
            }
        };
    }

    /// <summary>
    /// Consumes one chunk, cancels, then asks for the next. A correct implementation observes
    /// the token and throws OperationCanceledException. Returns the thrown exception, or a
    /// TimeoutException if the enumeration wedged.
    /// </summary>
    private static async Task<Exception?> CancelAfterFirstChunkAsync(
        Func<CancellationToken, IAsyncEnumerable<string>> stream)
    {
        using var cts = new CancellationTokenSource();

        async Task Drive()
        {
            await foreach (var _ in stream(cts.Token).WithCancellation(cts.Token))
                await cts.CancelAsync();
        }

        try
        {
            await Task.Run(Drive).WaitAsync(CancelBudget);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>
    /// Drains a stream to completion under a budget. A TimeoutException means the enumeration
    /// failed to terminate, rather than wedging the whole test run with no verdict.
    /// </summary>
    private static async Task<List<string>> DrainAsync(IAsyncEnumerable<string> stream)
    {
        var chunks = new List<string>();
        await Task.Run(async () =>
        {
            await foreach (var chunk in stream) chunks.Add(chunk);
        }).WaitAsync(DrainBudget);
        return chunks;
    }

    // ---------------------------------------------------------------- P2.1 – P2.4
    // Regression: StreamReader.EndOfStream refills its buffer with a SYNCHRONOUS, non-cancellable
    // read. While the streaming loops were driven by `while (!reader.EndOfStream)`, an
    // open-but-idle SSE connection parked in the loop CONDITION, so the
    // ThrowIfCancellationRequested() in the body was never reached and cancellation never unwound.
    // The loops now drive on `await reader.ReadLineAsync(ct)` and break on null.
    [Fact]
    public async Task P2_1_OpenAI_StreamAsync_honours_cancellation_while_the_stream_is_idle()
    {
        var response = SseResponseThatGoesIdle(
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hi\"}}]}\n", out var idle);
        using var _ = idle;
        var provider = new OpenAIProvider(
            new HttpClient(new CapturingHandler(_ => response)), new OpenAIOptions { ApiKey = "k" });

        var outcome = await CancelAfterFirstChunkAsync(
            ct => provider.StreamAsync([ChatMessage.User("Hi")], null, ct));

        outcome.Should().BeAssignableTo<OperationCanceledException>(
            "cancelling mid-stream must unwind promptly; a TimeoutException here means the " +
            "enumeration is wedged on a synchronous, non-cancellable read");
    }

    [Fact]
    public async Task P2_2_Anthropic_StreamAsync_honours_cancellation_while_the_stream_is_idle()
    {
        var response = SseResponseThatGoesIdle(
            "data: {\"type\":\"content_block_delta\",\"delta\":{\"text\":\"Hi\"}}\n", out var idle);
        using var _ = idle;
        var provider = new AnthropicProvider(
            new HttpClient(new CapturingHandler(_ => response)), new AnthropicOptions { ApiKey = "k" });

        var outcome = await CancelAfterFirstChunkAsync(
            ct => provider.StreamAsync([ChatMessage.User("Hi")], null, ct));

        outcome.Should().BeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task P2_3_Gemini_StreamAsync_honours_cancellation_while_the_stream_is_idle()
    {
        var response = SseResponseThatGoesIdle(
            "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Hi\"}]}}]}\n", out var idle);
        using var _ = idle;
        var provider = new GeminiProvider(
            new HttpClient(new CapturingHandler(_ => response)), new GeminiOptions { ApiKey = "k" });

        var outcome = await CancelAfterFirstChunkAsync(
            ct => provider.StreamAsync([ChatMessage.User("Hi")], null, ct));

        outcome.Should().BeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task P2_4_Ollama_StreamAsync_honours_cancellation_while_the_stream_is_idle()
    {
        var response = SseResponseThatGoesIdle(
            "{\"message\":{\"content\":\"Hi\"},\"done\":false}\n", out var idle);
        using var _ = idle;
        var provider = new OllamaProvider(
            new HttpClient(new CapturingHandler(_ => response)), new OllamaOptions());

        var outcome = await CancelAfterFirstChunkAsync(
            ct => provider.StreamAsync([ChatMessage.User("Hi")], null, ct));

        outcome.Should().BeAssignableTo<OperationCanceledException>();
    }

    // ---------------------------------------------------------------- P2.5
    // Azure OpenAI is the 5th provider; it delegates to OpenAIProvider, so it inherits the
    // same streaming path. Asserted explicitly because the QA scope names all five.
    [Fact]
    public async Task P2_5_AzureOpenAI_StreamAsync_honours_cancellation_while_the_stream_is_idle()
    {
        var response = SseResponseThatGoesIdle(
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hi\"}}]}\n", out var idle);
        using var _ = idle;
        var provider = new PolyAI.Providers.Azure.AzureOpenAIProvider(
            new HttpClient(new CapturingHandler(_ => response)),
            new PolyAI.Providers.Azure.AzureOpenAIOptions
            {
                ApiKey = "k",
                Endpoint = "https://unit-test.openai.azure.com",
                DeploymentName = "gpt-4o",
            });

        var outcome = await CancelAfterFirstChunkAsync(
            ct => provider.StreamAsync([ChatMessage.User("Hi")], null, ct));

        outcome.Should().BeAssignableTo<OperationCanceledException>();
    }

    // ---------------------------------------------------------------- P2.6
    // A token already cancelled before the call must never reach the network.
    [Fact]
    public async Task P2_6_A_pre_cancelled_token_prevents_the_stream_request_entirely()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("data: [DONE]\n\n", System.Text.Encoding.UTF8, "text/event-stream")
        });
        var provider = new OpenAIProvider(new HttpClient(handler), new OpenAIOptions { ApiKey = "k" });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () =>
        {
            await foreach (var _ in provider.StreamAsync([ChatMessage.User("Hi")], null, cts.Token)) { }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---------------------------------------------------------------- P2.7
    // Malformed SSE — QA scope. Expected to PASS: ParseStreamChunk swallows the JsonException
    // and the bad line is skipped. Recorded as verified-good, not assumed.
    [Fact]
    public async Task P2_7_Malformed_SSE_lines_are_skipped_without_terminating_the_stream()
    {
        const string sse =
            "data: {\"choices\":[{\"delta\":{\"content\":\"A\"}}]}\n" +
            "data: {this is not json\n" +
            ": heartbeat comment\n" +
            "event: ping\n" +
            "\n" +
            "data: {\"choices\":[{\"delta\":{}}]}\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"B\"}}]}\n" +
            "data: [DONE]\n";

        var provider = new OpenAIProvider(
            new HttpClient(new CapturingHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(sse, System.Text.Encoding.UTF8, "text/event-stream")
            })),
            new OpenAIOptions { ApiKey = "k" });

        var chunks = new List<string>();
        await foreach (var c in provider.StreamAsync([ChatMessage.User("Hi")])) chunks.Add(c);

        chunks.Should().Equal("A", "B");
    }

    // ---------------------------------------------------------------- P2.8
    // Malformed NDJSON — QA scope. Ollama's inline loop. Expected to PASS.
    [Fact]
    public async Task P2_8_Malformed_NDJSON_lines_are_skipped_without_terminating_the_stream()
    {
        const string ndjson =
            "{\"message\":{\"content\":\"A\"},\"done\":false}\n" +
            "{ truncated\n" +
            "\n" +
            "{\"message\":{\"content\":\"B\"},\"done\":false}\n" +
            "{\"message\":{\"content\":\"\"},\"done\":true}\n";

        var provider = new OllamaProvider(
            new HttpClient(new CapturingHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(ndjson, System.Text.Encoding.UTF8, "application/x-ndjson")
            })),
            new OllamaOptions());

        var chunks = new List<string>();
        await foreach (var c in provider.StreamAsync([ChatMessage.User("Hi")])) chunks.Add(c);

        chunks.Should().Equal("A", "B", "");
    }

    // ---------------------------------------------------------------- P2.11 – P2.12
    // A stream that simply ENDS, with no terminator, must still terminate the enumeration.
    // Neither P2.7 nor P2.8 covers this: both finish with an explicit sentinel ([DONE] / done:true),
    // so a loop that fails to break on a null line passes them both while spinning forever on a
    // truncated or aborted connection. These pin the null-line break itself.

    [Fact]
    public async Task P2_11_An_SSE_stream_that_ends_without_DONE_terminates_the_enumeration()
    {
        const string sse =
            "data: {\"choices\":[{\"delta\":{\"content\":\"A\"}}]}\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"B\"}}]}\n";

        var provider = new OpenAIProvider(
            new HttpClient(new CapturingHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(sse, System.Text.Encoding.UTF8, "text/event-stream")
            })),
            new OpenAIOptions { ApiKey = "k" });

        var chunks = await DrainAsync(provider.StreamAsync([ChatMessage.User("Hi")]));

        chunks.Should().Equal(new[] { "A", "B" },
            "the loop must break when ReadLineAsync returns null; a TimeoutException here means " +
            "it is spinning on an already-drained stream");
    }

    [Fact]
    public async Task P2_12_An_NDJSON_stream_that_ends_without_done_true_terminates_the_enumeration()
    {
        const string ndjson =
            "{\"message\":{\"content\":\"A\"},\"done\":false}\n" +
            "{\"message\":{\"content\":\"B\"},\"done\":false}\n";

        var provider = new OllamaProvider(
            new HttpClient(new CapturingHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(ndjson, System.Text.Encoding.UTF8, "application/x-ndjson")
            })),
            new OllamaOptions());

        var chunks = await DrainAsync(provider.StreamAsync([ChatMessage.User("Hi")]));

        chunks.Should().Equal(new[] { "A", "B" },
            "the null line must be tested BEFORE the whitespace skip: string.IsNullOrWhiteSpace(null) " +
            "is true, so a `continue` on null would swallow the end-of-stream signal and spin forever");
    }

    // ---------------------------------------------------------------- P2.9
    // On an error path the iterator still owns and disposes the response before propagating it.
    [Fact]
    public async Task P2_9_A_failed_stream_request_disposes_its_response()
    {
        var content = new DisposeTrackingContent("{\"error\":\"boom\"}", "application/json");
        var provider = new OpenAIProvider(
            new HttpClient(new CapturingHandler(_ =>
                new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError) { Content = content })),
            new OpenAIOptions { ApiKey = "k" });

        var act = async () =>
        {
            await foreach (var _ in provider.StreamAsync([ChatMessage.User("Hi")])) { }
        };
        await act.Should().ThrowAsync<ProviderException>();

        content.Disposed.Should().BeTrue(
            "the streaming iterator owns the response when EnsureSuccessAsync throws");
    }

    // ---------------------------------------------------------------- P2.10
    // Early consumer exit must release the response used by the streaming iterator.
    [Fact]
    public async Task P2_10_Breaking_out_of_a_stream_early_disposes_the_response()
    {
        var content = new DisposeTrackingContent(
            "data: {\"choices\":[{\"delta\":{\"content\":\"A\"}}]}\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"B\"}}]}\n" +
            "data: [DONE]\n");
        var provider = new OpenAIProvider(
            new HttpClient(new CapturingHandler(_ =>
                new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content })),
            new OpenAIOptions { ApiKey = "k" });

        await foreach (var _ in provider.StreamAsync([ChatMessage.User("Hi")]))
            break;

        content.Disposed.Should().BeTrue();
    }

    private static readonly string[] Providers = ["openai", "anthropic", "gemini", "ollama", "azure-openai"];

    [Theory]
    [MemberData(nameof(ProviderNames))]
    public async Task Streaming_completion_disposes_request_and_response_owners_and_keeps_client_usable(string providerName)
    {
        var handler = new OwnershipHandler(() => SuccessResponse(providerName, includeDone: true));
        using var client = new HttpClient(handler);
        var provider = CreateProvider(providerName, client);

        await foreach (var _ in provider.StreamAsync([ChatMessage.User("Hi")])) { }

        await AssertOwnershipAndClientUsable(handler, client);
    }

    [Theory]
    [MemberData(nameof(ProviderNames))]
    public async Task Streaming_http_error_disposes_request_and_response_owners_and_keeps_client_usable(string providerName)
    {
        var responseContent = new TrackedResponseContent("{\"error\":\"boom\"}", "application/json");
        var handler = new OwnershipHandler(() => new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
        {
            Content = responseContent
        });
        using var client = new HttpClient(handler);
        var provider = CreateProvider(providerName, client);

        var act = async () =>
        {
            await foreach (var _ in provider.StreamAsync([ChatMessage.User("Hi")])) { }
        };
        await act.Should().ThrowAsync<ProviderException>();

        await AssertOwnershipAndClientUsable(handler, client);
        responseContent.Disposed.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(ProviderNames))]
    public async Task Streaming_cancellation_disposes_request_and_response_owners_and_keeps_client_usable(string providerName)
    {
        var idle = new IdleAfterPrefixStream(StreamPrefix(providerName));
        var responseContent = new StreamTrackingContent(idle);
        var handler = new OwnershipHandler(() => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = responseContent
        });
        using var client = new HttpClient(handler);
        var provider = CreateProvider(providerName, client);
        using var cts = new CancellationTokenSource();
        var enumerator = provider.StreamAsync([ChatMessage.User("Hi")], null, cts.Token)
            .GetAsyncEnumerator(cts.Token);

        (await enumerator.MoveNextAsync()).Should().BeTrue();
        await cts.CancelAsync();
        var act = async () => await enumerator.MoveNextAsync().AsTask().WaitAsync(CancelBudget);
        await act.Should().ThrowAsync<OperationCanceledException>();
        await enumerator.DisposeAsync();

        await AssertOwnershipAndClientUsable(handler, client);
        responseContent.Disposed.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(ProviderNames))]
    public async Task Streaming_early_break_disposes_request_and_response_owners_and_keeps_client_usable(string providerName)
    {
        var responseContent = new TrackedResponseContent(StreamPayload(providerName, includeDone: true));
        var handler = new OwnershipHandler(() => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = responseContent
        });
        using var client = new HttpClient(handler);
        var provider = CreateProvider(providerName, client);

        await foreach (var _ in provider.StreamAsync([ChatMessage.User("Hi")]))
            break;

        await AssertOwnershipAndClientUsable(handler, client);
        responseContent.Disposed.Should().BeTrue();
    }

    public static IEnumerable<object[]> ProviderNames => Providers.Select(name => new object[] { name });

    private static IPolyAIClient CreateProvider(string providerName, HttpClient client) => providerName switch
    {
        "openai" => new OpenAIProvider(client, new OpenAIOptions { ApiKey = "k" }),
        "anthropic" => new AnthropicProvider(client, new AnthropicOptions { ApiKey = "k" }),
        "gemini" => new GeminiProvider(client, new GeminiOptions { ApiKey = "k" }),
        "ollama" => new OllamaProvider(client, new OllamaOptions()),
        "azure-openai" => new PolyAI.Providers.Azure.AzureOpenAIProvider(client,
            new PolyAI.Providers.Azure.AzureOpenAIOptions
            {
                ApiKey = "k", Endpoint = "https://unit-test.openai.azure.com", DeploymentName = "gpt-4o"
            }),
        _ => throw new ArgumentOutOfRangeException(nameof(providerName))
    };

    private static HttpResponseMessage SuccessResponse(string providerName, bool includeDone)
        => new(HttpStatusCode.OK) { Content = new TrackedResponseContent(StreamPayload(providerName, includeDone)) };

    private static string StreamPrefix(string providerName) => providerName == "ollama"
        ? "{\"message\":{\"content\":\"Hi\"},\"done\":false}\n"
        : providerName == "anthropic"
            ? "data: {\"type\":\"content_block_delta\",\"delta\":{\"text\":\"Hi\"}}\n"
            : providerName == "gemini"
                ? "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Hi\"}]}}]}\n"
                : "data: {\"choices\":[{\"delta\":{\"content\":\"Hi\"}}]}\n";

    private static string StreamPayload(string providerName, bool includeDone)
    {
        var prefix = StreamPrefix(providerName);
        if (providerName == "ollama")
            return prefix + (includeDone ? "{\"message\":{\"content\":\"\"},\"done\":true}\n" : string.Empty);

        var second = providerName == "anthropic"
            ? "data: {\"type\":\"content_block_delta\",\"delta\":{\"text\":\"!\"}}\n"
            : providerName == "gemini"
                ? "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"!\"}]}}]}\n"
                : "data: {\"choices\":[{\"delta\":{\"content\":\"!\"}}]}\n";
        return prefix + second + (includeDone ? "data: [DONE]\n" : string.Empty);
    }

    private static async Task AssertOwnershipAndClientUsable(OwnershipHandler handler, HttpClient client)
    {
        handler.RequestContent.Should().NotBeNull();
        handler.RequestContent!.Disposed.Should().BeTrue("the iterator owns and disposes its request content");
        handler.ResponseContent.Should().NotBeNull();
        handler.ResponseContent!.Disposed.Should().BeTrue("the iterator owns and disposes its response content");

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://unit.test/health");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "disposing a stream must not dispose the injected HttpClient");
    }

    private sealed class OwnershipHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _response;
        public OwnershipTrackingContent? RequestContent { get; private set; }
        public ITrackedContent? ResponseContent { get; private set; }
        private int _callCount;

        public OwnershipHandler(Func<HttpResponseMessage> response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                RequestContent = new OwnershipTrackingContent(request.Content);
                request.Content = RequestContent;
            }

            var response = Interlocked.Increment(ref _callCount) == 1
                ? _response()
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
            if (ResponseContent is null)
                ResponseContent = response.Content as ITrackedContent;
            return Task.FromResult(response);
        }
    }

    private interface ITrackedContent
    {
        bool Disposed { get; }
    }

    private sealed class OwnershipTrackingContent : HttpContent, ITrackedContent
    {
        private readonly HttpContent _inner;
        public bool Disposed { get; private set; }

        public OwnershipTrackingContent(HttpContent inner) => _inner = inner;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => _inner.CopyToAsync(stream, context, CancellationToken.None);

        protected override bool TryComputeLength(out long length)
        {
            length = _inner.Headers.ContentLength ?? -1;
            return length >= 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disposed = true;
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private sealed class StreamTrackingContent : HttpContent, ITrackedContent
    {
        private readonly Stream _stream;
        public bool Disposed { get; private set; }

        public StreamTrackingContent(Stream stream)
        {
            _stream = stream;
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => _stream.CopyToAsync(stream);

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(_stream);

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disposed = true;
                _stream.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private sealed class TrackedResponseContent : HttpContent, ITrackedContent
    {
        private readonly byte[] _payload;
        public bool Disposed { get; private set; }

        public TrackedResponseContent(string payload, string mediaType = "text/event-stream")
        {
            _payload = System.Text.Encoding.UTF8.GetBytes(payload);
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(_payload).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = _payload.Length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposed = true;
            base.Dispose(disposing);
        }
    }
}

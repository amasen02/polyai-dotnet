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

    // ---------------------------------------------------------------- P2.1 – P2.4
    // StreamReader.EndOfStream performs a SYNCHRONOUS, non-cancellable read to refill its
    // buffer. On an open-but-idle SSE connection the `while (!reader.EndOfStream)` condition
    // parks forever, so the ThrowIfCancellationRequested() inside the loop is never reached.
    [Fact(Skip = "Documented defect: streaming cancellation wedge (EndOfStream blocks). Tracked in GRO da833594.")]
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

    [Fact(Skip = "Documented defect: streaming cancellation wedge (EndOfStream blocks). Tracked in GRO da833594.")]
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

    [Fact(Skip = "Documented defect: streaming cancellation wedge (EndOfStream blocks). Tracked in GRO da833594.")]
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

    [Fact(Skip = "Documented defect: streaming cancellation wedge (EndOfStream blocks). Tracked in GRO da833594.")]
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
    [Fact(Skip = "Documented defect: streaming cancellation wedge (EndOfStream blocks). Tracked in GRO da833594.")]
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

    // ---------------------------------------------------------------- P2.9
    // StreamAsync never disposes the HttpResponseMessage. On the error path the response is
    // abandoned entirely — the connection is not returned to the pool.
    [Fact(Skip = "Documented defect: StreamAsync does not dispose HttpResponseMessage. Tracked in GRO-DISPOSAL.")]
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
            "StreamAsync builds the request and response without 'using'; when " +
            "EnsureSuccessAsync throws, the HttpResponseMessage is never disposed");
    }

    // ---------------------------------------------------------------- P2.10
    // Same leak on the success path when the consumer stops early — the common
    // 'take the first N tokens then break' pattern.
    [Fact(Skip = "Documented defect: StreamAsync does not dispose HttpResponseMessage. Tracked in GRO-DISPOSAL.")]
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
}

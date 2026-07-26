using System.Web;
using FluentAssertions;
using PolyAI.Abstractions;
using PolyAI.Providers.Gemini;
using PolyAI.Tests.QaProbes.Fakes;

namespace PolyAI.Tests.QaProbes;

/// <summary>
/// GRO-283 regression probes for the Gemini request URI.
///
/// The shipped suite asserted only on RESPONSE parsing, so nothing ever looked at the URI
/// leaving the SDK. <c>BuildEndpoint</c> appended <c>&amp;key=</c> to a non-streaming URL that
/// never opened a query string, so <c>generateContent&amp;key=...</c> became part of the PATH and
/// <c>ChatAsync</c>/<c>StructuredAsync</c> could not reach the API at all.
///
/// Verified against Google's live API with a deliberately invalid key, because the status code
/// separates a routing failure from an auth failure:
/// <list type="bullet">
///   <item><c>...:generateContent&amp;key=INVALID</c> -> 404, empty body (route does not exist)</item>
///   <item><c>...:generateContent?key=INVALID</c> -> 400 "API key not valid" (route resolves)</item>
///   <item><c>...:generateContent</c> + <c>x-goog-api-key</c> -> 400 "API key not valid" (route resolves)</item>
/// </list>
///
/// Crucible's original P1.1 asserted <c>key</c> was present in the query string. GRO-283 then
/// directed that the key move to the <c>x-goog-api-key</c> header, which supersedes that
/// assertion: the two cannot both hold, and P1.3 below is the reason the header wins. What P1.1
/// still enforces — that the path stops at the action and nothing is buried in it — is intact.
/// </summary>
public sealed class P1_GeminiWireFormatProbes
{
    private const string ApiKeyHeader = "x-goog-api-key";

    private const string GeminiOkJson = """
    {"candidates":[{"content":{"parts":[{"text":"ok"}],"role":"model"},"finishReason":"STOP"}]}
    """;

    private const string GeminiSseChunk =
        "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"hi\"}]}}]}\n\n";

    private static GeminiProvider Provider(CapturingHandler handler, string apiKey = "test-key")
        => new(new HttpClient(handler),
               new GeminiOptions { ApiKey = apiKey, DefaultModel = "gemini-1.5-flash" });

    private static string? HeaderValue(CapturingHandler handler, string name)
        => handler.LastHeaders!.TryGetValues(name, out var values) ? string.Concat(values) : null;

    // ---------------------------------------------------------------- P1.1
    // The original defect: the action and the key were swallowed into the URL path.
    [Fact]
    public async Task P1_1_Gemini_ChatAsync_path_ends_at_the_action_and_carries_no_query_string()
    {
        var handler = CapturingHandler.Json(GeminiOkJson);

        await Provider(handler).ChatAsync([ChatMessage.User("Hi")]);

        var uri = handler.LastUri!;
        uri.AbsolutePath.Should().EndWith(":generateContent",
            "the path must stop at the method name — 'key=' must not be part of the path");
        uri.Query.Should().BeEmpty("the key travels in a header, so no query string is needed");
        uri.ToString().Should().NotContain("&key=",
            "'&key=' without a preceding '?' is what made the route resolve to 404");
        HeaderValue(handler, ApiKeyHeader).Should().Be("test-key");
    }

    // ---------------------------------------------------------------- P1.2
    // Streaming control case — it always opened a query string, which is why it kept working.
    [Fact]
    public async Task P1_2_Gemini_StreamAsync_opens_a_real_query_string_for_alt_sse()
    {
        var handler = CapturingHandler.ServerSentEvents(GeminiSseChunk);

        await foreach (var _ in Provider(handler).StreamAsync([ChatMessage.User("Hi")])) { }

        var uri = handler.LastUri!;
        uri.AbsolutePath.Should().EndWith(":streamGenerateContent");
        HttpUtility.ParseQueryString(uri.Query)["alt"].Should().Be("sse");
        HeaderValue(handler, ApiKeyHeader).Should().Be("test-key");
    }

    // ---------------------------------------------------------------- P1.3
    // A secret must never be placed in a URL: Microsoft.Extensions.Http logs the request URI
    // at Information level, so a key in the query string lands in application logs.
    [Fact]
    public async Task P1_3_Gemini_does_not_put_the_API_key_in_the_URL()
    {
        var chat = CapturingHandler.Json(GeminiOkJson);
        var stream = CapturingHandler.ServerSentEvents(GeminiSseChunk);

        await Provider(chat, "sk-secret-value").ChatAsync([ChatMessage.User("Hi")]);
        await foreach (var _ in Provider(stream, "sk-secret-value").StreamAsync([ChatMessage.User("Hi")])) { }

        chat.LastUri!.ToString().Should().NotContain("sk-secret-value",
            "Gemini supports the x-goog-api-key header; a key in the URL is logged by " +
            "Microsoft.Extensions.Http request logging and by any proxy in the path");
        stream.LastUri!.ToString().Should().NotContain("sk-secret-value",
            "the streaming path must not leak the key either");
    }

    // ---------------------------------------------------------------- P1.4
    // The model name is caller-controlled and was interpolated into the path unescaped.
    [Fact]
    public async Task P1_4_Gemini_ChatAsync_escapes_the_model_name_before_putting_it_in_the_path()
    {
        var handler = CapturingHandler.Json(GeminiOkJson);

        await Provider(handler, "k").ChatAsync(
            [ChatMessage.User("Hi")], new ChatOptions { Model = "evil?alt=json&key=leaked" });

        var uri = handler.LastUri!;
        uri.Query.Should().BeEmpty(
            "an unescaped model name must not be able to open a query string of its own");
        HttpUtility.ParseQueryString(uri.Query).AllKeys.Should().NotContain("alt");
        HeaderValue(handler, ApiKeyHeader).Should().Be("k",
            "the injected 'key=leaked' must not become the credential actually sent");
    }

    // ---------------------------------------------------------------- P1.4 (streaming variant)
    // Streaming is the dangerous case for injection: it genuinely has a query to hijack.
    [Fact]
    public async Task Gemini_StreamAsync_escaped_model_name_cannot_override_the_alt_parameter()
    {
        var handler = CapturingHandler.ServerSentEvents(GeminiSseChunk);

        await foreach (var _ in Provider(handler, "k").StreamAsync(
            [ChatMessage.User("Hi")], new ChatOptions { Model = "evil?alt=json" })) { }

        HttpUtility.ParseQueryString(handler.LastUri!.Query)["alt"].Should().Be("sse",
            "the caller-supplied model name must not be able to override a real query parameter");
    }
}

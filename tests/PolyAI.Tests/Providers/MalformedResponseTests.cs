using FluentAssertions;
using PolyAI.Abstractions;
using PolyAI.Errors;
using PolyAI.Providers.Anthropic;
using PolyAI.Providers.Gemini;
using PolyAI.Providers.Ollama;
using PolyAI.Providers.OpenAI;
using PolyAI.Tests.Fakes;

namespace PolyAI.Tests.Providers;

/// <summary>
/// A provider response body is untrusted input: a proxy can truncate it, a gateway can return an
/// empty 200, and a provider can change a field's shape. Every one of those must surface as a
/// <see cref="ProviderException"/> carrying the raw payload — never as a raw System.Text.Json
/// exception, which escapes the documented "every failure is a <see cref="PolyAIException"/>"
/// contract and takes the caller's process down.
/// </summary>
public sealed class MalformedResponseTests
{
    public static TheoryData<string> AllProviders() => new() { "openai", "anthropic", "gemini", "ollama" };

    private static IPolyAIClient BuildProvider(string provider, string responseBody)
    {
        var http = new HttpClient(FakeHttpMessageHandler.WithJson(responseBody));
        return provider switch
        {
            "openai" => new OpenAIProvider(http, new OpenAIOptions { ApiKey = "test-key" }),
            "anthropic" => new AnthropicProvider(http, new AnthropicOptions { ApiKey = "test-key" }),
            "gemini" => new GeminiProvider(http, new GeminiOptions { ApiKey = "test-key" }),
            "ollama" => new OllamaProvider(http, new OllamaOptions()),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "unknown provider"),
        };
    }

    private static async Task<ProviderException> CaptureChatFailure(string provider, string responseBody)
    {
        var act = async () => await BuildProvider(provider, responseBody).ChatAsync([ChatMessage.User("Hi")]);
        var thrown = await act.Should().ThrowAsync<ProviderException>();
        return thrown.Which;
    }

    [Theory]
    [MemberData(nameof(AllProviders))]
    public async Task A_truncated_body_raises_ProviderException_carrying_the_raw_payload(string provider)
    {
        const string truncated = """{"choices": [ truncated""";

        var ex = await CaptureChatFailure(provider, truncated);

        ex.Provider.Should().Be(provider, "an operator must be able to tell which provider misbehaved");
        ex.StatusCode.Should().Be(200, "the transport succeeded — it is the payload that is unreadable");
        ex.ResponseBody.Should().Be(truncated, "the raw payload is the only way to diagnose a truncating proxy");
        ex.InnerException.Should().BeAssignableTo<System.Text.Json.JsonException>(
            "the underlying parse failure is preserved as the cause, not discarded");
    }

    [Theory]
    [MemberData(nameof(AllProviders))]
    public async Task An_empty_body_raises_ProviderException_that_says_so(string provider)
    {
        var ex = await CaptureChatFailure(provider, string.Empty);

        ex.Message.Should().Contain("empty",
            "an empty 200 is what a truncated or proxied connection produces, and the message " +
            "must name that rather than leaking a JSON reader's byte-offset complaint");
        ex.StatusCode.Should().Be(200);
    }

    [Theory]
    [MemberData(nameof(AllProviders))]
    public async Task A_body_that_is_the_JSON_literal_null_raises_ProviderException(string provider)
    {
        var ex = await CaptureChatFailure(provider, "null");

        ex.Message.Should().Contain("null",
            "'null' parses successfully, so without an explicit check it would silently yield " +
            "an empty response instead of reporting the malformed payload");
    }

    [Fact]
    public async Task An_OpenAI_response_with_no_choices_raises_ProviderException()
    {
        var ex = await CaptureChatFailure("openai", """{"choices": [], "model": "gpt-4o-mini"}""");

        ex.ResponseBody.Should().Contain("choices",
            "indexing past the end of a shorter-than-expected array is a malformed payload too, " +
            "not an unhandled ArgumentOutOfRangeException");
    }

    [Fact]
    public async Task An_Anthropic_content_block_of_the_wrong_kind_raises_ProviderException()
    {
        var ex = await CaptureChatFailure("anthropic", """{"content":{"type":"text","text":"oops"}}""");

        ex.Message.Should().Contain("content").And.Contain("array",
            "the message must name the field and the expected shape so the caller can act on it");
    }

    [Fact]
    public async Task An_Anthropic_response_with_no_content_block_is_read_as_an_empty_message()
    {
        var http = new HttpClient(FakeHttpMessageHandler.WithJson(
            """{"model":"claude-3-5-haiku-20241022","stop_reason":"end_turn"}"""));
        var provider = new AnthropicProvider(http, new AnthropicOptions { ApiKey = "test-key" });

        var result = await provider.ChatAsync([ChatMessage.User("Hi")]);

        result.Content.Should().BeEmpty("an absent block is an empty message, not a malformed payload");
        result.FinishReason.Should().Be("end_turn");
    }

    [Fact]
    public async Task A_Gemini_parts_list_of_the_wrong_kind_raises_ProviderException()
    {
        var ex = await CaptureChatFailure("gemini",
            """{"candidates":[{"content":{"parts":{"text":"oops"},"role":"model"}}]}""");

        ex.Message.Should().Contain("parts").And.Contain("array");
    }

    [Fact]
    public async Task A_Gemini_candidate_stopped_by_a_safety_filter_is_read_as_an_empty_message()
    {
        var http = new HttpClient(FakeHttpMessageHandler.WithJson(
            """{"candidates":[{"finishReason":"SAFETY","index":0}]}"""));
        var provider = new GeminiProvider(http, new GeminiOptions { ApiKey = "test-key" });

        var result = await provider.ChatAsync([ChatMessage.User("Hi")]);

        result.Content.Should().BeEmpty(
            "Gemini legitimately omits 'parts' on a blocked candidate — that is a real response, " +
            "not a malformed one, and must not be turned into an exception");
        result.FinishReason.Should().Be("SAFETY");
    }
}

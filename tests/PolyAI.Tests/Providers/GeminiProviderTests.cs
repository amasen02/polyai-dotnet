using FluentAssertions;
using PolyAI.Abstractions;
using PolyAI.Errors;
using PolyAI.Providers.Gemini;
using PolyAI.Tests.Fakes;

namespace PolyAI.Tests.Providers;

public sealed class GeminiProviderTests
{
    private static GeminiProvider BuildProvider(FakeHttpMessageHandler handler, string apiKey = "test-gemini-key")
    {
        var http = new HttpClient(handler);
        return new GeminiProvider(http, new GeminiOptions { ApiKey = apiKey, DefaultModel = "gemini-1.5-flash" });
    }

    [Fact]
    public async Task ChatAsync_returns_content_from_valid_response()
    {
        const string json = """
        {
          "candidates": [{
            "content": { "parts": [{ "text": "Hello from Gemini!" }], "role": "model" },
            "finishReason": "STOP",
            "index": 0
          }],
          "usageMetadata": { "promptTokenCount": 8, "candidatesTokenCount": 4 }
        }
        """;

        var provider = BuildProvider(FakeHttpMessageHandler.WithJson(json));
        var result = await provider.ChatAsync([ChatMessage.User("Hi")]);

        result.Content.Should().Be("Hello from Gemini!");
        result.FinishReason.Should().Be("STOP");
        result.Usage!.PromptTokens.Should().Be(8);
        result.Usage.CompletionTokens.Should().Be(4);
    }

    [Fact]
    public void Constructor_throws_when_api_key_is_empty()
    {
        var act = () => new GeminiProvider(new HttpClient(), new GeminiOptions { ApiKey = "" });
        act.Should().Throw<PolyAIException>().WithMessage("*API key*");
    }

    [Fact]
    public async Task ChatAsync_returns_tool_calls_on_function_call_response()
    {
        const string json = """
        {
          "candidates": [{
            "content": {
              "parts": [{ "functionCall": { "name": "get_weather", "args": { "city": "Tokyo" } } }],
              "role": "model"
            },
            "finishReason": "STOP"
          }]
        }
        """;

        var provider = BuildProvider(FakeHttpMessageHandler.WithJson(json));
        var result = await provider.ChatAsync([ChatMessage.User("Weather in Tokyo?")]);

        result.ToolCalls.Should().HaveCount(1);
        result.ToolCalls[0].Name.Should().Be("get_weather");
    }
}

using System.Net;
using FluentAssertions;
using PolyAI.Abstractions;
using PolyAI.Errors;
using PolyAI.Providers.OpenAI;
using PolyAI.Tests.Fakes;

namespace PolyAI.Tests.Providers;

public sealed class OpenAIProviderTests
{
    private static OpenAIProvider BuildProvider(FakeHttpMessageHandler handler, string apiKey = "test-key")
    {
        var http = new HttpClient(handler);
        return new OpenAIProvider(http, new OpenAIOptions { ApiKey = apiKey, DefaultModel = "gpt-4o-mini" });
    }

    [Fact]
    public async Task ChatAsync_returns_content_from_valid_response()
    {
        const string json = """
        {
          "choices": [{ "message": { "role": "assistant", "content": "Hello, world!" }, "finish_reason": "stop" }],
          "model": "gpt-4o-mini",
          "usage": { "prompt_tokens": 10, "completion_tokens": 5 }
        }
        """;

        var provider = BuildProvider(FakeHttpMessageHandler.WithJson(json));
        var messages = new[] { ChatMessage.User("Hi") };

        var result = await provider.ChatAsync(messages);

        result.Content.Should().Be("Hello, world!");
        result.Model.Should().Be("gpt-4o-mini");
        result.Usage!.PromptTokens.Should().Be(10);
        result.Usage.CompletionTokens.Should().Be(5);
        result.Usage.TotalTokens.Should().Be(15);
        result.FinishReason.Should().Be("stop");
    }

    [Fact]
    public async Task ChatAsync_returns_tool_calls_when_model_requests_them()
    {
        const string json = """
        {
          "choices": [{
            "message": {
              "role": "assistant",
              "content": null,
              "tool_calls": [{
                "id": "call_abc",
                "type": "function",
                "function": { "name": "get_weather", "arguments": "{\"city\":\"London\"}" }
              }]
            },
            "finish_reason": "tool_calls"
          }],
          "model": "gpt-4o",
          "usage": { "prompt_tokens": 20, "completion_tokens": 15 }
        }
        """;

        var provider = BuildProvider(FakeHttpMessageHandler.WithJson(json));
        var result = await provider.ChatAsync([ChatMessage.User("Weather in London?")]);

        result.ToolCalls.Should().HaveCount(1);
        result.ToolCalls[0].Name.Should().Be("get_weather");
        result.ToolCalls[0].ArgumentsJson.Should().Contain("London");
        result.FinishReason.Should().Be("tool_calls");
    }

    [Fact]
    public async Task ChatAsync_throws_ProviderAuthException_on_401()
    {
        var handler = new FakeHttpMessageHandler(
            new System.Net.Http.HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"error\":{\"message\":\"Invalid API key\"}}", System.Text.Encoding.UTF8, "application/json")
            });

        var provider = BuildProvider(handler);

        await FluentActions
            .Awaiting(() => provider.ChatAsync([ChatMessage.User("Hi")]))
            .Should().ThrowAsync<ProviderAuthException>()
            .Where(ex => ex.StatusCode == 401 && ex.Provider == "openai");
    }

    [Fact]
    public async Task ChatAsync_throws_ProviderRateLimitException_on_429()
    {
        var handler = new FakeHttpMessageHandler(
            new System.Net.Http.HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{\"error\":{\"message\":\"Rate limit\"}}", System.Text.Encoding.UTF8, "application/json")
            });

        var provider = BuildProvider(handler);

        await FluentActions
            .Awaiting(() => provider.ChatAsync([ChatMessage.User("Hi")]))
            .Should().ThrowAsync<ProviderRateLimitException>()
            .Where(ex => ex.Provider == "openai");
    }

    [Fact]
    public async Task StreamAsync_yields_content_chunks_from_sse()
    {
        const string sse = """
        data: {"choices":[{"delta":{"content":"Hello"},"index":0}]}

        data: {"choices":[{"delta":{"content":", "},"index":0}]}

        data: {"choices":[{"delta":{"content":"world!"},"index":0}]}

        data: [DONE]

        """;

        var provider = BuildProvider(FakeHttpMessageHandler.WithSse(sse));
        var chunks = new List<string>();

        await foreach (var chunk in provider.StreamAsync([ChatMessage.User("Hi")]))
            chunks.Add(chunk);

        string.Concat(chunks).Should().Be("Hello, world!");
    }

    [Fact]
    public void Constructor_throws_when_api_key_is_empty()
    {
        var http = new HttpClient();
        var act = () => new OpenAIProvider(http, new OpenAIOptions { ApiKey = string.Empty });
        act.Should().Throw<PolyAIException>().WithMessage("*API key*");
    }

    [Fact]
    public async Task StructuredAsync_deserializes_json_response()
    {
        const string json = """
        {
          "choices": [{ "message": { "role": "assistant", "content": "{\"name\":\"Alice\",\"age\":30}" }, "finish_reason": "stop" }],
          "model": "gpt-4o-mini",
          "usage": { "prompt_tokens": 5, "completion_tokens": 10 }
        }
        """;

        var provider = BuildProvider(FakeHttpMessageHandler.WithJson(json));
        var result = await provider.StructuredAsync<PersonDto>([ChatMessage.User("Give me a person")]);

        result.Name.Should().Be("Alice");
        result.Age.Should().Be(30);
    }

    private sealed record PersonDto(string Name, int Age);
}

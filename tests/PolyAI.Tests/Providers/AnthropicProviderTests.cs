using FluentAssertions;
using PolyAI.Abstractions;
using PolyAI.Errors;
using PolyAI.Providers.Anthropic;
using PolyAI.Tests.Fakes;

namespace PolyAI.Tests.Providers;

public sealed class AnthropicProviderTests
{
    private static AnthropicProvider BuildProvider(FakeHttpMessageHandler handler, string apiKey = "sk-ant-test")
    {
        var http = new HttpClient(handler);
        return new AnthropicProvider(http, new AnthropicOptions { ApiKey = apiKey });
    }

    [Fact]
    public async Task ChatAsync_returns_content_from_valid_response()
    {
        const string json = """
        {
          "id": "msg_001",
          "type": "message",
          "role": "assistant",
          "content": [{ "type": "text", "text": "Hello from Claude!" }],
          "model": "claude-3-5-haiku-20241022",
          "stop_reason": "end_turn",
          "usage": { "input_tokens": 12, "output_tokens": 4 }
        }
        """;

        var provider = BuildProvider(FakeHttpMessageHandler.WithJson(json));
        var result = await provider.ChatAsync([ChatMessage.User("Hi")]);

        result.Content.Should().Be("Hello from Claude!");
        result.Model.Should().Be("claude-3-5-haiku-20241022");
        result.FinishReason.Should().Be("end_turn");
        result.Usage!.PromptTokens.Should().Be(12);
        result.Usage.CompletionTokens.Should().Be(4);
    }

    [Fact]
    public async Task ChatAsync_separates_system_message_from_user_messages()
    {
        const string json = """
        {
          "id": "msg_002",
          "type": "message",
          "role": "assistant",
          "content": [{ "type": "text", "text": "ok" }],
          "model": "claude-3-5-haiku-20241022",
          "stop_reason": "end_turn",
          "usage": { "input_tokens": 5, "output_tokens": 1 }
        }
        """;

        var handler = FakeHttpMessageHandler.WithJson(json);
        var provider = BuildProvider(handler);

        await provider.ChatAsync([
            ChatMessage.System("You are a helpful assistant."),
            ChatMessage.User("Hi"),
        ]);

        handler.LastRequestBody.Should().Contain("\"system\"");
        handler.LastRequestBody.Should().NotContain("\"role\":\"system\""); // system must not appear in messages array
    }

    [Fact]
    public async Task ChatAsync_returns_tool_calls_on_tool_use_response()
    {
        const string json = """
        {
          "id": "msg_003",
          "type": "message",
          "role": "assistant",
          "content": [
            { "type": "tool_use", "id": "toolu_01", "name": "get_weather", "input": { "city": "Paris" } }
          ],
          "model": "claude-3-5-sonnet-20241022",
          "stop_reason": "tool_use",
          "usage": { "input_tokens": 40, "output_tokens": 12 }
        }
        """;

        var provider = BuildProvider(FakeHttpMessageHandler.WithJson(json));
        var result = await provider.ChatAsync([ChatMessage.User("Weather?")]);

        result.ToolCalls.Should().HaveCount(1);
        result.ToolCalls[0].Name.Should().Be("get_weather");
        result.ToolCalls[0].Id.Should().Be("toolu_01");
    }

    [Fact]
    public async Task StreamAsync_yields_text_delta_chunks()
    {
        const string sse = """
        data: {"type":"message_start","message":{"id":"msg_004","type":"message","role":"assistant","content":[],"model":"claude-3-5-haiku-20241022","stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":0}}}

        data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hel"}}

        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"lo"}}

        data: {"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":5}}

        data: {"type":"message_stop"}

        """;

        var provider = BuildProvider(FakeHttpMessageHandler.WithSse(sse));
        var chunks = new List<string>();

        await foreach (var chunk in provider.StreamAsync([ChatMessage.User("Hi")]))
            chunks.Add(chunk);

        string.Concat(chunks).Should().Be("Hello");
    }

    [Fact]
    public void Constructor_throws_when_api_key_is_empty()
    {
        var act = () => new AnthropicProvider(new HttpClient(), new AnthropicOptions { ApiKey = "" });
        act.Should().Throw<PolyAIException>().WithMessage("*API key*");
    }
}

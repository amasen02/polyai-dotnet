using System.Text.Json.Nodes;
using FluentAssertions;
using PolyAI.Abstractions;
using PolyAI.Providers.Anthropic;
using PolyAI.Providers.Gemini;
using PolyAI.Providers.OpenAI;
using PolyAI.Tests.Fakes;
using PolyAI.Tools;

namespace PolyAI.Tests.Providers;

/// <summary>
/// Proves the tool schema survives the trip to the wire on every provider that sends tools.
/// A correct <see cref="ToolRegistry"/> is worth nothing if the provider flattens it on the way
/// out, and each provider previously built the schema with its own copy of the same code.
/// </summary>
public sealed class ToolSchemaWireFormatTests
{
    private enum Unit { Celsius, Fahrenheit }

    private sealed class WireTools
    {
        [PolyAITool("Books a slot", name: "book")]
        public string Book(
            [PolyAIParam("Tags")] string[] tags,
            [PolyAIParam("Unit")] Unit unit,
            [PolyAIParam("When")] DateTime when,
            [PolyAIParam("How many")] int count) => string.Empty;
    }

    private static ChatOptions ToolOptions() =>
        new() { Tools = ToolRegistry.FromType(typeof(WireTools)) };

    /// <summary>Asserts the shared <c>{type:"object", properties, required}</c> body, wherever a provider nests it.</summary>
    private static void AssertParameterSchema(JsonNode? parameters)
    {
        parameters.Should().NotBeNull();
        parameters!["type"]!.GetValue<string>().Should().Be("object");

        var properties = parameters["properties"]!;

        var tags = properties["tags"]!;
        tags["type"]!.GetValue<string>().Should().Be("array");
        tags["items"]!["type"]!.GetValue<string>().Should().Be("string");
        tags["description"]!.GetValue<string>().Should().Be("Tags");

        var unit = properties["unit"]!;
        unit["type"]!.GetValue<string>().Should().Be("string");
        unit["enum"]!.AsArray().Select(v => v!.GetValue<string>())
            .Should().BeEquivalentTo("Celsius", "Fahrenheit");

        properties["when"]!["format"]!.GetValue<string>().Should().Be("date-time");
        properties["count"]!["type"]!.GetValue<string>().Should().Be("integer");

        parameters["required"]!.AsArray().Select(v => v!.GetValue<string>())
            .Should().BeEquivalentTo("tags", "unit", "when", "count");
    }

    [Fact]
    public async Task OpenAI_sends_the_full_parameter_schema()
    {
        const string json = """
        { "choices": [{ "message": { "role": "assistant", "content": "ok" }, "finish_reason": "stop" }] }
        """;
        var handler = FakeHttpMessageHandler.WithJson(json);
        var provider = new OpenAIProvider(
            new HttpClient(handler), new OpenAIOptions { ApiKey = "test-key", DefaultModel = "gpt-4o-mini" });

        await provider.ChatAsync([ChatMessage.User("Hi")], ToolOptions());

        var body = JsonNode.Parse(handler.LastRequestBody!)!;
        AssertParameterSchema(body["tools"]![0]!["function"]!["parameters"]);
    }

    [Fact]
    public async Task Anthropic_sends_the_full_parameter_schema()
    {
        const string json = """
        {
          "id": "msg_001", "type": "message", "role": "assistant",
          "content": [{ "type": "text", "text": "ok" }],
          "model": "claude-3-5-haiku-20241022", "stop_reason": "end_turn"
        }
        """;
        var handler = FakeHttpMessageHandler.WithJson(json);
        var provider = new AnthropicProvider(
            new HttpClient(handler), new AnthropicOptions { ApiKey = "sk-ant-test" });

        await provider.ChatAsync([ChatMessage.User("Hi")], ToolOptions());

        var body = JsonNode.Parse(handler.LastRequestBody!)!;
        AssertParameterSchema(body["tools"]![0]!["input_schema"]);
    }

    [Fact]
    public async Task Gemini_sends_the_full_parameter_schema()
    {
        const string json = """
        {
          "candidates": [{
            "content": { "parts": [{ "text": "ok" }], "role": "model" },
            "finishReason": "STOP", "index": 0
          }]
        }
        """;
        var handler = FakeHttpMessageHandler.WithJson(json);
        var provider = new GeminiProvider(
            new HttpClient(handler), new GeminiOptions { ApiKey = "test-gemini-key", DefaultModel = "gemini-1.5-flash" });

        await provider.ChatAsync([ChatMessage.User("Hi")], ToolOptions());

        var body = JsonNode.Parse(handler.LastRequestBody!)!;
        AssertParameterSchema(body["tools"]![0]!["function_declarations"]![0]!["parameters"]);
    }

    [Fact]
    public async Task No_provider_emits_a_string_format_that_Gemini_rejects()
    {
        const string json = """
        {
          "candidates": [{
            "content": { "parts": [{ "text": "ok" }], "role": "model" },
            "finishReason": "STOP", "index": 0
          }]
        }
        """;
        var handler = FakeHttpMessageHandler.WithJson(json);
        var provider = new GeminiProvider(
            new HttpClient(handler), new GeminiOptions { ApiKey = "test-gemini-key", DefaultModel = "gemini-1.5-flash" });

        await provider.ChatAsync([ChatMessage.User("Hi")], ToolOptions());

        // Gemini: "only 'enum' and 'date-time' are supported for STRING type". Any other format on a
        // string property fails the whole request, so the emitted set must stay inside that list.
        var emittedFormats = JsonNode.Parse(handler.LastRequestBody!)!["tools"]![0]!["function_declarations"]![0]!
            ["parameters"]!["properties"]!.AsObject()
            .Select(p => p.Value!["format"]?.GetValue<string>())
            .Where(f => f is not null)
            .ToArray();

        emittedFormats.Should().OnlyContain(f => f == "date-time" || f == "enum");
    }
}

using FluentAssertions;
using PolyAI.Abstractions;
using PolyAI.Providers.Ollama;
using PolyAI.Tests.Fakes;

namespace PolyAI.Tests.Providers;

public sealed class OllamaProviderTests
{
    private static OllamaProvider BuildProvider(FakeHttpMessageHandler handler)
    {
        var http = new HttpClient(handler);
        return new OllamaProvider(http, new OllamaOptions { DefaultModel = "llama3.2" });
    }

    [Fact]
    public async Task ChatAsync_returns_content_from_valid_response()
    {
        const string json = """
        {
          "model": "llama3.2",
          "message": { "role": "assistant", "content": "Hello from Ollama!" },
          "done": true,
          "prompt_eval_count": 8,
          "eval_count": 5
        }
        """;

        var provider = BuildProvider(FakeHttpMessageHandler.WithJson(json));
        var result = await provider.ChatAsync([ChatMessage.User("Hi")]);

        result.Content.Should().Be("Hello from Ollama!");
        result.Usage!.PromptTokens.Should().Be(8);
        result.Usage.CompletionTokens.Should().Be(5);
    }

    [Fact]
    public async Task StreamAsync_yields_ndjson_chunks()
    {
        // Ollama streams newline-delimited JSON, not SSE
        const string ndjson = """
        {"model":"llama3.2","message":{"role":"assistant","content":"Hi"},"done":false}
        {"model":"llama3.2","message":{"role":"assistant","content":" there"},"done":false}
        {"model":"llama3.2","message":{"role":"assistant","content":"!"},"done":true}
        """;

        // Return as plain text (Ollama doesn't use SSE)
        var handler = new FakeHttpMessageHandler(
            new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(ndjson, System.Text.Encoding.UTF8, "application/x-ndjson")
            });

        var provider = BuildProvider(handler);
        var chunks = new List<string>();

        await foreach (var chunk in provider.StreamAsync([ChatMessage.User("Hello")]))
            chunks.Add(chunk);

        string.Concat(chunks).Should().Be("Hi there!");
    }
}

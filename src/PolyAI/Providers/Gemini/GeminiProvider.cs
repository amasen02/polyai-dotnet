using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using PolyAI.Abstractions;
using PolyAI.Errors;

namespace PolyAI.Providers.Gemini;

/// <summary>Calls the Google Gemini generateContent API.</summary>
internal sealed class GeminiProvider : ProviderBase
{
    private readonly HttpClient _http;
    private readonly GeminiOptions _options;

    public override string ProviderName => "gemini";

    public GeminiProvider(HttpClient http, GeminiOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new PolyAIException("Gemini API key must not be empty. Set GeminiOptions.ApiKey.");

        _http = http;
        _options = options;
    }

    public override async Task<ChatResponse> ChatAsync(
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var model = options?.Model ?? _options.DefaultModel;
        var endpoint = BuildEndpoint(model, stream: false);
        var body = BuildRequestBody(messages, options);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), System.Text.Encoding.UTF8, "application/json")
        };

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Gemini ChatAsync").ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseChatResponse(json, model);
    }

    public override async IAsyncEnumerable<string> StreamAsync(
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var model = options?.Model ?? _options.DefaultModel;
        var endpoint = BuildEndpoint(model, stream: true);
        var body = BuildRequestBody(messages, options);

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Accept", "text/event-stream");

        var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, "Gemini StreamAsync").ConfigureAwait(false);

        await foreach (var chunk in ReadSseChunksAsync(response, ParseStreamChunk, cancellationToken).ConfigureAwait(false))
            yield return chunk;
    }

    private string BuildEndpoint(string model, bool stream)
    {
        var action = stream ? "streamGenerateContent?alt=sse" : "generateContent";
        return $"{_options.BaseUrl.TrimEnd('/')}/models/{model}:{action}&key={_options.ApiKey}";
    }

    private static Dictionary<string, object?> BuildRequestBody(IList<ChatMessage> messages, ChatOptions? options)
    {
        var contents = new List<object>();
        var systemInstruction = string.Join("\n\n", messages
            .Where(m => m.Role == ChatRole.System)
            .Select(m => m.Content));

        foreach (var msg in messages.Where(m => m.Role != ChatRole.System))
        {
            contents.Add(new
            {
                role = msg.Role == ChatRole.Assistant ? "model" : "user",
                parts = new[] { new { text = msg.Content } }
            });
        }

        var config = new Dictionary<string, object?>();
        if (options?.Temperature is { } temp) config["temperature"] = temp;
        if (options?.TopP is { } topP) config["topP"] = topP;
        if (options?.MaxTokens is { } maxTok) config["maxOutputTokens"] = maxTok;
        if (options?.StopSequences is { Count: > 0 } stop) config["stopSequences"] = stop;

        var body = new Dictionary<string, object?> { ["contents"] = contents };
        if (config.Count > 0) body["generationConfig"] = config;
        if (!string.IsNullOrEmpty(systemInstruction))
            body["systemInstruction"] = new { parts = new[] { new { text = systemInstruction } } };

        if (options?.Tools is { Count: > 0 } tools)
        {
            body["tools"] = new[]
            {
                new
                {
                    function_declarations = tools.Select(t => new
                    {
                        name = t.Name,
                        description = t.Description,
                        parameters = new
                        {
                            type = "object",
                            properties = t.Parameters.ToDictionary(
                                p => p.Name,
                                p => (object)new { type = p.JsonSchemaType, description = p.Description }),
                            required = t.Parameters.Where(p => p.Required).Select(p => p.Name).ToArray()
                        }
                    }).ToArray()
                }
            };
        }

        return body;
    }

    private static ChatResponse ParseChatResponse(string json, string model)
    {
        var root = JsonNode.Parse(json);
        var candidate = root?["candidates"]?[0];
        var parts = candidate?["content"]?["parts"]?.AsArray();

        var content = string.Concat(parts?
            .Where(p => p?["text"] is not null)
            .Select(p => p!["text"]!.GetValue<string>()) ?? []);

        var finishReason = candidate?["finishReason"]?.GetValue<string>();

        int? promptTokens = root?["usageMetadata"]?["promptTokenCount"]?.GetValue<int>();
        int? completionTokens = root?["usageMetadata"]?["candidatesTokenCount"]?.GetValue<int>();
        TokenUsage? usage = promptTokens.HasValue && completionTokens.HasValue
            ? new TokenUsage(promptTokens.Value, completionTokens.Value)
            : null;

        // Tool calls (function calls in Gemini terminology)
        var toolCalls = new List<ToolCall>();
        if (parts is not null)
        {
            foreach (var part in parts)
            {
                if (part?["functionCall"] is not JsonObject fc) continue;
                var name = fc["name"]?.GetValue<string>() ?? string.Empty;
                var args = fc["args"]?.ToJsonString() ?? "{}";
                toolCalls.Add(new ToolCall(Guid.NewGuid().ToString(), name, args));
            }
        }

        return new ChatResponse(content, toolCalls, usage, model, finishReason);
    }

    private static string? ParseStreamChunk(string data)
    {
        try
        {
            var node = JsonNode.Parse(data);
            return node?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }
}

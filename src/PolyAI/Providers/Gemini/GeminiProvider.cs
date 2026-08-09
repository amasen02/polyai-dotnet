using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using PolyAI.Abstractions;
using PolyAI.Errors;

namespace PolyAI.Providers.Gemini;

/// <summary>Calls the Google Gemini generateContent API.</summary>
internal sealed class GeminiProvider : ProviderBase
{
    /// <summary>
    /// Gemini accepts the API key in this header. The key is never placed in the URL, because
    /// request URIs are logged by Microsoft.Extensions.Http and by any proxy on the path.
    /// </summary>
    private const string ApiKeyHeader = "x-goog-api-key";

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
        var body = BuildRequestBody(messages, options);

        using var request = CreateRequest(model, stream: false, body);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Gemini ChatAsync").ConfigureAwait(false);

        return await ReadChatResponseAsync(response, root => ParseChatResponse(root, model), cancellationToken)
            .ConfigureAwait(false);
    }

    public override async IAsyncEnumerable<string> StreamAsync(
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var model = options?.Model ?? _options.DefaultModel;
        var body = BuildRequestBody(messages, options);

        var request = CreateRequest(model, stream: true, body);
        request.Headers.Add("Accept", "text/event-stream");

        var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, "Gemini StreamAsync").ConfigureAwait(false);

        await foreach (var chunk in ReadSseChunksAsync(response, ParseStreamChunk, cancellationToken).ConfigureAwait(false))
            yield return chunk;
    }

    /// <summary>Builds a POST carrying the request body and the API key header.</summary>
    private HttpRequestMessage CreateRequest(string model, bool stream, Dictionary<string, object?> body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(model, stream))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions), System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add(ApiKeyHeader, _options.ApiKey);
        return request;
    }

    private Uri BuildEndpoint(string model, bool stream)
    {
        // The model name is caller-supplied, so it is escaped before it enters the path: an
        // unescaped '?' or '&' would otherwise inject or override query parameters. Each branch
        // spells out the whole URL so that a missing '?' is visible on the line that builds it.
        var resource = $"{_options.BaseUrl.TrimEnd('/')}/models/{Uri.EscapeDataString(model)}";

        return stream
            ? new Uri($"{resource}:streamGenerateContent?alt=sse")
            : new Uri($"{resource}:generateContent");
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
                        parameters = Tools.ToolSchemaWriter.ToParameterSchema(t)
                    }).ToArray()
                }
            };
        }

        return body;
    }

    private static ChatResponse ParseChatResponse(JsonNode root, string model)
    {
        var candidate = root["candidates"]?[0];
        var parts = ReadParts(candidate);

        var content = string.Concat(parts?
            .Where(p => p?["text"] is not null)
            .Select(p => p!["text"]!.GetValue<string>()) ?? []);

        var finishReason = candidate?["finishReason"]?.GetValue<string>();

        int? promptTokens = root["usageMetadata"]?["promptTokenCount"]?.GetValue<int>();
        int? completionTokens = root["usageMetadata"]?["candidatesTokenCount"]?.GetValue<int>();
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

    /// <summary>
    /// Reads a candidate's "parts" list. Gemini legitimately omits it — a candidate stopped by a
    /// safety filter carries a finishReason and no parts — so an absent list is tolerated, while a
    /// list of any other kind is a malformed response and is reported rather than silently read as
    /// empty. <see cref="ProviderBase.ReadChatResponseAsync"/> translates the throw into a
    /// <see cref="ProviderException"/> carrying the raw payload.
    /// </summary>
    private static JsonArray? ReadParts(JsonNode? candidate) => candidate?["content"]?["parts"] switch
    {
        null => null,
        JsonArray parts => parts,
        var other => throw new InvalidOperationException(
            $"expected 'candidates[0].content.parts' to be a JSON array but found {other.GetValueKind()}"),
    };

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

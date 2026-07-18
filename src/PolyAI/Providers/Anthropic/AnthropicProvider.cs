using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using PolyAI.Abstractions;
using PolyAI.Errors;

namespace PolyAI.Providers.Anthropic;

/// <summary>Calls the Anthropic Messages API.</summary>
internal sealed class AnthropicProvider : ProviderBase
{
    private readonly HttpClient _http;
    private readonly AnthropicOptions _options;
    private readonly string _messagesEndpoint;

    public override string ProviderName => "anthropic";

    public AnthropicProvider(HttpClient http, AnthropicOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new PolyAIException("Anthropic API key must not be empty. Set AnthropicOptions.ApiKey.");

        _http = http;
        _options = options;
        _messagesEndpoint = $"{options.BaseUrl.TrimEnd('/')}/messages";
    }

    public override async Task<ChatResponse> ChatAsync(
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var body = BuildRequestBody(messages, options, stream: false);
        using var request = BuildRequest(JsonSerializer.Serialize(body, JsonOptions));
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Anthropic ChatAsync").ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseChatResponse(json);
    }

    public override async IAsyncEnumerable<string> StreamAsync(
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var body = BuildRequestBody(messages, options, stream: true);
        var request = BuildRequest(JsonSerializer.Serialize(body, JsonOptions));
        request.Headers.Add("Accept", "text/event-stream");

        var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, "Anthropic StreamAsync").ConfigureAwait(false);

        await foreach (var chunk in ReadSseChunksAsync(response, ParseStreamChunk, cancellationToken).ConfigureAwait(false))
            yield return chunk;
    }

    private Dictionary<string, object?> BuildRequestBody(IList<ChatMessage> messages, ChatOptions? options, bool stream)
    {
        var model = options?.Model ?? _options.DefaultModel;

        // Anthropic separates system messages from the messages array
        var systemContent = string.Join("\n\n", messages
            .Where(m => m.Role == ChatRole.System)
            .Select(m => m.Content));

        var userMessages = messages
            .Where(m => m.Role != ChatRole.System)
            .Select(ToAnthropicMessage)
            .ToArray();

        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = userMessages,
            ["max_tokens"] = options?.MaxTokens ?? 4096,
            ["stream"] = stream,
        };

        if (!string.IsNullOrEmpty(systemContent)) body["system"] = systemContent;
        if (options?.Temperature is { } temp) body["temperature"] = temp;
        if (options?.TopP is { } topP) body["top_p"] = topP;
        if (options?.StopSequences is { Count: > 0 } stop) body["stop_sequences"] = stop;

        if (options?.Tools is { Count: > 0 } tools)
            body["tools"] = tools.Select(ToAnthropicTool).ToArray();

        return body;
    }

    private HttpRequestMessage BuildRequest(string jsonBody)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _messagesEndpoint)
        {
            Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", _options.ApiKey);
        request.Headers.Add("anthropic-version", _options.ApiVersion);
        return request;
    }

    private static object ToAnthropicMessage(ChatMessage msg) => new
    {
        role = msg.Role == ChatRole.Assistant ? "assistant" : "user",
        content = msg.Content
    };

    private static object ToAnthropicTool(Tools.ToolDefinition tool)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var param in tool.Parameters)
        {
            properties[param.Name] = new { type = param.JsonSchemaType, description = param.Description };
            if (param.Required) required.Add(param.Name);
        }

        return new
        {
            name = tool.Name,
            description = tool.Description,
            input_schema = new
            {
                type = "object",
                properties,
                required
            }
        };
    }

    private static ChatResponse ParseChatResponse(string json)
    {
        var root = JsonNode.Parse(json);
        var content = string.Concat(
            root?["content"]?.AsArray()
                .Where(c => c?["type"]?.GetValue<string>() == "text")
                .Select(c => c?["text"]?.GetValue<string>() ?? string.Empty)
            ?? []);

        var model = root?["model"]?.GetValue<string>();
        var stopReason = root?["stop_reason"]?.GetValue<string>();

        int? promptTokens = root?["usage"]?["input_tokens"]?.GetValue<int>();
        int? completionTokens = root?["usage"]?["output_tokens"]?.GetValue<int>();
        TokenUsage? usage = promptTokens.HasValue && completionTokens.HasValue
            ? new TokenUsage(promptTokens.Value, completionTokens.Value)
            : null;

        var toolCalls = new List<ToolCall>();
        if (root?["content"] is JsonArray contentArray)
        {
            foreach (var item in contentArray)
            {
                if (item?["type"]?.GetValue<string>() != "tool_use") continue;
                var id = item["id"]?.GetValue<string>() ?? string.Empty;
                var name = item["name"]?.GetValue<string>() ?? string.Empty;
                var input = item["input"]?.ToJsonString() ?? "{}";
                toolCalls.Add(new ToolCall(id, name, input));
            }
        }

        return new ChatResponse(content, toolCalls, usage, model, stopReason);
    }

    private static string? ParseStreamChunk(string data)
    {
        try
        {
            var node = JsonNode.Parse(data);
            var type = node?["type"]?.GetValue<string>();
            return type == "content_block_delta"
                ? node?["delta"]?["text"]?.GetValue<string>()
                : null;
        }
        catch
        {
            return null;
        }
    }
}

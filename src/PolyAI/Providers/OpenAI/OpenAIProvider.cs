using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using PolyAI.Abstractions;
using PolyAI.Errors;

namespace PolyAI.Providers.OpenAI;

/// <summary>
/// Calls the OpenAI Chat Completions API (and any compatible endpoint such as Azure OpenAI).
/// </summary>
internal sealed class OpenAIProvider : ProviderBase
{
    private readonly HttpClient _http;
    private readonly OpenAIOptions _options;
    private readonly string _chatEndpoint;

    public override string ProviderName => "openai";

    public OpenAIProvider(HttpClient http, OpenAIOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new PolyAIException("OpenAI API key must not be empty. Set OpenAIOptions.ApiKey.");

        _http = http;
        _options = options;
        _chatEndpoint = $"{options.BaseUrl.TrimEnd('/')}/chat/completions";
    }

    public override async Task<ChatResponse> ChatAsync(
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var body = BuildRequestBody(messages, options, stream: false);
        using var request = BuildRequest(JsonSerializer.Serialize(body, JsonOptions));
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "OpenAI ChatAsync").ConfigureAwait(false);

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

        await EnsureSuccessAsync(response, "OpenAI StreamAsync").ConfigureAwait(false);

        await foreach (var chunk in ReadSseChunksAsync(response, ParseStreamChunk, cancellationToken).ConfigureAwait(false))
            yield return chunk;
    }

    private Dictionary<string, object?> BuildRequestBody(IList<ChatMessage> messages, ChatOptions? options, bool stream)
    {
        var model = options?.Model ?? _options.DefaultModel;
        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages.Select(ToOpenAIMessage).ToArray(),
            ["stream"] = stream,
        };

        if (options?.Temperature is { } temp) body["temperature"] = temp;
        if (options?.TopP is { } topP) body["top_p"] = topP;
        if (options?.MaxTokens is { } maxTok) body["max_tokens"] = maxTok;
        if (options?.StopSequences is { Count: > 0 } stop) body["stop"] = stop;

        if (options?.Tools is { Count: > 0 } tools)
        {
            body["tools"] = tools.Select(ToOpenAITool).ToArray();
            body["tool_choice"] = "auto";
        }

        return body;
    }

    private HttpRequestMessage BuildRequest(string jsonBody)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _chatEndpoint)
        {
            Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
        if (_options.Organization is not null)
            request.Headers.Add("OpenAI-Organization", _options.Organization);
        return request;
    }

    private static object ToOpenAIMessage(ChatMessage msg) => new
    {
        role = msg.Role switch
        {
            ChatRole.System => "system",
            ChatRole.User => "user",
            ChatRole.Assistant => "assistant",
            ChatRole.Tool => "tool",
            _ => "user"
        },
        content = msg.Content
    };

    private static object ToOpenAITool(Tools.ToolDefinition tool)
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
            type = "function",
            function = new
            {
                name = tool.Name,
                description = tool.Description,
                parameters = new
                {
                    type = "object",
                    properties,
                    required
                }
            }
        };
    }

    private static ChatResponse ParseChatResponse(string json)
    {
        var root = JsonNode.Parse(json);
        var choice = root?["choices"]?[0];
        var message = choice?["message"];
        var content = message?["content"]?.GetValue<string>() ?? string.Empty;
        var finishReason = choice?["finish_reason"]?.GetValue<string>();
        var model = root?["model"]?.GetValue<string>();

        int? promptTokens = root?["usage"]?["prompt_tokens"]?.GetValue<int>();
        int? completionTokens = root?["usage"]?["completion_tokens"]?.GetValue<int>();
        TokenUsage? usage = promptTokens.HasValue && completionTokens.HasValue
            ? new TokenUsage(promptTokens.Value, completionTokens.Value)
            : null;

        var toolCalls = new List<ToolCall>();
        if (message?["tool_calls"] is JsonArray toolCallArray)
        {
            foreach (var tc in toolCallArray)
            {
                var id = tc?["id"]?.GetValue<string>() ?? string.Empty;
                var name = tc?["function"]?["name"]?.GetValue<string>() ?? string.Empty;
                var args = tc?["function"]?["arguments"]?.GetValue<string>() ?? "{}";
                toolCalls.Add(new ToolCall(id, name, args));
            }
        }

        return new ChatResponse(content, toolCalls, usage, model, finishReason);
    }

    private static string? ParseStreamChunk(string data)
    {
        try
        {
            var node = JsonNode.Parse(data);
            return node?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }
}

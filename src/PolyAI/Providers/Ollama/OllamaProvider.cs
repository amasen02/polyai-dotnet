using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using PolyAI.Abstractions;

namespace PolyAI.Providers.Ollama;

/// <summary>Calls a local Ollama instance via its Chat API.</summary>
internal sealed class OllamaProvider : ProviderBase
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _options;

    public override string ProviderName => "ollama";

    public OllamaProvider(HttpClient http, OllamaOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        _http = http;
        _options = options;
    }

    public override async Task<ChatResponse> ChatAsync(
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var endpoint = $"{_options.BaseUrl.TrimEnd('/')}/api/chat";
        var body = BuildRequestBody(messages, options, stream: false);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), System.Text.Encoding.UTF8, "application/json")
        };

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Ollama ChatAsync").ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseChatResponse(json);
    }

    public override async IAsyncEnumerable<string> StreamAsync(
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var endpoint = $"{_options.BaseUrl.TrimEnd('/')}/api/chat";
        var body = BuildRequestBody(messages, options, stream: true);

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), System.Text.Encoding.UTF8, "application/json")
        };

        var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, "Ollama StreamAsync").ConfigureAwait(false);

        // Ollama streams newline-delimited JSON, not SSE
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line)) continue;

            string? chunk = null;
            bool done = false;
            try
            {
                var node = JsonNode.Parse(line);
                chunk = node?["message"]?["content"]?.GetValue<string>();
                done = node?["done"]?.GetValue<bool>() ?? false;
            }
            catch { /* skip malformed lines */ }

            if (chunk is not null) yield return chunk;
            if (done) break;
        }
    }

    private Dictionary<string, object?> BuildRequestBody(IList<ChatMessage> messages, ChatOptions? options, bool stream)
    {
        var model = options?.Model ?? _options.DefaultModel;
        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["stream"] = stream,
            ["messages"] = messages.Select(m => new
            {
                role = m.Role switch
                {
                    ChatRole.System => "system",
                    ChatRole.Assistant => "assistant",
                    _ => "user"
                },
                content = m.Content
            }).ToArray()
        };

        var opts = new Dictionary<string, object?>();
        if (options?.Temperature is { } temp) opts["temperature"] = temp;
        if (options?.TopP is { } topP) opts["top_p"] = topP;
        if (options?.MaxTokens is { } maxTok) opts["num_predict"] = maxTok;
        if (opts.Count > 0) body["options"] = opts;

        return body;
    }

    private static ChatResponse ParseChatResponse(string json)
    {
        var root = JsonNode.Parse(json);
        var content = root?["message"]?["content"]?.GetValue<string>() ?? string.Empty;
        var model = root?["model"]?.GetValue<string>();

        int? promptTokens = root?["prompt_eval_count"]?.GetValue<int>();
        int? completionTokens = root?["eval_count"]?.GetValue<int>();
        TokenUsage? usage = promptTokens.HasValue && completionTokens.HasValue
            ? new TokenUsage(promptTokens.Value, completionTokens.Value)
            : null;

        return new ChatResponse(content, null, usage, model, "stop");
    }
}

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using PolyAI.Abstractions;
using PolyAI.Errors;

namespace PolyAI.Providers;

/// <summary>Shared plumbing for all <see cref="IPolyAIClient"/> providers.</summary>
internal abstract class ProviderBase : IPolyAIClient
{
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    private static readonly JsonSerializerOptions StructuredJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public abstract string ProviderName { get; }

    public abstract Task<ChatResponse> ChatAsync(
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default);

    public abstract IAsyncEnumerable<string> StreamAsync(
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default);

    public async Task<T> StructuredAsync<T>(
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var augmented = new List<ChatMessage>(messages)
        {
            ChatMessage.User(
                $"Respond with a single valid JSON object matching this C# type: {typeof(T).Name}. " +
                "Do not include any text outside the JSON object.")
        };

        var response = await ChatAsync(augmented, options, cancellationToken).ConfigureAwait(false);

        var json = response.Content;
        try
        {
            json = ExtractJson(response.Content);
            return JsonSerializer.Deserialize<T>(json, StructuredJsonOptions)
                ?? throw new PolyAIException($"Provider {ProviderName} returned null when deserializing {typeof(T).Name}.");
        }
        catch (JsonException ex)
        {
            throw new PolyAIException(
                $"Provider {ProviderName} returned invalid JSON for structured output. Raw: {json}", ex);
        }
    }

    /// <summary>
    /// Reads a provider's success-path response body and turns it into a <see cref="ChatResponse"/>.
    /// </summary>
    /// <remarks>
    /// A response body is untrusted external input: a proxy can truncate it, a gateway can return an
    /// empty 200, and a provider can change a field's shape. This method is the single boundary where
    /// that is handled, which is why <paramref name="parse"/> runs inside it rather than at the call
    /// site — every malformed-payload failure then surfaces as a <see cref="ProviderException"/>,
    /// carrying the raw payload, so callers can rely on the documented contract that every failure of
    /// this SDK is a <see cref="PolyAIException"/>.
    /// </remarks>
    protected async Task<ChatResponse> ReadChatResponseAsync(
        HttpResponseMessage response,
        Func<JsonNode, ChatResponse> parse,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var statusCode = (int)response.StatusCode;

        if (string.IsNullOrWhiteSpace(body))
            throw MalformedResponse(statusCode, body, "it was empty");

        try
        {
            var root = JsonNode.Parse(body)
                ?? throw MalformedResponse(statusCode, body, "it was the JSON literal null");
            return parse(root);
        }
        // The malformed-payload surface of System.Text.Json, verified against the parser rather than
        // assumed: a reader failure, a node read as the wrong kind (AsArray/GetValue<T> on a
        // mismatched node), or an index past the end of a shorter-than-expected array. A payload can
        // provoke any of the three. Anything else is a defect in this SDK, and must keep escaping.
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            throw MalformedResponse(statusCode, body, ex.Message, ex);
        }
    }

    private ProviderException MalformedResponse(int statusCode, string body, string reason, Exception? inner = null)
    {
        var message = $"could not read the HTTP {statusCode} response body: {reason}";
        return inner is null
            ? new ProviderException(ProviderName, message, statusCode, body)
            : new ProviderException(ProviderName, message, inner, statusCode, body);
    }

    protected async Task EnsureSuccessAsync(HttpResponseMessage response, string context)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var statusCode = (int)response.StatusCode;

        if (statusCode is 401 or 403)
            throw new ProviderAuthException(ProviderName, $"{context}: authentication failed ({statusCode}). Check your API key.", statusCode);

        if (statusCode is 429)
        {
            var retryAfter = ParseRetryAfter(response.Headers.RetryAfter, DateTimeOffset.UtcNow);
            throw new ProviderRateLimitException(ProviderName, $"{context}: rate limit exceeded.", retryAfter);
        }

        throw new ProviderException(ProviderName, $"{context}: unexpected status {statusCode}.", statusCode, body);
    }

    protected static async IAsyncEnumerable<string> ReadSseChunksAsync(
        HttpResponseMessage response,
        Func<string, string?> parseDataLine,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);

        // Driven by ReadLineAsync, never StreamReader.EndOfStream: EndOfStream refills the buffer
        // with a synchronous, non-cancellable read, so on an open-but-idle connection it parks in
        // the loop condition and the token is never observed.
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) break;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var data = line[5..].Trim();
            if (data is "[DONE]") break;

            var chunk = parseDataLine(data);
            if (chunk is not null) yield return chunk;
        }
    }

    internal static TimeSpan? ParseRetryAfter(System.Net.Http.Headers.RetryConditionHeaderValue? value, DateTimeOffset now)
    {
        if (value?.Delta is { } delta) return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (value?.Date is not { } date) return null;

        var remaining = date - now;
        return remaining <= TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    private static string ExtractJson(string text)
    {
        var trimmed = text.Trim();
        // A valid raw JSON value wins before considering Markdown. This prevents JSON string
        // values containing backticks or fence-like text from being reinterpreted as Markdown.
        if (IsCompleteJson(trimmed)) return trimmed;

        // Count only complete delimiter lines. Triple backticks in prose or in a JSON string
        // are content; a second fenced section (including an unrecognized language) is not.
        var delimiterLines = System.Text.RegularExpressions.Regex.Matches(
            trimmed,
            "(?m)^[ \\t]*```[^\\r\\n]*\\r?$",
            System.Text.RegularExpressions.RegexOptions.None);
        if (delimiterLines.Count != 2)
            throw new JsonException("Expected exactly one recognized fenced JSON payload.");

        var matches = System.Text.RegularExpressions.Regex.Matches(
            trimmed,
            "(?m)^[ \\t]*```(?:json)?[ \\t]*\\r?\\n(?<payload>[\\s\\S]*?)\\r?\\n^[ \\t]*```[ \\t]*\\r?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (matches.Count == 1)
        {
            var payload = matches[0].Groups["payload"].Value.Trim();
            if (!string.IsNullOrEmpty(payload) && IsCompleteJson(payload)) return payload;
        }

        throw new JsonException("Expected one complete raw JSON value or one recognized fenced JSON payload.");
    }

    private static bool IsCompleteJson(string value)
    {
        try { using var _ = JsonDocument.Parse(value); return true; }
        catch (JsonException) { return false; }
    }
}

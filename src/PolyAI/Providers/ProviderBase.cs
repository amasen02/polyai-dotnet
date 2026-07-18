using System.Runtime.CompilerServices;
using System.Text.Json;
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

        var json = ExtractJson(response.Content);
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new PolyAIException($"Provider {ProviderName} returned null when deserializing {typeof(T).Name}.");
        }
        catch (JsonException ex)
        {
            throw new PolyAIException(
                $"Provider {ProviderName} returned invalid JSON for structured output. Raw: {json}", ex);
        }
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
            TimeSpan? retryAfter = null;
            if (response.Headers.RetryAfter?.Delta is { } delta) retryAfter = delta;
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

        while (!reader.EndOfStream)
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

    private static string ExtractJson(string text)
    {
        var trimmed = text.Trim();
        // Strip markdown code fences if present
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0) trimmed = trimmed[(firstNewline + 1)..];
            if (trimmed.EndsWith("```", StringComparison.Ordinal))
                trimmed = trimmed[..^3].TrimEnd();
        }
        return trimmed;
    }
}

namespace PolyAI.Abstractions;

/// <summary>
/// Core interface for interacting with a language model provider.
/// Resolve a specific provider by requesting <c>IPolyAIClient</c> with a
/// keyed DI registration (<c>provider</c> key) or use <see cref="IPolyAIRouter"/>
/// to let the router select a provider automatically.
/// </summary>
public interface IPolyAIClient
{
    /// <summary>The provider identifier (e.g. "openai", "anthropic").</summary>
    string ProviderName { get; }

    /// <summary>Generates a complete response for the given conversation.</summary>
    Task<ChatResponse> ChatAsync(
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Streams the response token by token as an async sequence of text chunks.</summary>
    IAsyncEnumerable<string> StreamAsync(
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a response and deserializes it into <typeparamref name="T"/>.
    /// The model is instructed to reply with valid JSON matching the type.
    /// </summary>
    Task<T> StructuredAsync<T>(
        IList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) where T : class;
}

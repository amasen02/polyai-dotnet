using PolyAI.Abstractions;
using PolyAI.Errors;
using PolyAI.Providers.OpenAI;

namespace PolyAI.Providers.Azure;

/// <summary>
/// Calls Azure OpenAI — delegates to <see cref="OpenAIProvider"/>.
/// The caller (typically <see cref="Extensions.PolyAIBuilder"/>) must supply an
/// <see cref="HttpClient"/> whose pipeline already includes <see cref="AzureAuthHandler"/>
/// so that Bearer auth is replaced with the Azure <c>api-key</c> header.
/// </summary>
internal sealed class AzureOpenAIProvider : IPolyAIClient
{
    private readonly IPolyAIClient _inner;

    public string ProviderName => "azure-openai";

    public AzureOpenAIProvider(HttpClient http, AzureOpenAIOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new PolyAIException("Azure OpenAI API key must not be empty. Set AzureOpenAIOptions.ApiKey.");
        if (string.IsNullOrWhiteSpace(options.Endpoint))
            throw new PolyAIException("Azure OpenAI endpoint must not be empty. Set AzureOpenAIOptions.Endpoint.");
        if (string.IsNullOrWhiteSpace(options.DeploymentName))
            throw new PolyAIException("Azure OpenAI deployment name must not be empty. Set AzureOpenAIOptions.DeploymentName.");

        var baseUrl = $"{options.Endpoint.TrimEnd('/')}/openai/deployments/{options.DeploymentName}";
        _inner = new OpenAIProvider(http, new OpenAIOptions
        {
            ApiKey = options.ApiKey,
            BaseUrl = baseUrl,
            DefaultModel = options.DeploymentName,
        });
    }

    public Task<ChatResponse> ChatAsync(IList<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => _inner.ChatAsync(messages, options, cancellationToken);

    public IAsyncEnumerable<string> StreamAsync(IList<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => _inner.StreamAsync(messages, options, cancellationToken);

    public Task<T> StructuredAsync<T>(IList<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) where T : class
        => _inner.StructuredAsync<T>(messages, options, cancellationToken);
}

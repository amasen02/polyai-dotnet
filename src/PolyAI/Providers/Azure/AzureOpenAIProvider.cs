using PolyAI.Abstractions;
using PolyAI.Errors;
using PolyAI.Providers.OpenAI;

namespace PolyAI.Providers.Azure;

/// <summary>
/// Calls Azure OpenAI — thin wrapper over <see cref="OpenAIProvider"/> with
/// Azure-specific endpoint and API-key authentication header.
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
        var chatEndpoint = $"{baseUrl}/chat/completions?api-version={options.ApiVersion}";

        // Delegate to OpenAIProvider with Azure-adjusted base URL; override the HttpClient
        // so we can inject the api-key header differently (Azure uses api-key, not Bearer).
        var azureHttp = new AzureHttpClientWrapper(http, options.ApiKey);

        _inner = new OpenAIProvider(azureHttp, new OpenAIOptions
        {
            ApiKey = options.ApiKey,   // still passed but header is overridden below
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

    /// <summary>Intercepts outgoing requests to replace Bearer auth with Azure api-key header.</summary>
    private sealed class AzureHttpClientWrapper : HttpClient
    {
        private readonly HttpClient _inner;
        private readonly string _apiKey;

        public AzureHttpClientWrapper(HttpClient inner, string apiKey)
        {
            _inner = inner;
            _apiKey = apiKey;
        }

        public new Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
            => SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);

        public new async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken cancellationToken = default)
        {
            // Replace Bearer token with Azure api-key header
            request.Headers.Authorization = null;
            request.Headers.Remove("api-key");
            request.Headers.Add("api-key", _apiKey);
            return await _inner.SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);
        }
    }
}

using PolyAI.Abstractions;
using PolyAI.Errors;
using PolyAI.Providers.OpenAI;

namespace PolyAI.Providers.Azure;

/// <summary>
/// Calls Azure OpenAI — delegates to <see cref="OpenAIProvider"/> with an
/// Azure-auth <see cref="DelegatingHandler"/> that replaces the Bearer header
/// with the Azure <c>api-key</c> header and appends the <c>api-version</c>
/// query string to every request.
/// </summary>
internal sealed class AzureOpenAIProvider : IPolyAIClient
{
    private readonly IPolyAIClient _inner;

    public string ProviderName => "azure-openai";

    public AzureOpenAIProvider(HttpClient httpClient, AzureOpenAIOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new PolyAIException("Azure OpenAI API key must not be empty. Set AzureOpenAIOptions.ApiKey.");
        if (string.IsNullOrWhiteSpace(options.Endpoint))
            throw new PolyAIException("Azure OpenAI endpoint must not be empty. Set AzureOpenAIOptions.Endpoint.");
        if (string.IsNullOrWhiteSpace(options.DeploymentName))
            throw new PolyAIException("Azure OpenAI deployment name must not be empty. Set AzureOpenAIOptions.DeploymentName.");

        // Wrap the base handler in the Azure auth delegating handler
        var azureHandler = new AzureAuthHandler(options.ApiKey, options.ApiVersion)
        {
            InnerHandler = httpClient.GetType()
                .GetProperty("Handler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(httpClient) as HttpMessageHandler
                ?? new HttpClientHandler()
        };

        var azureHttp = new HttpClient(azureHandler);

        var baseUrl = $"{options.Endpoint.TrimEnd('/')}/openai/deployments/{options.DeploymentName}";
        _inner = new OpenAIProvider(azureHttp, new OpenAIOptions
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

    /// <summary>
    /// Intercepts every outgoing request:
    ///   - Replaces <c>Authorization: Bearer ...</c> with <c>api-key: ...</c>
    ///   - Appends <c>?api-version=...</c> to the request URL
    /// </summary>
    private sealed class AzureAuthHandler : DelegatingHandler
    {
        private readonly string _apiKey;
        private readonly string _apiVersion;

        public AzureAuthHandler(string apiKey, string apiVersion)
        {
            _apiKey = apiKey;
            _apiVersion = apiVersion;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Swap Bearer header for api-key header
            request.Headers.Authorization = null;
            request.Headers.Remove("api-key");
            request.Headers.Add("api-key", _apiKey);

            // Append api-version query parameter
            var uriBuilder = new UriBuilder(request.RequestUri!);
            var query = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);
            if (string.IsNullOrEmpty(query["api-version"]))
            {
                query["api-version"] = _apiVersion;
                uriBuilder.Query = query.ToString();
                request.RequestUri = uriBuilder.Uri;
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}

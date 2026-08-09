using Microsoft.Extensions.DependencyInjection;
using PolyAI.Abstractions;
using PolyAI.Errors;
using PolyAI.Providers.Anthropic;
using PolyAI.Providers.Azure;
using PolyAI.Providers.Gemini;
using PolyAI.Providers.Ollama;
using PolyAI.Providers.OpenAI;

namespace PolyAI.Extensions;

/// <summary>
/// Fluent builder returned by <see cref="ServiceCollectionExtensions.AddPolyAI"/>.
/// Chain <c>.UseOpenAI()</c>, <c>.UseAnthropic()</c>, etc. to register providers.
/// </summary>
public sealed class PolyAIBuilder
{
    private const string AzureClientName = "polyai-azure-openai";

    private readonly IServiceCollection _services;
    private readonly Dictionary<string, Func<IServiceProvider, IPolyAIClient>> _factories = new(StringComparer.OrdinalIgnoreCase);
    private string? _defaultProvider;
    private AzureOpenAIOptions? _azureOptions;

    internal PolyAIBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>Registers the OpenAI provider.</summary>
    public PolyAIBuilder UseOpenAI(string apiKey, Action<OpenAIOptions>? configure = null)
    {
        var options = new OpenAIOptions { ApiKey = apiKey };
        configure?.Invoke(options);
        AddFactory("openai", sp => new Providers.OpenAI.OpenAIProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("polyai-openai"), options));
        _defaultProvider ??= "openai";
        return this;
    }

    /// <summary>Registers the Anthropic Claude provider.</summary>
    public PolyAIBuilder UseAnthropic(string apiKey, Action<AnthropicOptions>? configure = null)
    {
        var options = new AnthropicOptions { ApiKey = apiKey };
        configure?.Invoke(options);
        AddFactory("anthropic", sp => new AnthropicProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("polyai-anthropic"), options));
        _defaultProvider ??= "anthropic";
        return this;
    }

    /// <summary>Registers the Google Gemini provider.</summary>
    public PolyAIBuilder UseGemini(string apiKey, Action<GeminiOptions>? configure = null)
    {
        var options = new GeminiOptions { ApiKey = apiKey };
        configure?.Invoke(options);
        AddFactory("gemini", sp => new GeminiProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("polyai-gemini"), options));
        _defaultProvider ??= "gemini";
        return this;
    }

    /// <summary>Registers the local Ollama provider (no API key required).</summary>
    public PolyAIBuilder UseOllama(Action<OllamaOptions>? configure = null)
    {
        var options = new OllamaOptions();
        configure?.Invoke(options);
        AddFactory("ollama", sp => new OllamaProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("polyai-ollama"), options));
        _defaultProvider ??= "ollama";
        return this;
    }

    /// <summary>Registers the Azure OpenAI provider.</summary>
    public PolyAIBuilder UseAzureOpenAI(string apiKey, string endpoint, string deploymentName, Action<AzureOpenAIOptions>? configure = null)
    {
        var options = new AzureOpenAIOptions
        {
            ApiKey = apiKey,
            Endpoint = endpoint,
            DeploymentName = deploymentName,
        };
        configure?.Invoke(options);

        // Held for Build(), which attaches the Azure auth handler to the named client. Configuring
        // the client here instead would register services before the configuration has been
        // validated, leaving a rejected AddPolyAI call's container half-populated.
        _azureOptions = options;

        AddFactory("azure-openai", sp => new AzureOpenAIProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(AzureClientName), options));
        _defaultProvider ??= "azure-openai";
        return this;
    }

    /// <summary>Sets which provider is resolved when no name is specified.</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="providerName"/> is null, empty, or whitespace. A missing configuration key
    /// binds to null here; guessing a default would silently route traffic to a provider nobody
    /// chose. Whether the name matches a registered provider is checked later, in <see cref="Build"/>,
    /// because the fluent API allows the default to be named before the provider it refers to.
    /// </exception>
    public PolyAIBuilder WithDefaultProvider(string providerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        _defaultProvider = providerName;
        return this;
    }

    internal void Build()
    {
        // Reject an unusable configuration before touching the service collection: everything
        // needed to detect it is known here, and the alternative is a container that builds
        // clean, passes health checks, and throws on the first user request.
        if (_factories.Count == 0)
            throw new PolyAIException(PolyAIRouter.NoProvidersRegisteredMessage);

        var defaultProvider = _defaultProvider ?? _factories.Keys.First();

        if (!_factories.ContainsKey(defaultProvider))
            throw new PolyAIException(
                $"WithDefaultProvider(\"{defaultProvider}\") names a provider that was never registered. "
                + $"Registered providers: {string.Join(", ", _factories.Keys)}.");

        // Register named HttpClients for each provider
        _services.AddHttpClient("polyai-openai");
        _services.AddHttpClient("polyai-anthropic");
        _services.AddHttpClient("polyai-gemini");
        _services.AddHttpClient("polyai-ollama");
        var azureClient = _services.AddHttpClient(AzureClientName);

        // Azure authenticates with an `api-key` header and an `api-version` query parameter instead
        // of the Bearer auth its underlying OpenAI provider sets, so its named client carries one
        // extra handler. Attaching it here keeps the client resolvable from IHttpClientFactory like
        // every other provider, so handlers a consumer adds to this name are actually in the path.
        if (_azureOptions is { } azureOptions)
            azureClient.AddHttpMessageHandler(
                () => new Providers.Azure.AzureAuthHandler(azureOptions.ApiKey, azureOptions.ApiVersion));

        var factories = new Dictionary<string, Func<IServiceProvider, IPolyAIClient>>(_factories, StringComparer.OrdinalIgnoreCase);

        _services.AddSingleton<IPolyAIRouter>(sp =>
        {
            var clients = factories.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value(sp),
                StringComparer.OrdinalIgnoreCase);

            return new PolyAIRouter(clients, defaultProvider);
        });

        // Register the default client via the router for simple single-provider usage
        _services.AddSingleton<IPolyAIClient>(sp =>
            sp.GetRequiredService<IPolyAIRouter>().GetProvider());
    }

    private void AddFactory(string name, Func<IServiceProvider, IPolyAIClient> factory)
        => _factories[name] = factory;
}

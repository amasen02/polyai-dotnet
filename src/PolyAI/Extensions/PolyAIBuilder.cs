using Microsoft.Extensions.DependencyInjection;
using PolyAI.Abstractions;
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
    private readonly IServiceCollection _services;
    private readonly Dictionary<string, Func<IServiceProvider, IPolyAIClient>> _factories = new(StringComparer.OrdinalIgnoreCase);
    private string? _defaultProvider;

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
        AddFactory("azure-openai", _ =>
        {
            var handler = new Providers.Azure.AzureAuthHandler(options.ApiKey, options.ApiVersion)
            {
                InnerHandler = new HttpClientHandler()
            };
            return new AzureOpenAIProvider(new HttpClient(handler), options);
        });
        _defaultProvider ??= "azure-openai";
        return this;
    }

    /// <summary>Sets which provider is resolved when no name is specified.</summary>
    public PolyAIBuilder WithDefaultProvider(string providerName)
    {
        _defaultProvider = providerName;
        return this;
    }

    internal void Build()
    {
        // Register named HttpClients for each provider
        _services.AddHttpClient("polyai-openai");
        _services.AddHttpClient("polyai-anthropic");
        _services.AddHttpClient("polyai-gemini");
        _services.AddHttpClient("polyai-ollama");
        _services.AddHttpClient("polyai-azure-openai");

        var factories = new Dictionary<string, Func<IServiceProvider, IPolyAIClient>>(_factories, StringComparer.OrdinalIgnoreCase);
        var defaultProvider = _defaultProvider ?? factories.Keys.FirstOrDefault() ?? string.Empty;

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

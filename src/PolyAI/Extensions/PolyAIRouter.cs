using PolyAI.Abstractions;
using PolyAI.Errors;

namespace PolyAI.Extensions;

/// <summary>
/// Default router: resolves providers by name and falls back to the configured default.
/// </summary>
internal sealed class PolyAIRouter : IPolyAIRouter
{
    private readonly IReadOnlyDictionary<string, IPolyAIClient> _providers;
    private readonly string _defaultProvider;

    public IReadOnlyList<string> RegisteredProviders => [.. _providers.Keys];

    public PolyAIRouter(IReadOnlyDictionary<string, IPolyAIClient> providers, string defaultProvider)
    {
        if (providers.Count == 0)
            throw new PolyAIException("No AI providers registered. Call .UseAnthropic(), .UseOpenAI() etc. in AddPolyAI().");

        _providers = providers;
        _defaultProvider = defaultProvider;
    }

    public IPolyAIClient GetProvider(string? providerName = null)
    {
        var key = providerName ?? _defaultProvider;

        if (_providers.TryGetValue(key, out var client)) return client;

        throw new PolyAIException(
            $"No provider registered under '{key}'. Registered providers: {string.Join(", ", _providers.Keys)}.");
    }
}

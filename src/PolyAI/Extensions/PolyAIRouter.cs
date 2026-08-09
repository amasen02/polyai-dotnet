using PolyAI.Abstractions;
using PolyAI.Errors;

namespace PolyAI.Extensions;

/// <summary>
/// Default router: resolves providers by name and falls back to the configured default.
/// </summary>
internal sealed class PolyAIRouter : IPolyAIRouter
{
    /// <summary>
    /// Shared by <see cref="PolyAIBuilder.Build"/>, which rejects the configuration before the
    /// container is built, and by this constructor, which guards the invariant for any future
    /// caller. Declared once so the two cannot drift apart.
    /// </summary>
    internal const string NoProvidersRegisteredMessage =
        "No AI providers registered. Call .UseAnthropic(), .UseOpenAI() etc. in AddPolyAI().";

    private readonly IReadOnlyDictionary<string, IPolyAIClient> _providers;
    private readonly string _defaultProvider;

    public IReadOnlyList<string> RegisteredProviders => [.. _providers.Keys];

    public PolyAIRouter(IReadOnlyDictionary<string, IPolyAIClient> providers, string defaultProvider)
    {
        if (providers.Count == 0)
            throw new PolyAIException(NoProvidersRegisteredMessage);

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

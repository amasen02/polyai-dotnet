namespace PolyAI.Abstractions;

/// <summary>
/// Routes requests to one of the registered <see cref="IPolyAIClient"/> providers.
/// The default implementation uses the provider configured as the default;
/// override by implementing this interface and registering it in DI.
/// </summary>
public interface IPolyAIRouter
{
    /// <summary>Returns the provider registered under <paramref name="providerName"/>, or the default.</summary>
    IPolyAIClient GetProvider(string? providerName = null);

    /// <summary>Returns all registered provider names.</summary>
    IReadOnlyList<string> RegisteredProviders { get; }
}

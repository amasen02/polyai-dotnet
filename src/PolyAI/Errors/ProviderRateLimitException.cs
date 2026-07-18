namespace PolyAI.Errors;

/// <summary>Raised when a provider returns a 429 Too Many Requests response.</summary>
public sealed class ProviderRateLimitException : ProviderException
{
    /// <summary>When the caller may retry, if indicated by the provider.</summary>
    public TimeSpan? RetryAfter { get; }

    public ProviderRateLimitException(string provider, string message, TimeSpan? retryAfter = null)
        : base(provider, message, statusCode: 429)
    {
        RetryAfter = retryAfter;
    }
}

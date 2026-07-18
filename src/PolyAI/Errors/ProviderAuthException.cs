namespace PolyAI.Errors;

/// <summary>Raised when a provider rejects the API key (401/403).</summary>
public sealed class ProviderAuthException : ProviderException
{
    public ProviderAuthException(string provider, string message, int statusCode)
        : base(provider, message, statusCode: statusCode) { }
}

namespace PolyAI.Errors;

/// <summary>Raised when a provider API call fails.</summary>
public class ProviderException : PolyAIException
{
    /// <summary>The provider that raised this error (e.g. "openai", "anthropic").</summary>
    public string Provider { get; }

    /// <summary>HTTP status code from the provider, if available.</summary>
    public int? StatusCode { get; }

    /// <summary>Raw error body from the provider, if available.</summary>
    public string? ResponseBody { get; }

    public ProviderException(string provider, string message, int? statusCode = null, string? responseBody = null)
        : base($"[{provider}] {message}")
    {
        Provider = provider;
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public ProviderException(string provider, string message, Exception innerException, int? statusCode = null, string? responseBody = null)
        : base($"[{provider}] {message}", innerException)
    {
        Provider = provider;
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}

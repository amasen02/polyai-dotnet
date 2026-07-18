namespace PolyAI.Providers.OpenAI;

/// <summary>Configuration options for the OpenAI provider.</summary>
public sealed class OpenAIOptions
{
    /// <summary>OpenAI API key.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Default model to use. Defaults to gpt-4o-mini.</summary>
    public string DefaultModel { get; set; } = "gpt-4o-mini";

    /// <summary>Override the base URL (useful for OpenAI-compatible endpoints).</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>Organization ID header value (optional).</summary>
    public string? Organization { get; set; }
}

namespace PolyAI.Providers.Anthropic;

/// <summary>Configuration options for the Anthropic provider.</summary>
public sealed class AnthropicOptions
{
    /// <summary>Anthropic API key (starts with "sk-ant-").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Default model to use. Defaults to claude-3-5-haiku-20241022.</summary>
    public string DefaultModel { get; set; } = "claude-3-5-haiku-20241022";

    /// <summary>Anthropic API version header value.</summary>
    public string ApiVersion { get; set; } = "2023-06-01";

    /// <summary>Base URL for the Anthropic API.</summary>
    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1";
}

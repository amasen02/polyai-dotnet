namespace PolyAI.Providers.Gemini;

/// <summary>Configuration options for the Google Gemini provider.</summary>
public sealed class GeminiOptions
{
    /// <summary>Google AI API key.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Default model to use. Defaults to gemini-1.5-flash.</summary>
    public string DefaultModel { get; set; } = "gemini-1.5-flash";

    /// <summary>Base URL for the Gemini API.</summary>
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
}

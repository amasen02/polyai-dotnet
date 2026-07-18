namespace PolyAI.Providers.Ollama;

/// <summary>Configuration options for the local Ollama provider.</summary>
public sealed class OllamaOptions
{
    /// <summary>Default model to use. Defaults to llama3.2.</summary>
    public string DefaultModel { get; set; } = "llama3.2";

    /// <summary>Base URL for the local Ollama server. Defaults to http://localhost:11434.</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";
}

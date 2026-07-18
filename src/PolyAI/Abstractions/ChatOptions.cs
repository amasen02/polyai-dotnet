namespace PolyAI.Abstractions;

/// <summary>Controls generation behaviour for a single chat request.</summary>
public sealed class ChatOptions
{
    /// <summary>Model name/ID override. Uses the provider default when null.</summary>
    public string? Model { get; set; }

    /// <summary>Sampling temperature in [0, 2]. Lower = more deterministic.</summary>
    public float? Temperature { get; set; }

    /// <summary>Nucleus-sampling probability mass to consider.</summary>
    public float? TopP { get; set; }

    /// <summary>Maximum tokens to generate. Provider default is used when null.</summary>
    public int? MaxTokens { get; set; }

    /// <summary>Stop sequences that end generation early.</summary>
    public IReadOnlyList<string>? StopSequences { get; set; }

    /// <summary>
    /// Tool definitions to expose to the model for this call.
    /// When non-empty the provider enables function/tool calling.
    /// </summary>
    public IReadOnlyList<Tools.ToolDefinition>? Tools { get; set; }
}

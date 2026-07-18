namespace PolyAI.Abstractions;

/// <summary>The complete response from a non-streaming chat request.</summary>
public sealed class ChatResponse
{
    /// <summary>The generated text content. Empty when the model issued tool calls instead.</summary>
    public string Content { get; }

    /// <summary>Tool calls requested by the model, if any.</summary>
    public IReadOnlyList<ToolCall> ToolCalls { get; }

    /// <summary>Token usage information, when reported by the provider.</summary>
    public TokenUsage? Usage { get; }

    /// <summary>Which model produced the response.</summary>
    public string? Model { get; }

    /// <summary>Provider-specific finish reason (e.g. "stop", "tool_calls", "max_tokens").</summary>
    public string? FinishReason { get; }

    public ChatResponse(
        string content,
        IReadOnlyList<ToolCall>? toolCalls = null,
        TokenUsage? usage = null,
        string? model = null,
        string? finishReason = null)
    {
        Content = content;
        ToolCalls = toolCalls ?? [];
        Usage = usage;
        Model = model;
        FinishReason = finishReason;
    }
}

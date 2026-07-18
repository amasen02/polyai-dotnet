namespace PolyAI.Abstractions;

/// <summary>Token consumption for a single request/response pair.</summary>
public sealed class TokenUsage
{
    public int PromptTokens { get; }
    public int CompletionTokens { get; }
    public int TotalTokens => PromptTokens + CompletionTokens;

    public TokenUsage(int promptTokens, int completionTokens)
    {
        PromptTokens = promptTokens;
        CompletionTokens = completionTokens;
    }
}

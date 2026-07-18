namespace PolyAI.Abstractions;

/// <summary>A single message in a chat conversation.</summary>
public sealed class ChatMessage
{
    public ChatRole Role { get; }
    public string Content { get; }

    public ChatMessage(ChatRole role, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        Role = role;
        Content = content;
    }

    public static ChatMessage System(string content) => new(ChatRole.System, content);
    public static ChatMessage User(string content) => new(ChatRole.User, content);
    public static ChatMessage Assistant(string content) => new(ChatRole.Assistant, content);
}

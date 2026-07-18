namespace PolyAI.Abstractions;

/// <summary>A tool invocation requested by the model in its response.</summary>
public sealed class ToolCall
{
    public string Id { get; }
    public string Name { get; }
    public string ArgumentsJson { get; }

    public ToolCall(string id, string name, string argumentsJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        Name = name;
        ArgumentsJson = argumentsJson;
    }
}

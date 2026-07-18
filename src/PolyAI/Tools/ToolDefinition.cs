namespace PolyAI.Tools;

/// <summary>Describes a tool that the model can call during generation.</summary>
public sealed class ToolDefinition
{
    public string Name { get; }
    public string Description { get; }
    public IReadOnlyList<ToolParameter> Parameters { get; }

    public ToolDefinition(string name, string description, IReadOnlyList<ToolParameter>? parameters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Name = name;
        Description = description;
        Parameters = parameters ?? [];
    }
}

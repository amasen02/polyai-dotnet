namespace PolyAI.Tools;

/// <summary>Describes a single parameter for a tool definition.</summary>
public sealed class ToolParameter
{
    public string Name { get; }
    public string Description { get; }
    public string JsonSchemaType { get; }
    public bool Required { get; }

    public ToolParameter(string name, string description, string jsonSchemaType, bool required = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Description = description;
        JsonSchemaType = jsonSchemaType;
        Required = required;
    }
}

namespace PolyAI.Tools;

/// <summary>Describes a single parameter for a tool definition.</summary>
public sealed class ToolParameter
{
    public string Name { get; }
    public string Description { get; }
    public bool Required { get; }

    /// <summary>The full JSON Schema for this parameter, including array element and enum details.</summary>
    public JsonSchemaNode Schema { get; }

    /// <summary>The JSON Schema type keyword. Shorthand for <see cref="JsonSchemaNode.Type"/> on <see cref="Schema"/>.</summary>
    public string JsonSchemaType => Schema.Type;

    public ToolParameter(string name, string description, JsonSchemaNode schema, bool required = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(schema);
        Name = name;
        Description = description;
        Schema = schema;
        Required = required;
    }

    public ToolParameter(string name, string description, string jsonSchemaType, bool required = true)
        : this(name, description, new JsonSchemaNode(jsonSchemaType), required)
    {
    }
}

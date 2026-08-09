namespace PolyAI.Tools;

/// <summary>
/// Renders a <see cref="ToolDefinition"/> as the JSON Schema object every provider wraps.
/// OpenAI nests it under <c>function.parameters</c>, Anthropic under <c>input_schema</c> and
/// Gemini under <c>parameters</c>, but the object itself is identical — so it is built once here
/// rather than copied into each provider, where the three copies previously drifted together.
/// </summary>
internal static class ToolSchemaWriter
{
    /// <summary>Builds the <c>{ type: "object", properties, required }</c> schema for a tool's parameters.</summary>
    public static object ToParameterSchema(ToolDefinition tool)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var param in tool.Parameters)
        {
            properties[param.Name] = ToSchemaObject(param.Schema, param.Description);
            if (param.Required) required.Add(param.Name);
        }

        return new
        {
            type = "object",
            properties,
            required
        };
    }

    private static Dictionary<string, object> ToSchemaObject(JsonSchemaNode node, string? description)
    {
        var schema = new Dictionary<string, object> { ["type"] = node.Type };

        if (!string.IsNullOrEmpty(description)) schema["description"] = description;
        if (node.Format is not null) schema["format"] = node.Format;
        if (node.EnumValues is not null) schema["enum"] = node.EnumValues;

        // An array's element schema carries no description of its own; the parameter's description
        // already describes the collection as a whole.
        if (node.Items is not null) schema["items"] = ToSchemaObject(node.Items, description: null);

        return schema;
    }
}

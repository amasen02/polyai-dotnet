using System.Reflection;

namespace PolyAI.Tools;

/// <summary>
/// Scans an object for methods annotated with <see cref="PolyAIToolAttribute"/>
/// and builds <see cref="ToolDefinition"/> descriptors for them.
/// </summary>
public static class ToolRegistry
{
    private static readonly Dictionary<Type, string> JsonSchemaTypeMap = new()
    {
        [typeof(string)] = "string",
        [typeof(int)] = "integer",
        [typeof(long)] = "integer",
        [typeof(float)] = "number",
        [typeof(double)] = "number",
        [typeof(decimal)] = "number",
        [typeof(bool)] = "boolean",
        [typeof(int?)] = "integer",
        [typeof(long?)] = "integer",
        [typeof(float?)] = "number",
        [typeof(double?)] = "number",
        [typeof(decimal?)] = "number",
        [typeof(bool?)] = "boolean",
    };

    /// <summary>Returns all tool definitions discoverable on <paramref name="instance"/>.</summary>
    public static IReadOnlyList<ToolDefinition> FromInstance<T>(T instance) where T : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        return FromType(typeof(T));
    }

    /// <summary>Returns all tool definitions on public methods of <paramref name="type"/>.</summary>
    public static IReadOnlyList<ToolDefinition> FromType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var tools = new List<ToolDefinition>();

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            var toolAttr = method.GetCustomAttribute<PolyAIToolAttribute>();
            if (toolAttr is null) continue;

            var name = toolAttr.Name ?? ToSnakeCase(method.Name);
            var parameters = BuildParameters(method);
            tools.Add(new ToolDefinition(name, toolAttr.Description, parameters));
        }

        return tools;
    }

    private static IReadOnlyList<ToolParameter> BuildParameters(MethodInfo method)
    {
        var result = new List<ToolParameter>();

        foreach (var param in method.GetParameters())
        {
            var paramAttr = param.GetCustomAttribute<PolyAIParamAttribute>();
            var description = paramAttr?.Description ?? param.Name ?? "parameter";
            var schemaType = JsonSchemaTypeMap.GetValueOrDefault(param.ParameterType, "string");
            var required = !param.HasDefaultValue && !IsNullable(param);
            result.Add(new ToolParameter(param.Name ?? "param", description, schemaType, required));
        }

        return result;
    }

    private static bool IsNullable(ParameterInfo param)
    {
        var nullabilityContext = new NullabilityInfoContext();
        var nullabilityInfo = nullabilityContext.Create(param);
        return nullabilityInfo.WriteState is NullabilityState.Nullable
            || Nullable.GetUnderlyingType(param.ParameterType) is not null;
    }

    private static string ToSnakeCase(string name)
    {
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0) result.Append('_');
            result.Append(char.ToLowerInvariant(c));
        }
        return result.ToString();
    }
}

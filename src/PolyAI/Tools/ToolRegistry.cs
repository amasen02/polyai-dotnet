using System.Reflection;
using PolyAI.Errors;

namespace PolyAI.Tools;

/// <summary>
/// Scans an object for methods annotated with <see cref="PolyAIToolAttribute"/>
/// and builds <see cref="ToolDefinition"/> descriptors for them.
/// </summary>
public static class ToolRegistry
{
    /// <summary>
    /// Bounds the recursive descent through nested collections. A self-referential element type
    /// (<c>class Tree : IEnumerable&lt;Tree&gt;</c>) would otherwise recurse until the process dies
    /// on a <see cref="StackOverflowException"/>, which .NET cannot catch.
    /// </summary>
    private const int MaxSchemaDepth = 8;

    private static readonly JsonSchemaNode StringSchema = new("string");
    private static readonly JsonSchemaNode IntegerSchema = new("integer");
    private static readonly JsonSchemaNode NumberSchema = new("number");
    private static readonly JsonSchemaNode BooleanSchema = new("boolean");

    /// <summary>
    /// <c>date-time</c> is the only <c>format</c> emitted. Gemini rejects every other format on a
    /// string ("only 'enum' and 'date-time' are supported for STRING type"), so <see cref="Guid"/>,
    /// <see cref="DateOnly"/> and <see cref="TimeOnly"/> are described as plain strings rather than
    /// annotated with a format that would fail the request on one of the supported providers.
    /// </summary>
    private static readonly JsonSchemaNode DateTimeSchema = new("string", format: "date-time");

    private static readonly Dictionary<Type, JsonSchemaNode> ScalarSchemas = new()
    {
        [typeof(string)] = StringSchema,
        [typeof(Guid)] = StringSchema,
        [typeof(DateOnly)] = StringSchema,
        [typeof(TimeOnly)] = StringSchema,
        [typeof(DateTime)] = DateTimeSchema,
        [typeof(DateTimeOffset)] = DateTimeSchema,
        [typeof(bool)] = BooleanSchema,
        [typeof(byte)] = IntegerSchema,
        [typeof(sbyte)] = IntegerSchema,
        [typeof(short)] = IntegerSchema,
        [typeof(ushort)] = IntegerSchema,
        [typeof(int)] = IntegerSchema,
        [typeof(uint)] = IntegerSchema,
        [typeof(long)] = IntegerSchema,
        [typeof(ulong)] = IntegerSchema,
        [typeof(float)] = NumberSchema,
        [typeof(double)] = NumberSchema,
        [typeof(decimal)] = NumberSchema,
    };

    /// <summary>
    /// Returns all tool definitions discoverable on <paramref name="instance"/>, using its runtime
    /// type. Callers usually hold a tool object behind an interface or base type resolved from DI,
    /// where the attributes live on the concrete class.
    /// </summary>
    public static IReadOnlyList<ToolDefinition> FromInstance<T>(T instance) where T : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        return FromType(instance.GetType());
    }

    /// <summary>Returns all tool definitions on public methods of <paramref name="type"/>.</summary>
    /// <exception cref="PolyAIException">A tool parameter has no JSON Schema representation.</exception>
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
            // A CancellationToken is supplied by the caller dispatching the tool, never by the
            // model, so it is not part of the schema the model is asked to fill.
            if (param.ParameterType == typeof(CancellationToken)) continue;

            var schema = TryBuildSchema(param.ParameterType, depth: 0)
                ?? throw new PolyAIException(
                    $"Tool '{method.Name}' parameter '{param.Name}' has type "
                    + $"'{param.ParameterType.Name}', which has no JSON Schema representation. "
                    + "Supported: string, bool, the numeric primitives, DateTime, DateTimeOffset, "
                    + "DateOnly, TimeOnly, Guid, enums, and collections of those. Change the "
                    + "parameter type, or accept a JSON string and deserialize it inside the tool.");

            var paramAttr = param.GetCustomAttribute<PolyAIParamAttribute>();
            var description = paramAttr?.Description ?? param.Name ?? "parameter";
            var required = !param.HasDefaultValue && !IsNullable(param);
            result.Add(new ToolParameter(param.Name ?? "param", description, schema, required));
        }

        return result;
    }

    /// <summary>Maps a CLR type to its JSON Schema, or <see langword="null"/> when it has none.</summary>
    private static JsonSchemaNode? TryBuildSchema(Type type, int depth)
    {
        if (depth > MaxSchemaDepth) return null;

        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (ScalarSchemas.TryGetValue(underlying, out var scalar)) return scalar;
        if (underlying.IsEnum) return new JsonSchemaNode("string", enumValues: Enum.GetNames(underlying));

        var elementType = GetCollectionElementType(underlying);
        if (elementType is null) return null;

        var items = TryBuildSchema(elementType, depth + 1);
        return items is null ? null : new JsonSchemaNode("array", items: items);
    }

    /// <summary>
    /// Returns the element type when <paramref name="type"/> is a JSON-array-shaped collection.
    /// <see cref="string"/> never reaches here — it is matched as a scalar first, despite being an
    /// <see cref="IEnumerable{T}"/> of <see cref="char"/>.
    /// </summary>
    private static Type? GetCollectionElementType(Type type)
    {
        // A multi-dimensional array is not a JSON array of its element type.
        if (type.IsArray) return type.GetArrayRank() == 1 ? type.GetElementType() : null;

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            return type.GetGenericArguments()[0];

        return type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
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

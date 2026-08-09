namespace PolyAI.Tools;

/// <summary>
/// The JSON Schema description of a single value: a tool parameter, or the element type of an
/// array parameter. Nested through <see cref="Items"/> so that collections describe what they hold.
/// </summary>
public sealed class JsonSchemaNode
{
    /// <summary>JSON Schema type keyword: <c>string</c>, <c>integer</c>, <c>number</c>, <c>boolean</c> or <c>array</c>.</summary>
    public string Type { get; }

    /// <summary>
    /// JSON Schema <c>format</c> annotation, or <see langword="null"/> when the value carries none.
    /// Deliberately limited to <c>date-time</c>: the Gemini API rejects every other format on a
    /// string ("only 'enum' and 'date-time' are supported for STRING type"), so emitting a wider
    /// set would turn a valid tool into a 400 on one of the five supported providers.
    /// </summary>
    public string? Format { get; }

    /// <summary>The permitted values when the source type is an enum, otherwise <see langword="null"/>.</summary>
    public IReadOnlyList<string>? EnumValues { get; }

    /// <summary>The element schema when <see cref="Type"/> is <c>array</c>, otherwise <see langword="null"/>.</summary>
    public JsonSchemaNode? Items { get; }

    public JsonSchemaNode(
        string type,
        string? format = null,
        IReadOnlyList<string>? enumValues = null,
        JsonSchemaNode? items = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        Type = type;
        Format = format;
        EnumValues = enumValues;
        Items = items;
    }
}

namespace PolyAI.Tools;

/// <summary>
/// Marks a method as a tool the model can call during function-calling turns.
/// Apply to public methods on any class and register the class with
/// <see cref="ToolRegistry.FromInstance{T}(T)"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class PolyAIToolAttribute : Attribute
{
    /// <summary>Machine-readable tool name. Defaults to the method name.</summary>
    public string? Name { get; }

    /// <summary>Human-readable description shown to the model.</summary>
    public string Description { get; }

    public PolyAIToolAttribute(string description, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Description = description;
        Name = name;
    }
}

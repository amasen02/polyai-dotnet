namespace PolyAI.Tools;

/// <summary>
/// Annotates a parameter of a <see cref="PolyAIToolAttribute"/>-marked method
/// with its description shown to the model. Without this attribute the parameter
/// name is used as-is.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class PolyAIParamAttribute : Attribute
{
    public string Description { get; }

    public PolyAIParamAttribute(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Description = description;
    }
}

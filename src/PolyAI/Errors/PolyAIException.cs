namespace PolyAI.Errors;

/// <summary>Base exception for all PolyAI errors.</summary>
public class PolyAIException : Exception
{
    public PolyAIException(string message) : base(message) { }
    public PolyAIException(string message, Exception innerException) : base(message, innerException) { }
}

namespace PolyAI.Providers.Azure;

/// <summary>Configuration options for the Azure OpenAI provider.</summary>
public sealed class AzureOpenAIOptions
{
    /// <summary>Azure OpenAI API key.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Azure OpenAI resource endpoint (e.g. https://my-resource.openai.azure.com).</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Name of the deployment to target.</summary>
    public string DeploymentName { get; set; } = string.Empty;

    /// <summary>API version to include in the query string.</summary>
    public string ApiVersion { get; set; } = "2024-02-01";
}

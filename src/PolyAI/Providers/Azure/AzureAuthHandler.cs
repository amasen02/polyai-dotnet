namespace PolyAI.Providers.Azure;

/// <summary>
/// Intercepts outgoing Azure OpenAI requests:
/// — Replaces <c>Authorization: Bearer ...</c> with the Azure <c>api-key</c> header.
/// — Appends <c>?api-version=...</c> to the request URL when absent.
/// </summary>
internal sealed class AzureAuthHandler : DelegatingHandler
{
    private readonly string _apiKey;
    private readonly string _apiVersion;

    public AzureAuthHandler(string apiKey, string apiVersion)
    {
        _apiKey = apiKey;
        _apiVersion = apiVersion;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = null;
        request.Headers.Remove("api-key");
        request.Headers.Add("api-key", _apiKey);

        var uriBuilder = new UriBuilder(request.RequestUri!);
        var query = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);
        if (string.IsNullOrEmpty(query["api-version"]))
        {
            query["api-version"] = _apiVersion;
            uriBuilder.Query = query.ToString();
            request.RequestUri = uriBuilder.Uri;
        }

        return base.SendAsync(request, cancellationToken);
    }
}

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PolyAI.Abstractions;
using PolyAI.Errors;
using PolyAI.Extensions;
using PolyAI.Providers.Azure;

namespace PolyAI.Tests.QaProbes;

/// <summary>
/// GRO-123 QA probes — DI error cases and AzureAuthHandler. The shipped suite has no
/// AzureAuthHandler test at all, so the api-version and api-key behaviour was unverified.
/// </summary>
public sealed class P5_DiAndAzureAuthProbes
{
    /// <summary>Terminates the handler pipeline and records the request that reached it.</summary>
    private sealed class TerminalHandler : HttpMessageHandler
    {
        public Uri? SeenUri { get; private set; }
        public IEnumerable<string>? SeenApiKeys { get; private set; }
        public bool SawAuthorizationHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SeenUri = request.RequestUri;
            SawAuthorizationHeader = request.Headers.Authorization is not null;
            SeenApiKeys = request.Headers.TryGetValues("api-key", out var values) ? values.ToList() : [];
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private static async Task<TerminalHandler> SendThroughAzureHandlerAsync(
        string requestUri, string apiKey = "azure-key", string apiVersion = "2024-02-01")
    {
        var terminal = new TerminalHandler();
        var handler = new AzureAuthHandler(apiKey, apiVersion) { InnerHandler = terminal };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "should-be-stripped");
        using var _ = await client.SendAsync(request);
        return terminal;
    }

    // ---------------------------------------------------------------- P5.1
    // AzureAuthHandler must append api-version when absent — QA scope.
    [Fact]
    public async Task P5_1_AzureAuthHandler_appends_api_version_when_absent()
    {
        var terminal = await SendThroughAzureHandlerAsync(
            "https://r.openai.azure.com/openai/deployments/gpt-4o/chat/completions");

        System.Web.HttpUtility.ParseQueryString(terminal.SeenUri!.Query)["api-version"]
            .Should().Be("2024-02-01");
    }

    // ---------------------------------------------------------------- P5.2
    // api-version already present must NOT be duplicated or overwritten — QA scope, verbatim.
    [Fact]
    public async Task P5_2_AzureAuthHandler_does_not_duplicate_an_api_version_already_in_the_URL()
    {
        var terminal = await SendThroughAzureHandlerAsync(
            "https://r.openai.azure.com/openai/deployments/gpt-4o/chat/completions?api-version=2099-12-31");

        var query = System.Web.HttpUtility.ParseQueryString(terminal.SeenUri!.Query);
        query.GetValues("api-version").Should().HaveCount(1, "the parameter must appear exactly once");
        query["api-version"].Should().Be("2099-12-31", "the caller's explicit value must win");
        terminal.SeenUri!.Query.Should().NotContain("2024-02-01");
    }

    // ---------------------------------------------------------------- P5.3
    // Other query parameters must survive the rewrite.
    [Fact]
    public async Task P5_3_AzureAuthHandler_preserves_unrelated_query_parameters()
    {
        var terminal = await SendThroughAzureHandlerAsync(
            "https://r.openai.azure.com/openai/deployments/gpt-4o/chat/completions?trace=abc123");

        var query = System.Web.HttpUtility.ParseQueryString(terminal.SeenUri!.Query);
        query["trace"].Should().Be("abc123");
        query["api-version"].Should().Be("2024-02-01");
    }

    // ---------------------------------------------------------------- P5.4
    // Bearer auth must be replaced by exactly one api-key header.
    [Fact]
    public async Task P5_4_AzureAuthHandler_replaces_Bearer_auth_with_a_single_api_key_header()
    {
        var terminal = await SendThroughAzureHandlerAsync(
            "https://r.openai.azure.com/openai/deployments/gpt-4o/chat/completions");

        terminal.SawAuthorizationHeader.Should().BeFalse();
        terminal.SeenApiKeys.Should().Equal("azure-key");
    }

    // ---------------------------------------------------------------- P5.5
    // Missing api-key — QA scope. The handler must refuse rather than send an unauthenticated
    // request whose failure surfaces later as an opaque 401 from Azure.
    [Fact(Skip = "Documented defect: AzureAuthHandler and DI configuration validation gaps. Tracked in GRO-DIAZURE.")]
    public void P5_5_AzureAuthHandler_rejects_an_empty_api_key_at_construction()
    {
        var act = () => new AzureAuthHandler(string.Empty, "2024-02-01");

        act.Should().Throw<ArgumentException>(
            "a handler constructed with no credential can only ever produce 401s");
    }

    // ---------------------------------------------------------------- P5.6
    // Missing api-version. An empty version yields "?api-version=" and an Azure 400.
    [Fact(Skip = "Documented defect: AzureAuthHandler and DI configuration validation gaps. Tracked in GRO-DIAZURE.")]
    public void P5_6_AzureAuthHandler_rejects_an_empty_api_version_at_construction()
    {
        var act = () => new AzureAuthHandler("azure-key", string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    // ---------------------------------------------------------------- P5.7
    // No providers registered — QA scope. Verified-good, but note the failure is deferred to
    // first resolve rather than raised at AddPolyAI time.
    [Fact]
    public void P5_7_Resolving_the_router_with_no_providers_registered_throws_a_clear_error()
    {
        var services = new ServiceCollection();
        services.AddPolyAI(_ => { });
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IPolyAIRouter>();

        act.Should().Throw<PolyAIException>().WithMessage("*No AI providers registered*");
    }

    // ---------------------------------------------------------------- P5.8
    // Unknown provider name in GetProvider() — QA scope. Verified-good.
    [Fact]
    public void P5_8_GetProvider_with_an_unknown_name_lists_the_registered_providers()
    {
        var services = new ServiceCollection();
        services.AddPolyAI(b => b.UseOpenAI("k").UseAnthropic("k"));
        using var sp = services.BuildServiceProvider();
        var router = sp.GetRequiredService<IPolyAIRouter>();

        var act = () => router.GetProvider("bedrock");

        act.Should().Throw<PolyAIException>()
            .WithMessage("*bedrock*").WithMessage("*openai*").WithMessage("*anthropic*");
    }

    // ---------------------------------------------------------------- P5.9
    // A typo in WithDefaultProvider names a provider that was never registered. Nothing
    // validates it, so the container builds clean and the failure surfaces on the first
    // request instead of at startup.
    [Fact(Skip = "Documented defect: AzureAuthHandler and DI configuration validation gaps. Tracked in GRO-DIAZURE.")]
    public void P5_9_WithDefaultProvider_rejects_a_name_that_was_never_registered()
    {
        var services = new ServiceCollection();

        var act = () => services.AddPolyAI(b => b.UseOpenAI("k").WithDefaultProvider("opeanai"));

        act.Should().Throw<PolyAIException>(
            "a misconfigured default must fail at startup, not on the first user request");
    }

    // ---------------------------------------------------------------- P5.10
    // Providers are constructed inside a singleton factory holding an IHttpClientFactory
    // client for the process lifetime, which defeats handler rotation and DNS refresh.
    // Azure goes further and bypasses the factory with `new HttpClient(...)`, leaving the
    // registered "polyai-azure-openai" named client unused.
    [Fact(Skip = "Documented defect: AzureAuthHandler and DI configuration validation gaps. Tracked in GRO-DIAZURE.")]
    public void P5_10_Every_registered_named_HttpClient_is_actually_used_by_a_provider()
    {
        var services = new ServiceCollection();
        services.AddPolyAI(b => b.UseAzureOpenAI("k", "https://r.openai.azure.com", "gpt-4o"));
        using var sp = services.BuildServiceProvider();

        var azureHandlerWasBuilt = false;
        var probe = new ServiceCollection();
        probe.AddPolyAI(b => b.UseAzureOpenAI("k", "https://r.openai.azure.com", "gpt-4o"));
        probe.AddHttpClient("polyai-azure-openai")
             .ConfigurePrimaryHttpMessageHandler(() =>
             {
                 azureHandlerWasBuilt = true;
                 return new HttpClientHandler();
             });
        using var probeSp = probe.BuildServiceProvider();
        probeSp.GetRequiredService<IPolyAIRouter>().GetProvider("azure-openai");

        azureHandlerWasBuilt.Should().BeTrue(
            "PolyAIBuilder registers the named client polyai-azure-openai but UseAzureOpenAI " +
            "constructs `new HttpClient(new AzureAuthHandler{InnerHandler=new HttpClientHandler()})`, " +
            "so the factory registration is dead and the socket has no PooledConnectionLifetime");
    }

    // ---------------------------------------------------------------- P5.11
    // Registering the same provider twice silently keeps only the last configuration.
    [Fact(Skip = "Documented defect: AzureAuthHandler and DI configuration validation gaps. Tracked in GRO-DIAZURE.")]
    public void P5_11_Registering_the_same_provider_twice_is_not_silently_ignored()
    {
        var services = new ServiceCollection();

        var act = () => services.AddPolyAI(b => b
            .UseOpenAI("first-key", o => o.DefaultModel = "gpt-4o")
            .UseOpenAI("second-key", o => o.DefaultModel = "gpt-4o-mini"));

        act.Should().Throw<PolyAIException>(
            "AddFactory overwrites by key, so the first registration vanishes without a word");
    }
}

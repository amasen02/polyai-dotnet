using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PolyAI.Abstractions;
using PolyAI.Errors;
using PolyAI.Extensions;
using PolyAI.Providers.Azure;

namespace PolyAI.Tests.Extensions;

/// <summary>
/// GRO-290 — the Azure provider must resolve its <see cref="HttpClient"/> from
/// <see cref="IHttpClientFactory"/> through the "polyai-azure-openai" named client, like every
/// other provider. It previously constructed its own client, which made the named registration
/// dead: everything a consumer attached to that name (a resilience or retry handler, proxy or
/// certificate configuration, a test double) was silently ignored.
/// </summary>
public sealed class AzureHttpClientFactoryTests
{
    private const string NamedClient = "polyai-azure-openai";
    private const string ApiKey = "azure-key";
    private const string DeploymentName = "gpt-4o-mini";

    // A reserved TLD (RFC 2606). Nothing here should ever reach a socket; if the wiring regresses,
    // the request fails fast on name resolution instead of hanging on a real endpoint.
    private const string Endpoint = "https://polyai-gro290.invalid";

    private const string ChatResponseJson = """
    {
      "choices": [{ "message": { "role": "assistant", "content": "ok" }, "finish_reason": "stop" }],
      "model": "gpt-4o-mini",
      "usage": { "prompt_tokens": 1, "completion_tokens": 1 }
    }
    """;

    /// <summary>A handler a consumer attaches to the named client. Short-circuits the pipeline.</summary>
    private sealed class ConsumerHandler : DelegatingHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(JsonResponse());
        }
    }

    /// <summary>Stands in for the socket and records what the wire would have seen.</summary>
    private sealed class CapturingPrimaryHandler : HttpMessageHandler
    {
        public Uri? SeenUri { get; private set; }
        public IEnumerable<string> SeenApiKeys { get; private set; } = [];
        public bool SawAuthorizationHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SeenUri = request.RequestUri;
            SawAuthorizationHeader = request.Headers.Authorization is not null;
            SeenApiKeys = request.Headers.TryGetValues("api-key", out var values) ? values.ToList() : [];
            return Task.FromResult(JsonResponse());
        }
    }

    private static HttpResponseMessage JsonResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(ChatResponseJson, Encoding.UTF8, "application/json")
    };

    private static IPolyAIClient BuildAzureProvider(IServiceCollection services, out ServiceProvider serviceProvider)
    {
        serviceProvider = services.BuildServiceProvider();
        return serviceProvider.GetRequiredService<IPolyAIRouter>().GetProvider("azure-openai");
    }

    // ------------------------------------------------------------------------------------------
    // The defect itself: a handler attached to the named client must be in the path of a real call.
    [Fact]
    public async Task A_handler_attached_to_the_named_client_is_used_by_the_Azure_provider()
    {
        var consumerHandler = new ConsumerHandler();
        var services = new ServiceCollection();
        services.AddPolyAI(b => b.UseAzureOpenAI(ApiKey, Endpoint, DeploymentName));
        services.AddHttpClient(NamedClient).AddHttpMessageHandler(() => consumerHandler);

        var provider = BuildAzureProvider(services, out var serviceProvider);
        using (serviceProvider)
        {
            var response = await provider.ChatAsync([ChatMessage.User("hi")]);

            consumerHandler.CallCount.Should().Be(1,
                "the provider must send through the polyai-azure-openai named client, so that "
                + "resilience policies and other handlers a consumer attaches to it actually apply");
            response.Content.Should().Be("ok");
        }
    }

    // ------------------------------------------------------------------------------------------
    // The guard: moving the client to the factory must not drop Azure's authentication. Azure
    // authenticates with an `api-key` header plus an `api-version` query parameter, and the
    // underlying OpenAI provider sets Bearer auth that must still be stripped. If this regresses,
    // every Azure request goes out unauthenticated and only fails at the remote endpoint.
    [Fact]
    public async Task Azure_authentication_is_still_applied_when_the_client_comes_from_the_factory()
    {
        var primaryHandler = new CapturingPrimaryHandler();
        var services = new ServiceCollection();
        services.AddPolyAI(b => b.UseAzureOpenAI(ApiKey, Endpoint, DeploymentName));
        services.AddHttpClient(NamedClient).ConfigurePrimaryHttpMessageHandler(() => primaryHandler);

        var provider = BuildAzureProvider(services, out var serviceProvider);
        using (serviceProvider)
        {
            await provider.ChatAsync([ChatMessage.User("hi")]);
        }

        primaryHandler.SeenUri.Should().NotBeNull("the request must reach the factory-built pipeline");
        // Note the array form: Equal(params string[]) would swallow the reason as an expected element.
        primaryHandler.SeenApiKeys.Should().Equal([ApiKey],
            "the Azure api-key header is applied by AzureAuthHandler, which must still be in the pipeline");
        primaryHandler.SawAuthorizationHeader.Should().BeFalse(
            "the inherited OpenAI Bearer header must still be stripped");
        System.Web.HttpUtility.ParseQueryString(primaryHandler.SeenUri!.Query)["api-version"]
            .Should().Be(new AzureOpenAIOptions().ApiVersion,
                "Azure rejects a request that carries no api-version");
    }

    // ------------------------------------------------------------------------------------------
    // The invariant this fix must not break: AddPolyAI validates the configuration before it
    // registers anything, so a rejected configuration leaves the caller's container untouched.
    // Attaching the Azure handler inside UseAzureOpenAI rather than Build() would violate this.
    [Fact]
    public void A_rejected_configuration_registers_nothing_in_the_service_collection()
    {
        var services = new ServiceCollection();

        var act = () => services.AddPolyAI(b => b
            .UseAzureOpenAI(ApiKey, Endpoint, DeploymentName)
            .WithDefaultProvider("azure-openai-typo"));

        act.Should().Throw<PolyAIException>();
        services.Should().BeEmpty(
            "AddPolyAI rejects an unusable configuration before touching the service collection, "
            + "so a caller that catches the error is not left with half-registered services");
    }
}

using FluentAssertions;
using PolyAI.Abstractions;
using PolyAI.Errors;
using PolyAI.Providers.Azure;
using PolyAI.Tests.Fakes;

namespace PolyAI.Tests.Providers;

public sealed class AzureOpenAIProviderTests
{
    private static AzureOpenAIProvider BuildProvider(FakeHttpMessageHandler fakeHandler)
    {
        // Wire the fake handler into the Azure auth pipeline so requests are intercepted
        var authHandler = new AzureAuthHandler("test-azure-key", "2024-02-01")
        {
            InnerHandler = fakeHandler
        };
        var http = new HttpClient(authHandler);

        return new AzureOpenAIProvider(http, new AzureOpenAIOptions
        {
            ApiKey = "test-azure-key",
            Endpoint = "https://my-resource.openai.azure.com",
            DeploymentName = "gpt-4o-mini",
        });
    }

    [Fact]
    public async Task ChatAsync_returns_content_from_valid_openai_response()
    {
        const string json = """
        {
          "choices": [{ "message": { "role": "assistant", "content": "Hello from Azure!" }, "finish_reason": "stop" }],
          "model": "gpt-4o-mini",
          "usage": { "prompt_tokens": 10, "completion_tokens": 5 }
        }
        """;

        var provider = BuildProvider(FakeHttpMessageHandler.WithJson(json));
        var result = await provider.ChatAsync([ChatMessage.User("Hello")]);

        result.Content.Should().Be("Hello from Azure!");
        provider.ProviderName.Should().Be("azure-openai");
    }

    [Fact]
    public void Constructor_throws_when_api_key_is_empty()
    {
        var act = () => new AzureOpenAIProvider(new HttpClient(), new AzureOpenAIOptions
        {
            ApiKey = string.Empty,
            Endpoint = "https://my-resource.openai.azure.com",
            DeploymentName = "gpt-4o-mini",
        });
        act.Should().Throw<PolyAIException>().WithMessage("*API key*");
    }

    [Fact]
    public void Constructor_throws_when_endpoint_is_empty()
    {
        var act = () => new AzureOpenAIProvider(new HttpClient(), new AzureOpenAIOptions
        {
            ApiKey = "key",
            Endpoint = string.Empty,
            DeploymentName = "gpt-4o-mini",
        });
        act.Should().Throw<PolyAIException>().WithMessage("*endpoint*");
    }

    [Fact]
    public void Constructor_throws_when_deployment_is_empty()
    {
        var act = () => new AzureOpenAIProvider(new HttpClient(), new AzureOpenAIOptions
        {
            ApiKey = "key",
            Endpoint = "https://my-resource.openai.azure.com",
            DeploymentName = string.Empty,
        });
        act.Should().Throw<PolyAIException>().WithMessage("*deployment*");
    }
}

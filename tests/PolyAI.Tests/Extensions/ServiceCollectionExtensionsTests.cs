using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PolyAI.Abstractions;
using PolyAI.Errors;
using PolyAI.Extensions;

namespace PolyAI.Tests.Extensions;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPolyAI_registers_IPolyAIRouter()
    {
        var services = new ServiceCollection();
        services.AddPolyAI(o => o.UseOpenAI("test-key"));

        var provider = services.BuildServiceProvider();
        var router = provider.GetService<IPolyAIRouter>();

        router.Should().NotBeNull();
    }

    [Fact]
    public void AddPolyAI_registers_default_IPolyAIClient()
    {
        var services = new ServiceCollection();
        services.AddPolyAI(o => o.UseOpenAI("test-key"));

        var provider = services.BuildServiceProvider();
        var client = provider.GetService<IPolyAIClient>();

        client.Should().NotBeNull();
        client!.ProviderName.Should().Be("openai");
    }

    [Fact]
    public void AddPolyAI_first_registered_provider_becomes_default()
    {
        var services = new ServiceCollection();
        services.AddPolyAI(o => o
            .UseOpenAI("key1")
            .UseAnthropic("sk-ant-key"));

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IPolyAIClient>();

        // OpenAI was registered first, so it's the default
        client.ProviderName.Should().Be("openai");
    }

    [Fact]
    public void AddPolyAI_WithDefaultProvider_overrides_default()
    {
        var services = new ServiceCollection();
        services.AddPolyAI(o => o
            .UseOpenAI("key1")
            .UseAnthropic("sk-ant-key")
            .WithDefaultProvider("anthropic"));

        var provider = services.BuildServiceProvider();
        var router = provider.GetRequiredService<IPolyAIRouter>();

        router.GetProvider().ProviderName.Should().Be("anthropic");
    }

    [Fact]
    public void IPolyAIRouter_GetProvider_throws_for_unknown_provider()
    {
        var services = new ServiceCollection();
        services.AddPolyAI(o => o.UseOpenAI("key1"));

        var provider = services.BuildServiceProvider();
        var router = provider.GetRequiredService<IPolyAIRouter>();

        var act = () => router.GetProvider("unknown-provider");
        act.Should().Throw<PolyAIException>().WithMessage("*No provider registered*unknown-provider*");
    }

    [Fact]
    public void IPolyAIRouter_RegisteredProviders_lists_all()
    {
        var services = new ServiceCollection();
        services.AddPolyAI(o => o
            .UseOpenAI("k1")
            .UseAnthropic("sk-ant-k2")
            .UseOllama());

        var provider = services.BuildServiceProvider();
        var router = provider.GetRequiredService<IPolyAIRouter>();

        router.RegisteredProviders.Should().BeEquivalentTo("openai", "anthropic", "ollama");
    }

    [Fact]
    public void AddPolyAI_throws_when_no_providers_configured()
    {
        var services = new ServiceCollection();
        services.AddPolyAI(_ => { }); // no providers registered

        var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IPolyAIRouter>();
        act.Should().Throw<Exception>().WithMessage("*No AI providers registered*");
    }
}

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
    public void AddPolyAI_throws_at_registration_when_no_providers_configured()
    {
        var services = new ServiceCollection();

        var act = () => services.AddPolyAI(_ => { }); // no providers registered

        act.Should().Throw<PolyAIException>("a container with no providers can never serve a request")
            .WithMessage("*No AI providers registered*");
    }

    [Fact]
    public void AddPolyAI_throws_at_registration_when_the_default_provider_was_never_registered()
    {
        var services = new ServiceCollection();

        var act = () => services.AddPolyAI(o => o
            .UseOpenAI("key1")
            .WithDefaultProvider("opemai")); // typo

        act.Should().Throw<PolyAIException>("a typo must fail at startup, not on the first user request")
            .WithMessage("*opemai*");
    }

    [Fact]
    public void AddPolyAI_unknown_default_error_lists_the_registered_providers()
    {
        var services = new ServiceCollection();

        var act = () => services.AddPolyAI(o => o
            .UseOpenAI("key1")
            .UseAnthropic("sk-ant-key")
            .WithDefaultProvider("opemai"));

        act.Should().Throw<PolyAIException>()
            .WithMessage("*openai*").WithMessage("*anthropic*");
    }

    [Fact]
    public void AddPolyAI_allows_the_default_to_be_named_before_the_provider_it_refers_to()
    {
        var services = new ServiceCollection();

        // The fluent API permits naming the default first; validation must be deferred to
        // Build() rather than evaluated inside WithDefaultProvider.
        services.AddPolyAI(o => o
            .WithDefaultProvider("anthropic")
            .UseOpenAI("key1")
            .UseAnthropic("sk-ant-key"));

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IPolyAIRouter>().GetProvider().ProviderName.Should().Be("anthropic");
    }

    [Fact]
    public void AddPolyAI_matches_the_default_provider_name_case_insensitively()
    {
        var services = new ServiceCollection();

        services.AddPolyAI(o => o
            .UseOpenAI("key1")
            .UseAnthropic("sk-ant-key")
            .WithDefaultProvider("AnThRoPiC"));

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IPolyAIRouter>().GetProvider().ProviderName.Should().Be("anthropic");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithDefaultProvider_rejects_a_missing_name(string? providerName)
    {
        var services = new ServiceCollection();

        // A missing configuration key binds to null/empty here. Without this guard the name is
        // stored verbatim and Build() silently falls back to whichever provider was registered
        // first, so the application runs against a provider nobody chose.
        var act = () => services.AddPolyAI(o => o
            .UseOpenAI("key1")
            .UseAnthropic("sk-ant-key")
            .WithDefaultProvider(providerName!));

        act.Should().Throw<ArgumentException>("an absent default must be reported, not guessed");
    }
}

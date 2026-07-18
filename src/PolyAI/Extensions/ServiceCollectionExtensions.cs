using Microsoft.Extensions.DependencyInjection;

namespace PolyAI.Extensions;

/// <summary>
/// ASP.NET Core DI registration extensions for PolyAI.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds PolyAI services to the dependency injection container.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddPolyAI(o => o
    ///     .UseAnthropic(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")!)
    ///     .UseOpenAI(Environment.GetEnvironmentVariable("OPENAI_API_KEY")!));
    /// </code>
    /// </example>
    public static IServiceCollection AddPolyAI(
        this IServiceCollection services,
        Action<PolyAIBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new PolyAIBuilder(services);
        configure(builder);
        builder.Build();

        return services;
    }
}

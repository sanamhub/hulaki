using System;
using Hulaki;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Returned by <see cref="HulakiServiceCollectionExtensions.AddHulaki"/>. Provider packages add
/// their channels through extension methods on it, such as <c>AddTelegram</c>.
/// </summary>
public interface IHulakiBuilder
{
    /// <summary>The service collection Hulaki is registered in.</summary>
    IServiceCollection Services { get; }
}

/// <summary>Registration helpers for provider packages.</summary>
public static class HulakiBuilderExtensions
{
    /// <summary>
    /// Registers a channel as a singleton, keyed by <paramref name="name"/> and as an
    /// <see cref="IChannel"/> that <see cref="HulakiClient"/> picks up.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="name">The channel name. Targets use it; it must be unique.</param>
    /// <param name="factory">Creates the channel. Called once, on first use.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="factory"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    public static IHulakiBuilder AddChannel(this IHulakiBuilder builder, string name, Func<IServiceProvider, IChannel> factory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        builder.Services.AddKeyedSingleton(name, (provider, _) => factory(provider));
        builder.Services.AddSingleton(provider => provider.GetRequiredKeyedService<IChannel>(name));
        return builder;
    }
}

using System;
using Hulaki;
using Hulaki.Extensions.DependencyInjection;
using Hulaki.Idempotency;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers Hulaki in an <see cref="IServiceCollection"/> (ADR-0013).</summary>
public static class HulakiServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="HulakiClient"/> as a singleton over every registered
    /// <see cref="IChannel"/>, and an <see cref="InMemoryIdempotencyStore"/> unless an
    /// <see cref="IIdempotencyStore"/> is already registered. Configure the client with
    /// <c>services.Configure&lt;HulakiClientOptions&gt;(...)</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>A builder for adding channels.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <remarks>
    /// Do not add a retry or resilience handler to Hulaki's <see cref="System.Net.Http.HttpClient"/>s:
    /// it would resend requests the platform may already have acted on (ADR-0005).
    /// </remarks>
    public static IHulakiBuilder AddHulaki(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions();
        services.TryAddSingleton<IIdempotencyStore>(provider => new InMemoryIdempotencyStore(provider.GetService<TimeProvider>()));
        services.TryAddSingleton(provider => new HulakiClient(
            provider.GetServices<IChannel>(),
            provider.GetRequiredService<IOptions<HulakiClientOptions>>().Value,
            provider.GetRequiredService<IIdempotencyStore>(),
            provider.GetService<ILogger<HulakiClient>>()));
        return new HulakiBuilder(services);
    }
}

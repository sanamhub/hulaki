using System;
using Hulaki.Bluesky;
using Hulaki.Credentials;
using Hulaki.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Adds Bluesky channels to Hulaki (ADR-0013).</summary>
public static class BlueskyHulakiBuilderExtensions
{
    /// <summary>
    /// Adds a Bluesky channel named <paramref name="name"/> with options bound from
    /// <paramref name="section"/>, for example <c>{ "Identifier": "alerts.example.org", "AppPassword": "..." }</c>. The options are
    /// validated when the host starts. An <see cref="ICredentialStore"/> registered in the
    /// container keeps the session.
    /// </summary>
    /// <param name="builder">The Hulaki builder.</param>
    /// <param name="name">The channel name, unique across channels.</param>
    /// <param name="section">The configuration section holding <see cref="BlueskyChannelOptions"/>.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="section"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    /// <exception cref="FormatException">The section's <c>Enabled</c> is not true or false.</exception>
    /// <remarks>Nothing is added when the section's <c>Enabled</c> is false.</remarks>
    public static IHulakiBuilder AddBluesky(this IHulakiBuilder builder, string name, IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(section);
        if (!ProviderRegistration.IsEnabled(section))
        {
            return builder;
        }

        return AddBlueskyCore(builder, name, options => options.Configure(o => BlueskyChannelSettings.Apply(section, o)));
    }

    /// <summary>
    /// Adds a Bluesky channel named <paramref name="name"/> configured in code. The options are
    /// validated when the host starts.
    /// </summary>
    /// <param name="builder">The Hulaki builder.</param>
    /// <param name="name">The channel name, unique across channels.</param>
    /// <param name="configure">Sets the options, at least the identifier and app password.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    public static IHulakiBuilder AddBluesky(this IHulakiBuilder builder, string name, Action<BlueskyChannelOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        return AddBlueskyCore(builder, name, options => options.Configure(configure));
    }

    private static IHulakiBuilder AddBlueskyCore(IHulakiBuilder builder, string name, Action<OptionsBuilder<BlueskyChannelOptions>> configure) =>
        ProviderRegistration.AddHttpChannel<BlueskyChannelOptions, BlueskyChannelOptionsValidator>(
            builder, name, configure, (channel, http, options, provider) =>
            {
                // A store registered in the container keeps the session across restarts and instances.
                options.CredentialStore ??= provider.GetService<ICredentialStore>();
                return new BlueskyChannel(channel, http, options);
            });
}

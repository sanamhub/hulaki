using System;
using Hulaki.Mastodon;
using Hulaki.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Adds Mastodon channels to Hulaki (ADR-0013).</summary>
public static class MastodonHulakiBuilderExtensions
{
    /// <summary>
    /// Adds a Mastodon channel named <paramref name="name"/> with options bound from
    /// <paramref name="section"/>, for example <c>{ "InstanceUrl": "https://mastodon.social/", "AccessToken": "..." }</c>. The options are
    /// validated when the host starts.
    /// </summary>
    /// <param name="builder">The Hulaki builder.</param>
    /// <param name="name">The channel name, unique across channels.</param>
    /// <param name="section">The configuration section holding <see cref="MastodonChannelOptions"/>.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="section"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    /// <exception cref="FormatException">The section's <c>Enabled</c> is not true or false.</exception>
    /// <remarks>Nothing is added when the section's <c>Enabled</c> is false.</remarks>
    public static IHulakiBuilder AddMastodon(this IHulakiBuilder builder, string name, IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(section);
        if (!ProviderRegistration.IsEnabled(section))
        {
            return builder;
        }

        return AddMastodonCore(builder, name, options => options.Configure(o => MastodonChannelSettings.Apply(section, o)));
    }

    /// <summary>
    /// Adds a Mastodon channel named <paramref name="name"/> configured in code. The options are
    /// validated when the host starts.
    /// </summary>
    /// <param name="builder">The Hulaki builder.</param>
    /// <param name="name">The channel name, unique across channels.</param>
    /// <param name="configure">Sets the options, at least the instance URL and access token.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    public static IHulakiBuilder AddMastodon(this IHulakiBuilder builder, string name, Action<MastodonChannelOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        return AddMastodonCore(builder, name, options => options.Configure(configure));
    }

    private static IHulakiBuilder AddMastodonCore(IHulakiBuilder builder, string name, Action<OptionsBuilder<MastodonChannelOptions>> configure) =>
        ProviderRegistration.AddHttpChannel<MastodonChannelOptions, MastodonChannelOptionsValidator>(
            builder, name, configure, (channel, http, options, _) => new MastodonChannel(channel, http, options));
}

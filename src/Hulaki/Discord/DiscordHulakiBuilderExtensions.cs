using System;
using Hulaki.Discord;
using Hulaki.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Adds Discord webhook channels to Hulaki (ADR-0013).</summary>
public static class DiscordHulakiBuilderExtensions
{
    /// <summary>
    /// Adds a Discord channel named <paramref name="name"/> with options bound from
    /// <paramref name="section"/>, for example <c>{ "WebhookUrl": "..." }</c>. The options are
    /// validated when the host starts.
    /// </summary>
    /// <param name="builder">The Hulaki builder.</param>
    /// <param name="name">The channel name, unique across channels.</param>
    /// <param name="section">The configuration section holding <see cref="DiscordChannelOptions"/>.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="section"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    /// <exception cref="FormatException">The section's <c>Enabled</c> is not true or false.</exception>
    /// <remarks>Nothing is added when the section's <c>Enabled</c> is false.</remarks>
    public static IHulakiBuilder AddDiscord(this IHulakiBuilder builder, string name, IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(section);
        if (!ProviderRegistration.IsEnabled(section))
        {
            return builder;
        }

        return AddDiscordCore(builder, name, options => options.Configure(o => DiscordChannelSettings.Apply(section, o)));
    }

    /// <summary>
    /// Adds a Discord channel named <paramref name="name"/> configured in code. The options are
    /// validated when the host starts.
    /// </summary>
    /// <param name="builder">The Hulaki builder.</param>
    /// <param name="name">The channel name, unique across channels.</param>
    /// <param name="configure">Sets the options, at least <see cref="DiscordChannelOptions.WebhookUrl"/>.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    public static IHulakiBuilder AddDiscord(this IHulakiBuilder builder, string name, Action<DiscordChannelOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        return AddDiscordCore(builder, name, options => options.Configure(configure));
    }

    private static IHulakiBuilder AddDiscordCore(IHulakiBuilder builder, string name, Action<OptionsBuilder<DiscordChannelOptions>> configure) =>
        ProviderRegistration.AddHttpChannel<DiscordChannelOptions, DiscordChannelOptionsValidator>(
            builder, name, configure, (channel, http, options, _) => new DiscordChannel(channel, http, options));
}

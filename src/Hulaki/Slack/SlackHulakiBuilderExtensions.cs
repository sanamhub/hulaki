using System;
using Hulaki.Providers;
using Hulaki.Slack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Adds Slack incoming webhook channels to Hulaki (ADR-0013).</summary>
public static class SlackHulakiBuilderExtensions
{
    /// <summary>
    /// Adds a Slack channel named <paramref name="name"/> with options bound from
    /// <paramref name="section"/>, for example <c>{ "WebhookUrl": "..." }</c>. The options are
    /// validated when the host starts.
    /// </summary>
    /// <param name="builder">The Hulaki builder.</param>
    /// <param name="name">The channel name, unique across channels.</param>
    /// <param name="section">The configuration section holding <see cref="SlackChannelOptions"/>.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="section"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    public static IHulakiBuilder AddSlack(this IHulakiBuilder builder, string name, IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(section);
        return AddSlackCore(builder, name, options => options.Configure(o => SlackChannelSettings.Apply(section, o)));
    }

    /// <summary>
    /// Adds a Slack channel named <paramref name="name"/> configured in code. The options are
    /// validated when the host starts.
    /// </summary>
    /// <param name="builder">The Hulaki builder.</param>
    /// <param name="name">The channel name, unique across channels.</param>
    /// <param name="configure">Sets the options, at least <see cref="SlackChannelOptions.WebhookUrl"/>.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    public static IHulakiBuilder AddSlack(this IHulakiBuilder builder, string name, Action<SlackChannelOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        return AddSlackCore(builder, name, options => options.Configure(configure));
    }

    private static IHulakiBuilder AddSlackCore(IHulakiBuilder builder, string name, Action<OptionsBuilder<SlackChannelOptions>> configure) =>
        ProviderRegistration.AddHttpChannel<SlackChannelOptions, SlackChannelOptionsValidator>(
            builder, name, configure, (channel, http, options, _) => new SlackChannel(channel, http, options));
}

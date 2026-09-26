using System;
using Hulaki.Providers;
using Hulaki.Webhook;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Adds generic webhook channels to Hulaki (ADR-0013).</summary>
public static class WebhookHulakiBuilderExtensions
{
    /// <summary>
    /// Adds a webhook channel named <paramref name="name"/> with options bound from
    /// <paramref name="section"/>, for example <c>{ "Url": "...", "Secret": "..." }</c>. The options
    /// are validated when the host starts.
    /// </summary>
    /// <param name="builder">The Hulaki builder.</param>
    /// <param name="name">The channel name, unique across channels.</param>
    /// <param name="section">The configuration section holding <see cref="WebhookChannelOptions"/>.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="section"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    public static IHulakiBuilder AddWebhook(this IHulakiBuilder builder, string name, IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(section);
        return AddWebhookCore(builder, name, options => options.Configure(o => WebhookChannelSettings.Apply(section, o)));
    }

    /// <summary>
    /// Adds a webhook channel named <paramref name="name"/> configured in code. The options are
    /// validated when the host starts.
    /// </summary>
    /// <param name="builder">The Hulaki builder.</param>
    /// <param name="name">The channel name, unique across channels.</param>
    /// <param name="configure">Sets the options, at least <see cref="WebhookChannelOptions.Url"/>.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    public static IHulakiBuilder AddWebhook(this IHulakiBuilder builder, string name, Action<WebhookChannelOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        return AddWebhookCore(builder, name, options => options.Configure(configure));
    }

    private static IHulakiBuilder AddWebhookCore(IHulakiBuilder builder, string name, Action<OptionsBuilder<WebhookChannelOptions>> configure) =>
        ProviderRegistration.AddHttpChannel<WebhookChannelOptions, WebhookChannelOptionsValidator>(
            builder, name, configure, (channel, http, options, _) => new WebhookChannel(channel, http, options));
}

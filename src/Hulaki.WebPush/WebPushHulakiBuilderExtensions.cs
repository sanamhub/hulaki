using System;
using Hulaki.Providers;
using Hulaki.WebPush;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Adds Web Push channels to Hulaki (ADR-0013).</summary>
public static class WebPushHulakiBuilderExtensions
{
    /// <summary>
    /// Adds a Web Push channel named <paramref name="name"/> with options bound from
    /// <paramref name="section"/>, for example
    /// <c>{ "VapidPublicKey": "...", "VapidPrivateKey": "...", "VapidSubject": "mailto:ops@example.org" }</c>.
    /// The options are validated when the host starts.
    /// </summary>
    /// <param name="builder">The Hulaki builder.</param>
    /// <param name="name">The channel name, unique across channels.</param>
    /// <param name="section">The configuration section holding <see cref="WebPushChannelOptions"/>.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="section"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    public static IHulakiBuilder AddWebPush(this IHulakiBuilder builder, string name, IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(section);
        return AddWebPushCore(builder, name, options => options.Configure(o => WebPushChannelSettings.Apply(section, o)));
    }

    /// <summary>
    /// Adds a Web Push channel named <paramref name="name"/> configured in code. The options are
    /// validated when the host starts.
    /// </summary>
    /// <param name="builder">The Hulaki builder.</param>
    /// <param name="name">The channel name, unique across channels.</param>
    /// <param name="configure">Sets the options, at least the VAPID keys and subject.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    public static IHulakiBuilder AddWebPush(this IHulakiBuilder builder, string name, Action<WebPushChannelOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        return AddWebPushCore(builder, name, options => options.Configure(configure));
    }

    private static IHulakiBuilder AddWebPushCore(IHulakiBuilder builder, string name, Action<OptionsBuilder<WebPushChannelOptions>> configure) =>
        ProviderRegistration.AddHttpChannel<WebPushChannelOptions, WebPushChannelOptionsValidator>(
            builder, name, configure, (channel, http, options, _) => new WebPushChannel(channel, http, options));
}

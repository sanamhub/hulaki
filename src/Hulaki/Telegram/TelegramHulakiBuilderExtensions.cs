using System;
using Hulaki.Providers;
using Hulaki.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Adds Telegram channels to Hulaki (ADR-0013).</summary>
public static class TelegramHulakiBuilderExtensions
{
    /// <summary>
    /// Adds a Telegram channel named <paramref name="name"/> with options bound from
    /// <paramref name="section"/>, for example <c>{ "BotToken": "..." }</c>. The options are
    /// validated when the host starts.
    /// </summary>
    /// <param name="builder">The Hulaki builder.</param>
    /// <param name="name">The channel name, unique across channels.</param>
    /// <param name="section">The configuration section holding <see cref="TelegramChannelOptions"/>.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="section"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    public static IHulakiBuilder AddTelegram(this IHulakiBuilder builder, string name, IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(section);
        return AddTelegramCore(builder, name, options => options.Configure(o => TelegramChannelSettings.Apply(section, o)));
    }

    /// <summary>
    /// Adds a Telegram channel named <paramref name="name"/> configured in code. The options are
    /// validated when the host starts.
    /// </summary>
    /// <param name="builder">The Hulaki builder.</param>
    /// <param name="name">The channel name, unique across channels.</param>
    /// <param name="configure">Sets the options, at least <see cref="TelegramChannelOptions.BotToken"/>.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    public static IHulakiBuilder AddTelegram(this IHulakiBuilder builder, string name, Action<TelegramChannelOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        return AddTelegramCore(builder, name, options => options.Configure(configure));
    }

    private static IHulakiBuilder AddTelegramCore(IHulakiBuilder builder, string name, Action<OptionsBuilder<TelegramChannelOptions>> configure) =>
        ProviderRegistration.AddHttpChannel<TelegramChannelOptions, TelegramChannelOptionsValidator>(
            builder, name, configure, (channel, http, options, _) => new TelegramChannel(channel, http, options));
}

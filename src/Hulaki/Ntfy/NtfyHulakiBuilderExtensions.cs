using System;
using Hulaki.Ntfy;
using Hulaki.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Adds ntfy channels to Hulaki (ADR-0013).</summary>
public static class NtfyHulakiBuilderExtensions
{
    /// <summary>
    /// Adds an ntfy channel named <paramref name="name"/> with options bound from
    /// <paramref name="section"/>, for example <c>{ "BaseAddress": "https://ntfy.sh/" }</c>. The
    /// options are validated when the host starts.
    /// </summary>
    /// <param name="builder">The Hulaki builder.</param>
    /// <param name="name">The channel name, unique across channels.</param>
    /// <param name="section">The configuration section holding <see cref="NtfyChannelOptions"/>.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="section"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    public static IHulakiBuilder AddNtfy(this IHulakiBuilder builder, string name, IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(section);
        return AddNtfyCore(builder, name, options => options.Configure(o => NtfyChannelSettings.Apply(section, o)));
    }

    /// <summary>
    /// Adds an ntfy channel named <paramref name="name"/> configured in code. The options are
    /// validated when the host starts.
    /// </summary>
    /// <param name="builder">The Hulaki builder.</param>
    /// <param name="name">The channel name, unique across channels.</param>
    /// <param name="configure">Sets the options. The defaults publish anonymously to ntfy.sh.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    public static IHulakiBuilder AddNtfy(this IHulakiBuilder builder, string name, Action<NtfyChannelOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        return AddNtfyCore(builder, name, options => options.Configure(configure));
    }

    private static IHulakiBuilder AddNtfyCore(IHulakiBuilder builder, string name, Action<OptionsBuilder<NtfyChannelOptions>> configure) =>
        ProviderRegistration.AddHttpChannel<NtfyChannelOptions, NtfyChannelOptionsValidator>(
            builder, name, configure, (channel, http, options, _) => new NtfyChannel(channel, http, options));
}

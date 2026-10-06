using System;
using Hulaki.Providers;
using Hulaki.Teams;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Adds Microsoft Teams Workflows channels to Hulaki (ADR-0013).</summary>
public static class TeamsHulakiBuilderExtensions
{
    /// <summary>
    /// Adds a Teams channel named <paramref name="name"/> with options bound from
    /// <paramref name="section"/>, for example <c>{ "WorkflowUrl": "..." }</c>. The options are
    /// validated when the host starts.
    /// </summary>
    /// <param name="builder">The Hulaki builder.</param>
    /// <param name="name">The channel name, unique across channels.</param>
    /// <param name="section">The configuration section holding <see cref="TeamsChannelOptions"/>.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="section"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    /// <exception cref="FormatException">The section's <c>Enabled</c> is not true or false.</exception>
    /// <remarks>Nothing is added when the section's <c>Enabled</c> is false.</remarks>
    public static IHulakiBuilder AddTeams(this IHulakiBuilder builder, string name, IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(section);
        if (!ProviderRegistration.IsEnabled(section))
        {
            return builder;
        }

        return AddTeamsCore(builder, name, options => options.Configure(o => TeamsChannelSettings.Apply(section, o)));
    }

    /// <summary>
    /// Adds a Teams channel named <paramref name="name"/> configured in code. The options are
    /// validated when the host starts.
    /// </summary>
    /// <param name="builder">The Hulaki builder.</param>
    /// <param name="name">The channel name, unique across channels.</param>
    /// <param name="configure">Sets the options, at least <see cref="TeamsChannelOptions.WorkflowUrl"/>.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    public static IHulakiBuilder AddTeams(this IHulakiBuilder builder, string name, Action<TeamsChannelOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        return AddTeamsCore(builder, name, options => options.Configure(configure));
    }

    private static IHulakiBuilder AddTeamsCore(IHulakiBuilder builder, string name, Action<OptionsBuilder<TeamsChannelOptions>> configure) =>
        ProviderRegistration.AddHttpChannel<TeamsChannelOptions, TeamsChannelOptionsValidator>(
            builder, name, configure, (channel, http, options, _) => new TeamsChannel(channel, http, options));
}

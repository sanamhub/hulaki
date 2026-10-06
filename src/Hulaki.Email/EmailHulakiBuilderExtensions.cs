using System;
using FreeTierMail;
using Hulaki.Email;
using Hulaki.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Adds email channels to Hulaki (ADR-0013). A channel sends through its own SMTP server, or with
/// <see cref="EmailChannelOptions.UseFreeTierMail"/> through the <see cref="FreeTierMailer"/>
/// registered with <c>AddFreeTierMail()</c> and its providers, such as <c>AddResend()</c>.
/// </summary>
public static class EmailHulakiBuilderExtensions
{
    /// <summary>
    /// Adds an email channel named <paramref name="name"/> with options bound from
    /// <paramref name="section"/>, for example
    /// <c>{ "Host": "smtp.example.org", "Username": "...", "Password": "...", "From": "alerts@example.org" }</c>,
    /// or <c>{ "UseFreeTierMail": true, "From": "alerts@example.org" }</c>.
    /// The options are validated when the host starts.
    /// </summary>
    /// <param name="builder">The Hulaki builder.</param>
    /// <param name="name">The channel name, unique across channels.</param>
    /// <param name="section">The configuration section holding <see cref="EmailChannelOptions"/>.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="section"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    /// <exception cref="FormatException">The section's <c>Enabled</c> is not true or false.</exception>
    /// <remarks>Nothing is added when the section's <c>Enabled</c> is false.</remarks>
    public static IHulakiBuilder AddEmail(this IHulakiBuilder builder, string name, IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(section);
        if (!ProviderRegistration.IsEnabled(section))
        {
            return builder;
        }

        return AddEmailCore(builder, name, options => options.Configure(o => EmailChannelSettings.Apply(section, o)));
    }

    /// <summary>
    /// Adds an email channel named <paramref name="name"/> configured in code. The options are
    /// validated when the host starts.
    /// </summary>
    /// <param name="builder">The Hulaki builder.</param>
    /// <param name="name">The channel name, unique across channels.</param>
    /// <param name="configure">Sets the options, at least <see cref="EmailChannelOptions.From"/>, and <see cref="EmailChannelOptions.Host"/> unless <see cref="EmailChannelOptions.UseFreeTierMail"/> is true.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    public static IHulakiBuilder AddEmail(this IHulakiBuilder builder, string name, Action<EmailChannelOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        return AddEmailCore(builder, name, options => options.Configure(configure));
    }

    // The mailer owns the HTTP clients and connections, so only the options half of the shared
    // registration applies.
    private static IHulakiBuilder AddEmailCore(IHulakiBuilder builder, string name, Action<OptionsBuilder<EmailChannelOptions>> configure)
    {
        ProviderRegistration.AddOptions<EmailChannelOptions, EmailChannelOptionsValidator>(builder, name, configure);
        return builder.AddChannel(name, provider =>
        {
            var options = provider.GetRequiredService<IOptionsMonitor<EmailChannelOptions>>().Get(name);
            if (!options.UseFreeTierMail)
            {
                return new EmailChannel(name, options);
            }

            var mailer = provider.GetService<FreeTierMailer>()
                ?? throw new InvalidOperationException($"Email channel '{name}' has UseFreeTierMail set, but no FreeTierMailer is registered. Call services.AddFreeTierMail() and add a provider.");
            return new EmailChannel(name, mailer, options);
        });
    }
}

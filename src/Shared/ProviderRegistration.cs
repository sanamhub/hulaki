using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using Hulaki.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hulaki.Providers;

/// <summary>
/// The registration every HTTP provider's <c>Add&lt;Name&gt;</c> extensions share (ADR-0013): named
/// options validated at startup, the host's logger factory, a named <see cref="HttpClient"/>
/// without request logging, and the channel as a keyed singleton.
/// </summary>
/// <remarks>Compiled into <c>Hulaki</c> and <c>Hulaki.Email</c> from <c>src/Shared</c>.</remarks>
internal static class ProviderRegistration
{
    public static IHulakiBuilder AddHttpChannel<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TValidator>(
        IHulakiBuilder builder,
        string name,
        Action<OptionsBuilder<TOptions>> configure,
        Func<string, HttpClient, TOptions, IServiceProvider, IChannel> create)
        where TOptions : ChannelOptions
        where TValidator : class, IValidateOptions<TOptions>
    {
        var services = AddOptions<TOptions, TValidator>(builder, name, configure);

        // Webhook URLs and bot tokens travel in the request path, and IHttpClientFactory logs
        // request URIs at Information by default. Without RemoveAllLoggers the secret is in every
        // log (ADR-0015).
        var clientName = "hulaki." + name;
        services.AddHttpClient(clientName, http => http.Timeout = TimeSpan.FromSeconds(30)).RemoveAllLoggers();

        return builder.AddChannel(name, provider => create(
            name,
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName),
            provider.GetRequiredService<IOptionsMonitor<TOptions>>().Get(name),
            provider));
    }

    public static IServiceCollection AddOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TValidator>(
        IHulakiBuilder builder,
        string name,
        Action<OptionsBuilder<TOptions>> configure)
        where TOptions : ChannelOptions
        where TValidator : class, IValidateOptions<TOptions>
    {
        var services = builder.Services;
        var options = services.AddOptions<TOptions>(name).ValidateOnStart();
        configure(options);
        options.PostConfigure<IServiceProvider>((o, provider) => o.LoggerFactory ??= provider.GetService<ILoggerFactory>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TOptions>, TValidator>());
        return services;
    }
}

using System;
using Hulaki.Providers;
using Microsoft.Extensions.Configuration;

namespace Hulaki.Discord;

/// <summary>The configuration-bindable part of <see cref="DiscordChannelOptions"/>; see <see cref="ChannelSettings"/>.</summary>
internal sealed class DiscordChannelSettings : ChannelSettings
{
    public Uri? WebhookUrl { get; set; }

    public static void Apply(IConfiguration section, DiscordChannelOptions options)
    {
        var settings = section.Get<DiscordChannelSettings>();
        if (settings is null)
        {
            return;
        }

        options.WebhookUrl = settings.WebhookUrl ?? options.WebhookUrl;
        settings.ApplyShared(options);
    }
}

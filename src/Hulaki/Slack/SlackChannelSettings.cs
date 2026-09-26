using System;
using Hulaki.Providers;
using Microsoft.Extensions.Configuration;

namespace Hulaki.Slack;

/// <summary>The configuration-bindable part of <see cref="SlackChannelOptions"/>; see <see cref="ChannelSettings"/>.</summary>
internal sealed class SlackChannelSettings : ChannelSettings
{
    public Uri? WebhookUrl { get; set; }

    public static void Apply(IConfiguration section, SlackChannelOptions options)
    {
        var settings = section.Get<SlackChannelSettings>();
        if (settings is null)
        {
            return;
        }

        options.WebhookUrl = settings.WebhookUrl ?? options.WebhookUrl;
        settings.ApplyShared(options);
    }
}

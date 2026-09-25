using System;
using System.Collections.Generic;
using Hulaki.Providers;
using Microsoft.Extensions.Configuration;

namespace Hulaki.Webhook;

/// <summary>The configuration-bindable part of <see cref="WebhookChannelOptions"/>; see <see cref="ChannelSettings"/>.</summary>
internal sealed class WebhookChannelSettings : ChannelSettings
{
    public Uri? Url { get; set; }

    public string? Secret { get; set; }

    public Dictionary<string, string>? Headers { get; set; }

    public string? BodyTemplate { get; set; }

    public string? ContentType { get; set; }

    public int? MaxTextBytes { get; set; }

    public static void Apply(IConfiguration section, WebhookChannelOptions options)
    {
        var settings = section.Get<WebhookChannelSettings>();
        if (settings is null)
        {
            return;
        }

        options.Url = settings.Url ?? options.Url;
        options.Secret = settings.Secret ?? options.Secret;
        options.BodyTemplate = settings.BodyTemplate ?? options.BodyTemplate;
        options.ContentType = settings.ContentType ?? options.ContentType;
        options.MaxTextBytes = settings.MaxTextBytes ?? options.MaxTextBytes;
        foreach (var (header, value) in settings.Headers ?? [])
        {
            options.Headers[header] = value;
        }

        settings.ApplyShared(options);
    }
}

using System;
using Hulaki.Providers;
using Microsoft.Extensions.Configuration;

namespace Hulaki.WebPush;

/// <summary>The configuration-bindable part of <see cref="WebPushChannelOptions"/>; see <see cref="ChannelSettings"/>.</summary>
internal sealed class WebPushChannelSettings : ChannelSettings
{
    public string? VapidPublicKey { get; set; }

    public string? VapidPrivateKey { get; set; }

    public string? VapidSubject { get; set; }

    public TimeSpan? TimeToLive { get; set; }

    public static void Apply(IConfiguration section, WebPushChannelOptions options)
    {
        var settings = section.Get<WebPushChannelSettings>();
        if (settings is null)
        {
            return;
        }

        options.VapidPublicKey = settings.VapidPublicKey ?? options.VapidPublicKey;
        options.VapidPrivateKey = settings.VapidPrivateKey ?? options.VapidPrivateKey;
        options.VapidSubject = settings.VapidSubject ?? options.VapidSubject;
        options.TimeToLive = settings.TimeToLive ?? options.TimeToLive;
        settings.ApplyShared(options);
    }
}

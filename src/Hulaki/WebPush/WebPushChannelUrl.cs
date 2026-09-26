using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using Hulaki.Providers;

namespace Hulaki.WebPush;

/// <summary>
/// <c>webpush://&lt;vapid-public-key&gt;:&lt;vapid-private-key&gt;@vapid?subject=&lt;mailto or https&gt;[&amp;ttl=&lt;seconds&gt;]</c>.
/// For the CLI only (ADR-0013). The subscription's endpoint is the recipient address, and its
/// <c>p256dh</c> and <c>auth</c> keys are recipient properties.
/// </summary>
internal static class WebPushChannelUrl
{
    public const string Scheme = "webpush";

    public const string Format = "webpush://<vapid-public-key>:<vapid-private-key>@vapid?subject=<mailto:...>[&ttl=<seconds>]";

    public static IChannel? TryCreate(string name, Uri url, HttpClient http, out string? error)
    {
        var (publicKey, privateKey) = ChannelUrlParts.Credentials(url);
        var query = ChannelUrlParts.Query(url);
        var options = new WebPushChannelOptions
        {
            VapidPublicKey = publicKey,
            VapidPrivateKey = privateKey ?? string.Empty,
            VapidSubject = query.GetValueOrDefault("subject", string.Empty),
        };
        if (query.TryGetValue("ttl", out var ttl))
        {
            if (!int.TryParse(ttl, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            {
                error = "The ttl parameter must be a whole number of seconds.";
                return null;
            }

            options.TimeToLive = TimeSpan.FromSeconds(seconds);
        }

        return ChannelUrlParts.Create(name, options, new WebPushChannelOptionsValidator(), () => new WebPushChannel(name, http, options), out error);
    }
}

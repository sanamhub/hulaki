using System;
using System.Net.Http;
using Hulaki.Providers;

namespace Hulaki.Webhook;

/// <summary>
/// <c>webhook+https://[&lt;secret&gt;@]&lt;host&gt;/&lt;path&gt;</c>: the receiver's URL with <c>webhook+</c>
/// in front and the HMAC secret, if any, as user info. <c>webhook+http</c> is accepted for a
/// loopback receiver. For the CLI only (ADR-0013).
/// </summary>
internal static class WebhookChannelUrl
{
    public const string Scheme = "webhook+https";

    public const string LoopbackScheme = "webhook+http";

    public const string Format = "webhook+https://[<hmac-secret>@]<host>/<path>";

    public static IChannel? TryCreate(string name, Uri url, HttpClient http, out string? error)
    {
        var secret = ChannelUrlParts.UserInfo(url);
        var target = new UriBuilder(url)
        {
            Scheme = url.Scheme[(url.Scheme.IndexOf('+', StringComparison.Ordinal) + 1)..],
            UserName = string.Empty,
            Password = string.Empty,
        };
        if (url.IsDefaultPort)
        {
            target.Port = -1;
        }

        var options = new WebhookChannelOptions { Url = target.Uri, Secret = secret.Length == 0 ? null : secret };
        return ChannelUrlParts.Create(name, options, new WebhookChannelOptionsValidator(), () => new WebhookChannel(name, http, options), out error);
    }
}

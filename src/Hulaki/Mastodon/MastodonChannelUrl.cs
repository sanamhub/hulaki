using System;
using System.Globalization;
using System.Net.Http;
using Hulaki.Providers;

namespace Hulaki.Mastodon;

/// <summary>
/// <c>mastodon://&lt;access-token&gt;@&lt;instance host&gt;[?visibility=unlisted&amp;lang=ne&amp;max=5000]</c>.
/// For the CLI only (ADR-0013).
/// </summary>
internal static class MastodonChannelUrl
{
    public const string Scheme = "mastodon";

    public const string Format = "mastodon://<access-token>@<instance host>[?visibility=unlisted&lang=ne&max=5000]";

    public static IChannel? TryCreate(string name, Uri url, HttpClient http, out string? error)
    {
        var query = ChannelUrlParts.Query(url);
        var options = new MastodonChannelOptions
        {
            InstanceUrl = new UriBuilder(url.IsLoopback ? Uri.UriSchemeHttp : Uri.UriSchemeHttps, url.Host, url.IsDefaultPort ? -1 : url.Port).Uri,
            AccessToken = ChannelUrlParts.UserInfo(url),
            Visibility = query.TryGetValue("visibility", out var visibility) ? visibility : null,
            Language = query.TryGetValue("lang", out var language) ? language : null,
        };
        if (query.TryGetValue("max", out var max))
        {
            if (!int.TryParse(max, NumberStyles.None, CultureInfo.InvariantCulture, out var characters))
            {
                error = "The max parameter must be a whole number of characters.";
                return null;
            }

            options.MaxCharacters = characters;
        }

        return ChannelUrlParts.Create(name, options, new MastodonChannelOptionsValidator(), () => new MastodonChannel(name, http, options), out error);
    }
}

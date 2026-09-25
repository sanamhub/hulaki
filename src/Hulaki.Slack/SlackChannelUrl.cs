using System;
using System.Net.Http;
using Hulaki.Providers;

namespace Hulaki.Slack;

/// <summary>
/// <c>slack://hooks.slack.com/services/&lt;T&gt;/&lt;B&gt;/&lt;secret&gt;</c>: the webhook URL with
/// <c>slack</c> as its scheme. The path is the secret and is never printed. For the CLI only
/// (ADR-0013).
/// </summary>
internal static class SlackChannelUrl
{
    public const string Scheme = "slack";

    public const string Format = "slack://hooks.slack.com/services/<T...>/<B...>/<secret>";

    public static IChannel? TryCreate(string name, Uri url, HttpClient http, out string? error)
    {
        var options = new SlackChannelOptions
        {
            WebhookUrl = new UriBuilder(url) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri,
        };
        return ChannelUrlParts.Create(name, options, new SlackChannelOptionsValidator(), () => new SlackChannel(name, http, options), out error);
    }
}

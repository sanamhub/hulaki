using System;
using System.Net.Http;
using Hulaki.Providers;

namespace Hulaki.Discord;

/// <summary>
/// <c>discord://&lt;webhook-id&gt;:&lt;webhook-token&gt;@discord</c>, the two parts of
/// <c>https://discord.com/api/webhooks/{id}/{token}</c>. For the CLI only (ADR-0013).
/// </summary>
internal static class DiscordChannelUrl
{
    public const string Scheme = "discord";

    public const string Format = "discord://<webhook-id>:<webhook-token>@discord";

    public static IChannel? TryCreate(string name, Uri url, HttpClient http, out string? error)
    {
        var (id, token) = ChannelUrlParts.Credentials(url);
        var options = new DiscordChannelOptions();
        if (id.Length > 0 && !string.IsNullOrEmpty(token) && Uri.TryCreate($"https://discord.com/api/webhooks/{Uri.EscapeDataString(id)}/{Uri.EscapeDataString(token)}", UriKind.Absolute, out var webhook))
        {
            options.WebhookUrl = webhook;
        }

        return ChannelUrlParts.Create(name, options, new DiscordChannelOptionsValidator(), () => new DiscordChannel(name, http, options), out error);
    }
}

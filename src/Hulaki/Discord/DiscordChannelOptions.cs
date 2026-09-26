using System;
using Hulaki.Channels;

namespace Hulaki.Discord;

/// <summary>Options for <see cref="DiscordChannel"/>.</summary>
public sealed class DiscordChannelOptions : ChannelOptions
{
    /// <summary>
    /// The webhook URL from the channel's Integrations settings, for example
    /// <c>https://discord.com/api/webhooks/{id}/{token}</c>. A secret: anyone holding it can post to
    /// the channel, and it is the request URI, so the channel's <see cref="System.Net.Http.HttpClient"/>
    /// must not log request URIs (ADR-0015).
    /// </summary>
    public Uri? WebhookUrl { get; set; }
}

using System;
using Hulaki.Channels;

namespace Hulaki.Slack;

/// <summary>Options for <see cref="SlackChannel"/>.</summary>
public sealed class SlackChannelOptions : ChannelOptions
{
    /// <summary>
    /// The incoming webhook URL, <c>https://hooks.slack.com/services/...</c>. A secret: anyone
    /// holding it can post to the channel, and it is the request URI, so the channel's
    /// <see cref="System.Net.Http.HttpClient"/> must not log request URIs (ADR-0015).
    /// </summary>
    public Uri? WebhookUrl { get; set; }
}

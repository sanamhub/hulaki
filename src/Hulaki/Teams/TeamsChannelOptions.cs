using System;
using Hulaki.Channels;

namespace Hulaki.Teams;

/// <summary>Options for <see cref="TeamsChannel"/>.</summary>
public sealed class TeamsChannelOptions : ChannelOptions
{
    /// <summary>
    /// The HTTP URL of a Teams Workflows flow built from "Post to a channel when a webhook request
    /// is received". A secret: its <c>sig</c> parameter authorizes the post, and it is the request
    /// URI, so the channel's <see cref="System.Net.Http.HttpClient"/> must not log request URIs
    /// (ADR-0015). Office 365 connector URLs are refused: Microsoft retired them on 2026-05-22.
    /// </summary>
    public Uri? WorkflowUrl { get; set; }
}

using System;
using Hulaki.Channels;

namespace Hulaki.Ntfy;

/// <summary>Options for <see cref="NtfyChannel"/>.</summary>
public sealed class NtfyChannelOptions : ChannelOptions
{
    /// <summary>
    /// The server's root URL. Defaults to <c>https://ntfy.sh/</c>. Set it for a self-hosted server;
    /// plain HTTP is accepted only on loopback (ADR-0015).
    /// </summary>
    public Uri BaseAddress { get; set; } = new("https://ntfy.sh/");

    /// <summary>
    /// Optional access token, sent as <c>Authorization: Bearer</c>. Needed for reserved topics and
    /// servers that deny anonymous publishing. A secret.
    /// </summary>
    public string? AccessToken { get; set; }
}

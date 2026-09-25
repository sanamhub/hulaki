using System;
using Hulaki.Channels;

namespace Hulaki.WebPush;

/// <summary>Options for <see cref="WebPushChannel"/>.</summary>
public sealed class WebPushChannelOptions : ChannelOptions
{
    /// <summary>
    /// The VAPID public key: an uncompressed P-256 point, 65 bytes, base64url. The same key the
    /// page passed to <c>pushManager.subscribe</c> as <c>applicationServerKey</c>.
    /// </summary>
    public string VapidPublicKey { get; set; } = string.Empty;

    /// <summary>The VAPID private key: the 32-byte P-256 scalar, base64url. A secret.</summary>
    public string VapidPrivateKey { get; set; } = string.Empty;

    /// <summary>
    /// A contact for the push service operator, <c>mailto:</c> or <c>https:</c> (RFC 8292
    /// section 2.1), for example <c>mailto:ops@example.org</c>.
    /// </summary>
    public string VapidSubject { get; set; } = string.Empty;

    /// <summary>How long the push service keeps an undelivered message. Defaults to 24 hours.</summary>
    public TimeSpan TimeToLive { get; set; } = TimeSpan.FromHours(24);
}

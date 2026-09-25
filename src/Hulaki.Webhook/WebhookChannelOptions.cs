using System;
using System.Collections.Generic;
using Hulaki.Channels;

namespace Hulaki.Webhook;

/// <summary>Options for <see cref="WebhookChannel"/>.</summary>
public sealed class WebhookChannelOptions : ChannelOptions
{
    /// <summary>
    /// The endpoint that receives the POST. HTTPS, or HTTP on loopback (ADR-0015). Treat it as a
    /// secret when it carries a token in its path or query; the channel never logs it.
    /// </summary>
    public Uri? Url { get; set; }

    /// <summary>
    /// Shared secret for the <c>X-Hulaki-Signature: sha256=&lt;hex&gt;</c> header, an HMAC-SHA256 of
    /// the raw request body. Null sends no signature. A secret.
    /// </summary>
    public string? Secret { get; set; }

    /// <summary>
    /// Headers added to every request, for example an <c>Authorization</c> header the receiver
    /// expects. Values can be secrets; the channel never logs them.
    /// </summary>
    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Body template. Null sends <c>{"title","text","priority","link"}</c> as JSON. Otherwise
    /// <c>{{title}}</c>, <c>{{text}}</c>, <c>{{priority}}</c> and <c>{{link}}</c> are replaced by the
    /// message's values, escaped for a JSON string when <see cref="ContentType"/> is JSON (write the
    /// quotes in the template) and form-encoded when it is <c>application/x-www-form-urlencoded</c>.
    /// </summary>
    public string? BodyTemplate { get; set; }

    /// <summary>Media type of the body. Defaults to <c>application/json</c>.</summary>
    public string ContentType { get; set; } = "application/json";

    /// <summary>Longest text the receiver accepts, in UTF-8 bytes. Defaults to 65536.</summary>
    public int MaxTextBytes { get; set; } = 65536;
}

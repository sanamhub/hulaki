using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Hulaki.Channels;
using Hulaki.Markup;
using Hulaki.Text;

namespace Hulaki.WebPush;

/// <summary>
/// Sends Web Push messages (RFC 8030) to browser subscriptions, signed with VAPID (RFC 8292) and
/// encrypted with <c>aes128gcm</c> (RFC 8291). The recipient address is the subscription's
/// <c>endpoint</c>; <see cref="Recipient.Properties"/> holds its <c>p256dh</c> and <c>auth</c> keys,
/// base64url, as <c>PushSubscription.toJSON()</c> gives them. The payload is
/// <c>{"title","body","url"}</c> JSON for the page's service worker to show.
/// </summary>
public sealed class WebPushChannel : ChannelBase
{
    /// <summary>
    /// A push message is one 4096-byte record, so the JSON payload can be 3993 UTF-8 bytes after
    /// the 86-byte header and 17 bytes of tag and padding. The title and link count, as JSON.
    /// </summary>
    public static CapabilityManifest Manifest { get; } = new(
        "webpush",
        new TextLimit(WebPushEncryption.MaxPlaintext, TextCounter.Utf8Bytes),
        [
            new(Capability.Text, Availability.Available),
            new(Capability.Title, Availability.Available, "The payload's title field"),
            new(Capability.Markup, Availability.UnsupportedByPlatform, "Notifications show plain text"),
            new(Capability.Priority, Availability.Available, "Urgency header: Low very-low, Normal normal, High and Urgent high"),
            new(Capability.ClickAction, Availability.Available, "The payload's url field, for the service worker"),
            new(Capability.Images, Availability.NotImplemented),
            new(Capability.IdempotentSend, Availability.UnsupportedByPlatform),
        ]);

    private readonly HttpClient _http;
    private readonly WebPushChannelOptions _options;
    private readonly VapidSigner _vapid;
    private readonly Func<ECDiffieHellman> _newServerKey;
    private readonly Func<byte[]> _newSalt;

    /// <summary>Creates the channel.</summary>
    /// <param name="name">Registration name.</param>
    /// <param name="http">Client with no request logging: endpoints are capability URLs. The channel does not dispose it.</param>
    /// <param name="options">Options with the VAPID key pair and subject.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty, the VAPID keys are not a matching P-256 pair, the subject is not <c>mailto:</c> or <c>https:</c>, or the time to live is negative.</exception>
    public WebPushChannel(string name, HttpClient http, WebPushChannelOptions options)
        : this(name, http, options, static () => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256), static () => RandomNumberGenerator.GetBytes(16))
    {
    }

    /// <summary>The seam for the RFC 8291 example: a fixed server key and salt.</summary>
    internal WebPushChannel(string name, HttpClient http, WebPushChannelOptions options, Func<ECDiffieHellman> newServerKey, Func<byte[]> newSalt)
        : base(name, Manifest, options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.TimeToLive, TimeSpan.Zero, "options.TimeToLive");
        _vapid = VapidSigner.TryCreate(options.VapidPublicKey, options.VapidPrivateKey, options.VapidSubject, options.TimeProvider, out var problem)
            ?? throw new ArgumentException(problem, nameof(options));
        _http = http;
        _options = options;
        _newServerKey = newServerKey;
        _newSalt = newSalt;
    }

    /// <inheritdoc />
    protected override void PrepareCore(Message message, Recipient recipient, ICollection<PreparationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(issues);
        if (!IsEndpoint(recipient.Address))
        {
            issues.Add(new("invalid-endpoint", "A Web Push endpoint is an absolute https URL on a push service's host name."));
        }

        if (!recipient.Properties.TryGetValue("p256dh", out var p256dh) || !WebPushEncryption.TryDecode(p256dh, out var point) || point.Length != 65 || point[0] != 4)
        {
            issues.Add(new("invalid-p256dh", "The p256dh property must be a base64url uncompressed P-256 point."));
        }

        if (!recipient.Properties.TryGetValue("auth", out var auth) || !WebPushEncryption.TryDecode(auth, out var secret) || secret.Length != 16)
        {
            issues.Add(new("invalid-auth", "The auth property must be 16 bytes, base64url."));
        }
    }

    /// <inheritdoc />
    protected override async Task<DeliveryOutcome> SendOnceAsync(Message message, Recipient recipient, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(recipient);
        var endpoint = new Uri(recipient.Address);
        WebPushEncryption.TryDecode(recipient.Properties["p256dh"], out var userAgentKey);
        WebPushEncryption.TryDecode(recipient.Properties["auth"], out var authSecret);

        byte[] body;
        try
        {
            using var serverKey = _newServerKey();
            body = WebPushEncryption.Encrypt(Payload(message), userAgentKey, authSecret, serverKey, _newSalt());
        }
        catch (CryptographicException)
        {
            // The p256dh bytes have the right shape but are not a point on the curve.
            return DeliveryOutcome.Failed(new HulakiError(HulakiErrorCode.InvalidInput, RetryDisposition.Never, "The subscription's p256dh key is not a P-256 public key."));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add("aes128gcm");
        request.Headers.Authorization = new AuthenticationHeaderValue("vapid", _vapid.AuthorizationFor(endpoint));
        request.Headers.TryAddWithoutValidation("TTL", ((long)_options.TimeToLive.TotalSeconds).ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("Urgency", UrgencyOf(message.Priority));

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            // 201 Created: the push service queued it (RFC 8030 section 5). Delivery to the browser happens later.
            return DeliveryOutcome.Accepted();
        }

        return HttpHelpers.OutcomeFor(MapError(response));
    }

    /// <inheritdoc />
    /// <remarks>The JSON payload is what gets encrypted, so it is what the limit measures.</remarks>
    protected override string CountedText(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Encoding.UTF8.GetString(Payload(message));
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _vapid.Dispose();
        }

        base.Dispose(disposing);
    }

    internal static string UrgencyOf(MessagePriority priority) => priority switch
    {
        MessagePriority.Low => "very-low",
        MessagePriority.High or MessagePriority.Urgent => "high",
        _ => "normal",
    };

    internal static byte[] Payload(Message message)
    {
        var body = message.Format == TextFormat.Markup ? MarkupDocument.Parse(message.Text).ToPlainText() : message.Text;
        var payload = new WebPushPayload { Title = message.Title, Body = body, Url = message.Link?.AbsoluteUri };
        return JsonSerializer.SerializeToUtf8Bytes(payload, WebPushJsonContext.Default.WebPushPayload);
    }

    /// <summary>
    /// An absolute https URL on a host name. IP literals and localhost are refused: push services
    /// have DNS names, and an endpoint comes from a browser the app does not control.
    /// </summary>
    internal static bool IsEndpoint(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.HostNameType == UriHostNameType.Dns
        && !uri.IsLoopback;

    private HulakiError MapError(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        var retryAfter = HttpHelpers.RetryAfter(response, Options.TimeProvider);

        HulakiError Error(HulakiErrorCode code, RetryDisposition retry, string text) =>
            new(code, retry, text) { HttpStatus = status, RetryAfter = retryAfter };

        // Push services answer in their own formats, and none is needed beyond the status (RFC 8030 section 5).
        return status switch
        {
            404 or 410 => Error(HulakiErrorCode.RecipientNotFound, RetryDisposition.Never, "The subscription has expired or was removed."),
            401 or 403 => Error(HulakiErrorCode.InvalidConfiguration, RetryDisposition.Never, "The push service refused the VAPID signature or key."),
            413 => Error(HulakiErrorCode.InvalidInput, RetryDisposition.Never, "The push service refused the payload size."),
            429 => Error(HulakiErrorCode.RateLimited, RetryDisposition.AfterDelay, "The push service rate limited the request."),
            _ => HttpHelpers.ErrorFor(response.StatusCode, retryAfter, $"The push service returned {status}."),
        };
    }
}

/// <summary>The payload the page's service worker reads. Changing it breaks deployed service workers.</summary>
internal sealed class WebPushPayload
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("body")]
    public required string Body { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WebPushPayload))]
internal sealed partial class WebPushJsonContext : JsonSerializerContext;

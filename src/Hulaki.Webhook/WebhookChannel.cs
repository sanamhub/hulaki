using System;
using System.Collections.Generic;
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

namespace Hulaki.Webhook;

/// <summary>
/// Posts a message to any HTTP endpoint the caller controls. The recipient is
/// <see cref="Recipient.Self"/>: the configured URL. The default body is
/// <c>{"title","text","priority","link"}</c>; a template can shape it for a receiver that expects
/// something else. With <see cref="WebhookChannelOptions.Secret"/> set, every request carries
/// <c>X-Hulaki-Signature: sha256=&lt;hex&gt;</c>, an HMAC-SHA256 of the raw body, so the receiver
/// can check it came from you.
/// </summary>
public sealed class WebhookChannel : ChannelBase
{
    /// <summary>The header carrying the body's HMAC-SHA256.</summary>
    internal const string SignatureHeader = "X-Hulaki-Signature";

    private static readonly CapabilityDeclaration[] Declarations =
    [
        new(Capability.Text, Availability.Available),
        new(Capability.Title, Availability.Available, "The title field"),
        new(Capability.Markup, Availability.UnsupportedByPlatform, "Sent as plain text"),
        new(Capability.Priority, Availability.Available, "The priority field; the receiver decides what it means"),
        new(Capability.ClickAction, Availability.Available, "The link field"),
        new(Capability.IdempotentSend, Availability.UnsupportedByPlatform),
    ];

    private readonly HttpClient _http;
    private readonly WebhookChannelOptions _options;
    private readonly Uri _url;
    private readonly byte[]? _secret;

    /// <summary>
    /// The default limit is 65536 UTF-8 bytes of text. A channel uses
    /// <see cref="WebhookChannelOptions.MaxTextBytes"/> instead.
    /// </summary>
    public static CapabilityManifest Manifest { get; } = ManifestFor(65536);

    /// <summary>Creates the channel.</summary>
    /// <param name="name">Registration name.</param>
    /// <param name="http">Client with no request logging when the URL holds a secret. The channel does not dispose it.</param>
    /// <param name="options">Options with a URL.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty, the URL is missing or not absolute, the content type is empty, or the text limit is below 1.</exception>
    public WebhookChannel(string name, HttpClient http, WebhookChannelOptions options)
        : base(name, ManifestFor(options), options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Url is not { IsAbsoluteUri: true } url)
        {
            throw new ArgumentException("Url must be an absolute URL.", nameof(options));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(options.ContentType, "options.ContentType");
        _http = http;
        _options = options;
        _url = url;
        _secret = string.IsNullOrEmpty(options.Secret) ? null : Encoding.UTF8.GetBytes(options.Secret);
    }

    /// <inheritdoc />
    protected override void PrepareCore(Message message, Recipient recipient, ICollection<PreparationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(issues);
        if (!recipient.IsSelf)
        {
            issues.Add(new("recipient-must-be-self", "A webhook posts to its configured URL; use Recipient.Self."));
        }
    }

    /// <inheritdoc />
    protected override async Task<DeliveryOutcome> SendOnceAsync(Message message, Recipient recipient, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var body = BuildBody(message, _options);
        using var request = new HttpRequestMessage(HttpMethod.Post, _url) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(_options.ContentType);
        foreach (var (header, value) in _options.Headers)
        {
            if (!request.Headers.TryAddWithoutValidation(header, value))
            {
                request.Content.Headers.Remove(header);
                request.Content.Headers.TryAddWithoutValidation(header, value);
            }
        }

        if (_secret is not null)
        {
            request.Headers.TryAddWithoutValidation(SignatureHeader, Sign(_secret, body));
        }

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.StatusCode switch
        {
            HttpStatusCode.Accepted => DeliveryOutcome.Accepted(),
            _ when response.IsSuccessStatusCode => DeliveryOutcome.Delivered(),
            // The receiver is the caller's own code; its body says nothing Hulaki can map, and may echo the message.
            var status => HttpHelpers.OutcomeFor(HttpHelpers.ErrorFor(status, HttpHelpers.RetryAfter(response, Options.TimeProvider), $"The webhook returned {(int)status}.")),
        };
    }

    /// <inheritdoc />
    /// <remarks>The title and link travel in their own fields, so only the text counts.</remarks>
    protected override string CountedText(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return PlainText(message);
    }

    /// <summary><c>sha256=</c> and the lower-case hex HMAC-SHA256 of <paramref name="body"/>.</summary>
    internal static string Sign(byte[] secret, byte[] body) => "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(secret, body));

    internal static byte[] BuildBody(Message message, WebhookChannelOptions options)
    {
        var text = PlainText(message);
        var priority = PriorityName(message.Priority);
        var link = message.Link?.AbsoluteUri;
        if (options.BodyTemplate is null)
        {
            var payload = new WebhookPayload { Title = message.Title, Text = text, Priority = priority, Link = link };
            return JsonSerializer.SerializeToUtf8Bytes(payload, WebhookJsonContext.Default.WebhookPayload);
        }

        Func<string, string> escape = MediaTypeHeaderValue.Parse(options.ContentType).MediaType switch
        {
            var type when type is not null && (type.EndsWith("/json", StringComparison.OrdinalIgnoreCase) || type.EndsWith("+json", StringComparison.OrdinalIgnoreCase)) =>
                value => JsonEncodedText.Encode(value).ToString(),
            "application/x-www-form-urlencoded" => WebUtility.UrlEncode,
            _ => value => value,
        };

        var rendered = new StringBuilder(options.BodyTemplate)
            .Replace("{{title}}", escape(message.Title ?? string.Empty))
            .Replace("{{text}}", escape(text))
            .Replace("{{priority}}", escape(priority))
            .Replace("{{link}}", escape(link ?? string.Empty))
            .ToString();
        return Encoding.UTF8.GetBytes(rendered);
    }

    private static string PlainText(Message message) =>
        message.Format == TextFormat.Markup ? MarkupDocument.Parse(message.Text).ToPlainText() : message.Text;

    private static string PriorityName(MessagePriority priority) => priority switch
    {
        MessagePriority.Low => "low",
        MessagePriority.High => "high",
        MessagePriority.Urgent => "urgent",
        _ => "normal",
    };

    private static CapabilityManifest ManifestFor(WebhookChannelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxTextBytes, 1, "options.MaxTextBytes");
        return options.MaxTextBytes == Manifest.TextLimit.Max ? Manifest : ManifestFor(options.MaxTextBytes);
    }

    private static CapabilityManifest ManifestFor(int maxTextBytes) =>
        new("webhook", new TextLimit(maxTextBytes, TextCounter.Utf8Bytes), Declarations);
}

/// <summary>The default body. Documented for receivers in PACKAGE.md; change it only in a major version.</summary>
internal sealed class WebhookPayload
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("priority")]
    public required string Priority { get; init; }

    [JsonPropertyName("link")]
    public string? Link { get; init; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WebhookPayload))]
internal sealed partial class WebhookJsonContext : JsonSerializerContext;

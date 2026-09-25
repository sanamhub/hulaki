using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Hulaki.Channels;
using Hulaki.Markup;
using Hulaki.Text;

namespace Hulaki.Telegram;

/// <summary>Options for <see cref="TelegramChannel"/>.</summary>
public sealed class TelegramChannelOptions : ChannelOptions
{
    /// <summary>
    /// Bot token from BotFather. A secret: it is part of every request path, so the channel's
    /// <see cref="HttpClient"/> must not log request URIs (ADR-0015).
    /// </summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>API base. Change only for a local Bot API server.</summary>
    public Uri BaseAddress { get; set; } = new("https://api.telegram.org/");

    /// <summary>Hide link previews. Defaults to true, so an alert stays one compact message.</summary>
    public bool DisableLinkPreview { get; set; } = true;
}

/// <summary>
/// Sends to Telegram chats through the Bot API <c>sendMessage</c> method. The recipient address is
/// a chat id or <c>@channelusername</c>. Markup is rendered as Telegram HTML.
/// </summary>
public sealed class TelegramChannel : ChannelBase
{
    /// <summary>Telegram's limit is 4096 characters after entity parsing, counted in UTF-16 code units.</summary>
    public static CapabilityManifest Manifest { get; } = new(
        "telegram",
        new TextLimit(4096, TextCounter.Utf16CodeUnits),
        [
            new(Capability.Text, Availability.Available),
            new(Capability.Markup, Availability.Available, "Telegram HTML parse mode"),
            new(Capability.Priority, Availability.Available, "Low sends silently; others notify"),
            new(Capability.Title, Availability.UnsupportedByPlatform, "Sent as a bold first line"),
            new(Capability.Images, Availability.NotImplemented, "sendPhoto in 0.2"),
            new(Capability.IdempotentSend, Availability.UnsupportedByPlatform),
            new(Capability.Delete, Availability.NotImplemented),
        ]);

    private readonly HttpClient _http;
    private readonly TelegramChannelOptions _options;
    private readonly PartitionedRateLimiter<string>? _perChat;

    /// <summary>Creates the channel.</summary>
    /// <param name="name">Registration name.</param>
    /// <param name="http">Client with no request logging. The channel does not dispose it.</param>
    /// <param name="options">Options with a bot token.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> or the bot token is empty.</exception>
    public TelegramChannel(string name, HttpClient http, TelegramChannelOptions options)
        : base(name, Manifest, options, DefaultLimiter)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.BotToken, "options.BotToken");
        _http = http;
        _options = options;
        _perChat = options.DisableRateLimiting ? null : ChatRateLimiter.PerChat(options.TimeProvider);
    }

    /// <inheritdoc />
    protected override void PrepareCore(Message message, Recipient recipient, ICollection<PreparationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(issues);
        if (recipient.IsSelf)
        {
            issues.Add(new("recipient-required", "Telegram needs a chat id or @channel."));
        }
    }

    /// <inheritdoc />
    protected override async Task<DeliveryOutcome> SendOnceAsync(Message message, Recipient recipient, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(recipient);
        var request = new SendMessageRequest
        {
            ChatId = recipient.Address,
            Text = Render(message),
            ParseMode = "HTML",
            DisableNotification = message.Priority == MessagePriority.Low ? true : null,
            LinkPreviewOptions = _options.DisableLinkPreview ? new LinkPreviewOptions { IsDisabled = true } : null,
        };

        // Telegram allows about one message per second to the same chat (core.telegram.org/bots/faq).
        using var chatLease = _perChat is null ? null : await _perChat.AcquireAsync(recipient.Address, 1, cancellationToken).ConfigureAwait(false);
        if (chatLease is { IsAcquired: false })
        {
            return DeliveryOutcome.Failed(new HulakiError(HulakiErrorCode.RateLimited, RetryDisposition.AfterDelay, "Local per-chat queue is full."));
        }

        // "./" matters: without it the token's colon makes "bot123" parse as a URI scheme.
        var uri = new Uri(_options.BaseAddress, $"./bot{_options.BotToken}/sendMessage");
        using var response = await _http.PostAsJsonAsync(uri, request, TelegramJsonContext.Default.SendMessageRequest, cancellationToken).ConfigureAwait(false);
        var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);

        if (body is { Ok: true, Result: { } result })
        {
            return DeliveryOutcome.Delivered(result.MessageId.ToString(CultureInfo.InvariantCulture));
        }

        return HttpHelpers.OutcomeFor(MapError(response, body));
    }

    /// <inheritdoc />
    protected override string CountedText(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var plain = message.Format == TextFormat.Markup ? MarkupDocument.Parse(message.Text).ToPlainText() : message.Text;
        var title = message.Title is null ? string.Empty : message.Title + "\n";
        var link = message.Link is null ? string.Empty : "\n" + message.Link.AbsoluteUri;
        return title + plain + link;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _perChat?.Dispose();
        }

        base.Dispose(disposing);
    }

    internal static string Render(Message message)
    {
        var builder = new StringBuilder();
        if (message.Title is not null)
        {
            builder.Append("<b>").Append(WebUtility.HtmlEncode(message.Title)).Append("</b>\n");
        }

        if (message.Format == TextFormat.Markup)
        {
            AppendHtml(builder, MarkupDocument.Parse(message.Text).Nodes);
        }
        else
        {
            builder.Append(WebUtility.HtmlEncode(message.Text));
        }

        if (message.Link is not null)
        {
            builder.Append('\n').Append(WebUtility.HtmlEncode(message.Link.AbsoluteUri));
        }

        return builder.ToString();
    }

    private static void AppendHtml(StringBuilder builder, IReadOnlyList<MarkupNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode t:
                    builder.Append(WebUtility.HtmlEncode(t.Value));
                    break;
                case BoldNode b:
                    builder.Append("<b>");
                    AppendHtml(builder, b.Children);
                    builder.Append("</b>");
                    break;
                case ItalicNode i:
                    builder.Append("<i>");
                    AppendHtml(builder, i.Children);
                    builder.Append("</i>");
                    break;
                case CodeNode c:
                    builder.Append("<code>").Append(WebUtility.HtmlEncode(c.Value)).Append("</code>");
                    break;
                case LinkNode l:
                    builder.Append("<a href=\"").Append(WebUtility.HtmlEncode(l.Target.AbsoluteUri)).Append("\">")
                        .Append(WebUtility.HtmlEncode(l.Text)).Append("</a>");
                    break;
                default:
                    throw new InvalidOperationException($"Unknown node {node.GetType().Name}.");
            }
        }
    }

    private static async Task<TelegramResponse?> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync(TelegramJsonContext.Default.TelegramResponse, cancellationToken).ConfigureAwait(false);
        }
        catch (System.Text.Json.JsonException)
        {
            return null; // a proxy's HTML error page; the status code decides
        }
    }

    private HulakiError MapError(HttpResponseMessage response, TelegramResponse? body)
    {
        // Telegram's descriptions are stable English strings; they are the only way to tell
        // "blocked by the user" from other 403s. Never include them verbatim: they can echo input.
        var description = body?.Description ?? string.Empty;
        var platformCode = body?.ErrorCode?.ToString(CultureInfo.InvariantCulture);
        var status = response.StatusCode;
        var retryAfter = body?.Parameters?.RetryAfter is int seconds ? TimeSpan.FromSeconds(seconds) : HttpHelpers.RetryAfter(response, Options.TimeProvider);

        HulakiError Error(HulakiErrorCode code, RetryDisposition retry, string text) =>
            new(code, retry, text) { HttpStatus = (int)status, PlatformCode = platformCode, RetryAfter = retryAfter };

        return (int)status switch
        {
            403 when Contains(description, "blocked") || Contains(description, "deactivated") || Contains(description, "kicked") =>
                Error(HulakiErrorCode.RecipientBlocked, RetryDisposition.Never, "The recipient blocked the bot or left the chat."),
            400 when Contains(description, "chat not found") =>
                Error(HulakiErrorCode.RecipientNotFound, RetryDisposition.Never, "Chat not found."),
            400 when Contains(description, "parse entities") =>
                Error(HulakiErrorCode.InvalidInput, RetryDisposition.Never, "Telegram could not parse the HTML."),
            401 or 404 =>
                Error(HulakiErrorCode.InvalidConfiguration, RetryDisposition.Never, "The bot token was refused."),
            _ => HttpHelpers.ErrorFor(status, retryAfter, $"Telegram returned {(int)status}.") with { PlatformCode = platformCode },
        };
    }

    private static bool Contains(string text, string value) => text.Contains(value, StringComparison.OrdinalIgnoreCase);

    // Telegram allows about 30 messages per second per bot (core.telegram.org/bots/faq).
    private static RateLimiter DefaultLimiter() => new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 30,
        TokensPerPeriod = 30,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        QueueLimit = 1000,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    });
}

internal sealed class SendMessageRequest
{
    [JsonPropertyName("chat_id")]
    public required string ChatId { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("parse_mode")]
    public string? ParseMode { get; init; }

    [JsonPropertyName("disable_notification")]
    public bool? DisableNotification { get; init; }

    [JsonPropertyName("link_preview_options")]
    public LinkPreviewOptions? LinkPreviewOptions { get; init; }
}

internal sealed class LinkPreviewOptions
{
    [JsonPropertyName("is_disabled")]
    public bool IsDisabled { get; init; }
}

internal sealed class TelegramResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("result")]
    public TelegramMessage? Result { get; init; }

    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("parameters")]
    public ResponseParameters? Parameters { get; init; }
}

internal sealed class TelegramMessage
{
    [JsonPropertyName("message_id")]
    public long MessageId { get; init; }
}

internal sealed class ResponseParameters
{
    [JsonPropertyName("retry_after")]
    public int? RetryAfter { get; init; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SendMessageRequest))]
[JsonSerializable(typeof(TelegramResponse))]
internal sealed partial class TelegramJsonContext : JsonSerializerContext;

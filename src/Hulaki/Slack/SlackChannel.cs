using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Hulaki.Channels;
using Hulaki.Markup;
using Hulaki.Text;

namespace Hulaki.Slack;

/// <summary>
/// Posts to a Slack channel through an
/// <see href="https://api.slack.com/messaging/webhooks">incoming webhook</see>. The recipient is
/// <see cref="Recipient.Self"/>: the webhook's channel. Markup is rendered as Slack mrkdwn.
/// </summary>
public sealed partial class SlackChannel : ChannelBase
{
    /// <summary>
    /// Slack truncates <c>text</c> after 40,000 characters and advises staying under 4,000. The
    /// limit is the hard one, counted in UTF-16 code units after escaping.
    /// </summary>
    public static CapabilityManifest Manifest { get; } = new(
        "slack",
        new TextLimit(40_000, TextCounter.Utf16CodeUnits),
        [
            new(Capability.Text, Availability.Available),
            new(Capability.Markup, Availability.Available, "mrkdwn: *bold*, _italic_, `code`, <url|text>"),
            new(Capability.Title, Availability.NotImplemented, "Sent as a bold first line; blocks are not used"),
            new(Capability.Priority, Availability.UnsupportedByPlatform),
            new(Capability.ClickAction, Availability.UnsupportedByPlatform, "The link is appended"),
            new(Capability.Images, Availability.NotImplemented),
            new(Capability.IdempotentSend, Availability.UnsupportedByPlatform),
        ]);

    private readonly HttpClient _http;
    private readonly Uri _webhook;

    /// <summary>Creates the channel.</summary>
    /// <param name="name">Registration name.</param>
    /// <param name="http">Client with no request logging: the webhook URL is the request URI. The channel does not dispose it.</param>
    /// <param name="options">Options with a webhook URL.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty, or the webhook URL is missing or not a Slack incoming webhook URL.</exception>
    public SlackChannel(string name, HttpClient http, SlackChannelOptions options)
        : base(name, Manifest, options, DefaultLimiter)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        if (!IsWebhookUrl(options.WebhookUrl))
        {
            throw new ArgumentException("WebhookUrl must be an https://hooks.slack.com/services/... URL.", nameof(options));
        }

        _http = http;
        _webhook = options.WebhookUrl;
    }

    /// <inheritdoc />
    protected override void PrepareCore(Message message, Recipient recipient, ICollection<PreparationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(issues);
        if (!recipient.IsSelf)
        {
            issues.Add(new("recipient-must-be-self", "A Slack incoming webhook posts to its own channel; use Recipient.Self."));
        }
    }

    /// <inheritdoc />
    protected override async Task<DeliveryOutcome> SendOnceAsync(Message message, Recipient recipient, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var response = await _http.PostAsJsonAsync(_webhook, new SlackMessage { Text = Render(message) }, SlackJsonContext.Default.SlackMessage, cancellationToken).ConfigureAwait(false);
        var body = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();

        // Slack answers a posted message with 200 and the body "ok"; it has no message id to return.
        if (response.StatusCode == HttpStatusCode.OK && body == "ok")
        {
            return DeliveryOutcome.Delivered();
        }

        return HttpHelpers.OutcomeFor(MapError(response, body));
    }

    /// <inheritdoc />
    protected override string CountedText(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Render(message);
    }

    internal static bool IsWebhookUrl([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] Uri? url) =>
        url is { IsAbsoluteUri: true }
        && url.Scheme == Uri.UriSchemeHttps
        && url.IsDefaultPort
        && (url.Host.Equals("hooks.slack.com", StringComparison.OrdinalIgnoreCase) || url.Host.Equals("hooks.slack-gov.com", StringComparison.OrdinalIgnoreCase))
        && url.AbsolutePath.StartsWith("/services/", StringComparison.Ordinal);

    internal static string Render(Message message)
    {
        var builder = new StringBuilder();
        if (message.Title is not null)
        {
            builder.Append('*');
            AppendEscaped(builder, message.Title);
            builder.Append("*\n");
        }

        if (message.Format == TextFormat.Markup)
        {
            AppendMrkdwn(builder, MarkupDocument.Parse(message.Text).Nodes);
        }
        else
        {
            AppendEscaped(builder, message.Text);
        }

        if (message.Link is not null)
        {
            builder.Append("\n<").Append(LinkTarget(message.Link)).Append('>');
        }

        return builder.ToString();
    }

    private static void AppendMrkdwn(StringBuilder builder, IReadOnlyList<MarkupNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode t:
                    AppendEscaped(builder, t.Value);
                    break;
                case BoldNode b:
                    builder.Append('*');
                    AppendMrkdwn(builder, b.Children);
                    builder.Append('*');
                    break;
                case ItalicNode i:
                    builder.Append('_');
                    AppendMrkdwn(builder, i.Children);
                    builder.Append('_');
                    break;
                case CodeNode c:
                    builder.Append('`');
                    AppendEscaped(builder, c.Value);
                    builder.Append('`');
                    break;
                case LinkNode l:
                    // A "|" or ">" would end the URL early, so LinkTarget percent-encodes them.
                    builder.Append('<').Append(LinkTarget(l.Target)).Append('|');
                    AppendEscaped(builder, l.Text);
                    builder.Append('>');
                    break;
                default:
                    throw new InvalidOperationException($"Unknown node {node.GetType().Name}.");
            }
        }
    }

    /// <summary>
    /// Slack reads <c>&amp;</c>, <c>&lt;</c> and <c>&gt;</c> as control characters and asks for
    /// them as HTML entities (api.slack.com/reference/surfaces/formatting#escaping). It has no
    /// escape for <c>*</c>, <c>_</c> or <c>~</c>.
    /// </summary>
    internal static void AppendEscaped(StringBuilder builder, string text)
    {
        foreach (var c in text)
        {
            _ = c switch
            {
                '&' => builder.Append("&amp;"),
                '<' => builder.Append("&lt;"),
                '>' => builder.Append("&gt;"),
                _ => builder.Append(c),
            };
        }
    }

    private static string LinkTarget(Uri target) =>
        new StringBuilder(target.AbsoluteUri).Replace("|", "%7C").Replace("<", "%3C").Replace(">", "%3E").ToString();

    private HulakiError MapError(HttpResponseMessage response, string body)
    {
        var status = response.StatusCode;
        var retryAfter = HttpHelpers.RetryAfter(response, Options.TimeProvider);

        // Slack's error bodies are bare codes such as "invalid_payload". Keep one only when it
        // looks like a code, so a proxy page never lands in PlatformCode.
        var code = ErrorCode().IsMatch(body) ? body : null;

        HulakiError Error(HulakiErrorCode hulakiCode, RetryDisposition retry, string text) =>
            new(hulakiCode, retry, text) { HttpStatus = (int)status, PlatformCode = code, RetryAfter = retryAfter };

        return code switch
        {
            "invalid_payload" or "no_text" or "too_many_attachments" or "invalid_blocks" =>
                Error(HulakiErrorCode.InvalidInput, RetryDisposition.Never, "Slack refused the message payload."),
            "channel_not_found" or "channel_is_archived" =>
                Error(HulakiErrorCode.RecipientNotFound, RetryDisposition.Never, "The webhook's channel no longer exists or is archived."),
            "no_service" or "no_service_id" or "no_team" or "team_disabled" or "invalid_token" =>
                Error(HulakiErrorCode.InvalidConfiguration, RetryDisposition.Never, "Slack refused the webhook URL."),
            "action_prohibited" or "posting_to_general_channel_denied" =>
                Error(HulakiErrorCode.PermissionDenied, RetryDisposition.Never, "The webhook may not post to its channel."),
            _ when (int)status == 429 =>
                Error(HulakiErrorCode.RateLimited, RetryDisposition.AfterDelay, "Slack rate limited the webhook."),
            _ => HttpHelpers.ErrorFor(status, retryAfter, $"Slack returned {(int)status}.") with { PlatformCode = code },
        };
    }

    // "1 message per second" per incoming webhook, with short bursts allowed
    // (api.slack.com/apis/rate-limits, incoming webhooks).
    private static RateLimiter DefaultLimiter() => new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 1,
        TokensPerPeriod = 1,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        QueueLimit = 100,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    });

    [GeneratedRegex("^[a-z_]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorCode();
}

internal sealed class SlackMessage
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

[JsonSerializable(typeof(SlackMessage))]
internal sealed partial class SlackJsonContext : JsonSerializerContext;

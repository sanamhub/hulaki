using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Hulaki.Channels;
using Hulaki.Discord.Wire;
using Hulaki.Markup;
using Hulaki.Text;

namespace Hulaki.Discord;

/// <summary>
/// Posts to a Discord channel through an incoming webhook
/// (<see href="https://discord.com/developers/docs/resources/webhook#execute-webhook">Execute Webhook</see>).
/// The recipient is <see cref="Recipient.Self"/>: the webhook's own channel. Markup is rendered as
/// Discord Markdown, and mentions are switched off so no message can ping <c>@everyone</c>.
/// </summary>
public sealed partial class DiscordChannel : ChannelBase
{
    /// <summary>
    /// Discord's limit is 2000 characters of <c>content</c>. It is counted here in UTF-16 code
    /// units, which is never fewer than Discord's count, after escaping and with the title and link.
    /// </summary>
    public static CapabilityManifest Manifest { get; } = new(
        "discord",
        new TextLimit(2000, TextCounter.Utf16CodeUnits),
        [
            new(Capability.Text, Availability.Available),
            new(Capability.Markup, Availability.Available, "Discord Markdown, text escaped"),
            new(Capability.Title, Availability.NotImplemented, "Sent as a bold first line; embeds are not used"),
            new(Capability.Priority, Availability.UnsupportedByPlatform),
            new(Capability.ClickAction, Availability.UnsupportedByPlatform, "The link is appended"),
            new(Capability.Images, Availability.NotImplemented),
            new(Capability.IdempotentSend, Availability.UnsupportedByPlatform),
            new(Capability.Delete, Availability.NotImplemented),
        ]);

    private readonly HttpClient _http;
    private readonly Uri _executeUri;

    /// <summary>Creates the channel.</summary>
    /// <param name="name">Registration name.</param>
    /// <param name="http">Client with no request logging: the webhook URL is the request URI. The channel does not dispose it.</param>
    /// <param name="options">Options with a webhook URL.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty, or the webhook URL is missing or not a Discord webhook URL.</exception>
    public DiscordChannel(string name, HttpClient http, DiscordChannelOptions options)
        : base(name, Manifest, options, DefaultLimiter)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        if (!IsWebhookUrl(options.WebhookUrl))
        {
            // Never include the URL: it is the secret.
            throw new ArgumentException("WebhookUrl must be an https://discord.com/api/webhooks/... URL.", nameof(options));
        }

        _http = http;
        _executeUri = WithWait(options.WebhookUrl);
    }

    /// <inheritdoc />
    protected override void PrepareCore(Message message, Recipient recipient, ICollection<PreparationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(issues);
        if (!recipient.IsSelf)
        {
            issues.Add(new("recipient-must-be-self", "A Discord webhook posts to its own channel; use Recipient.Self."));
        }
    }

    /// <inheritdoc />
    protected override async Task<DeliveryOutcome> SendOnceAsync(Message message, Recipient recipient, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var request = new ExecuteWebhookRequest { Content = Render(message) };
        using var response = await _http.PostAsJsonAsync(_executeUri, request, DiscordJsonContext.Default.ExecuteWebhookRequest, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return DeliveryOutcome.Delivered();
        }

        if (response.IsSuccessStatusCode)
        {
            var sent = await ReadAsync(response, DiscordJsonContext.Default.DiscordMessage, cancellationToken).ConfigureAwait(false);
            return sent?.Id is { Length: > 0 } id
                ? DeliveryOutcome.Delivered(id)
                : DeliveryOutcome.Unknown(new HulakiError(HulakiErrorCode.UpstreamFailure, RetryDisposition.ReconcileFirst, "Discord answered 2xx without a message.") { HttpStatus = (int)response.StatusCode });
        }

        var error = await ReadAsync(response, DiscordJsonContext.Default.DiscordError, cancellationToken).ConfigureAwait(false);
        return HttpHelpers.OutcomeFor(MapError(response, error));
    }

    /// <inheritdoc />
    protected override string CountedText(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Render(message);
    }

    internal static bool IsWebhookUrl([NotNullWhen(true)] Uri? url) =>
        url is { IsAbsoluteUri: true }
        && url.Scheme == Uri.UriSchemeHttps
        && url.IsDefaultPort
        && (url.Host.Equals("discord.com", StringComparison.OrdinalIgnoreCase) || url.Host.Equals("discordapp.com", StringComparison.OrdinalIgnoreCase))
        && WebhookPath().IsMatch(url.AbsolutePath);

    internal static string Render(Message message)
    {
        var builder = new StringBuilder();
        if (message.Title is not null)
        {
            builder.Append("**");
            AppendEscaped(builder, message.Title);
            builder.Append("**\n");
        }

        if (message.Format == TextFormat.Markup)
        {
            AppendMarkdown(builder, MarkupDocument.Parse(message.Text).Nodes);
        }
        else
        {
            AppendEscaped(builder, message.Text);
        }

        if (message.Link is not null)
        {
            builder.Append('\n').Append(message.Link.AbsoluteUri);
        }

        return builder.ToString();
    }

    private static void AppendMarkdown(StringBuilder builder, IReadOnlyList<MarkupNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode t:
                    AppendEscaped(builder, t.Value);
                    break;
                case BoldNode b:
                    builder.Append("**");
                    AppendMarkdown(builder, b.Children);
                    builder.Append("**");
                    break;
                case ItalicNode i:
                    builder.Append('*');
                    AppendMarkdown(builder, i.Children);
                    builder.Append('*');
                    break;
                case CodeNode c:
                    // The markup parser ends a code span at the next backtick, so the value holds none.
                    builder.Append('`').Append(c.Value).Append('`');
                    break;
                case LinkNode l:
                    builder.Append('[');
                    AppendEscaped(builder, l.Text);
                    builder.Append("](").Append(LinkTarget(l.Target)).Append(')');
                    break;
                default:
                    throw new InvalidOperationException($"Unknown node {node.GetType().Name}.");
            }
        }
    }

    /// <summary>
    /// Escapes the characters Discord Markdown gives meaning to: the task's list
    /// <c>\ * _ ~ ` | &gt;</c>, plus <c>[ ]</c> (masked links), <c>#</c> (headings) and <c>&lt;</c>
    /// (mention and timestamp syntax), so a text node always renders as the characters it holds.
    /// </summary>
    internal static void AppendEscaped(StringBuilder builder, string text)
    {
        foreach (var c in text)
        {
            if (c is '\\' or '*' or '_' or '~' or '`' or '|' or '>' or '[' or ']' or '#' or '<')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }
    }

    // A ")" or whitespace would end the link early; percent-encode them.
    private static string LinkTarget(Uri target) =>
        target.AbsoluteUri.Replace("(", "%28", StringComparison.Ordinal).Replace(")", "%29", StringComparison.Ordinal);

    private static Uri WithWait(Uri webhook)
    {
        // wait=true makes Discord answer with the message, so the outcome carries its id.
        var query = webhook.Query.Length > 1 ? webhook.Query[1..] + "&wait=true" : "wait=true";
        return new UriBuilder(webhook) { Query = query }.Uri;
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync(type, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null; // a proxy's HTML error page or an empty body; the status code decides
        }
    }

    private HulakiError MapError(HttpResponseMessage response, DiscordError? body)
    {
        var status = response.StatusCode;
        var platformCode = body?.Code?.ToString(CultureInfo.InvariantCulture);
        var retryAfter = body?.RetryAfter is double seconds and > 0
            ? TimeSpan.FromSeconds(seconds)
            : HttpHelpers.RetryAfter(response, Options.TimeProvider);

        HulakiError Error(HulakiErrorCode code, RetryDisposition retry, string text) =>
            new(code, retry, text) { HttpStatus = (int)status, PlatformCode = platformCode, RetryAfter = retryAfter };

        // Discord's "message" strings can quote the request; only the numeric code is kept.
        return (int)status switch
        {
            // 10015 Unknown Webhook, 50027 Invalid Webhook Token: the webhook was deleted or the URL is wrong.
            401 or 404 => Error(HulakiErrorCode.InvalidConfiguration, RetryDisposition.Never, "Discord refused the webhook URL."),
            403 => Error(HulakiErrorCode.PermissionDenied, RetryDisposition.Never, "The webhook may not post to its channel."),
            413 => Error(HulakiErrorCode.InvalidInput, RetryDisposition.Never, "The message is too large for Discord."),
            429 => Error(HulakiErrorCode.RateLimited, RetryDisposition.AfterDelay, "Discord rate limited the webhook."),
            _ => HttpHelpers.ErrorFor(status, retryAfter, $"Discord returned {(int)status}.") with { PlatformCode = platformCode },
        };
    }

    // Discord publishes its limits only in response headers. Webhooks are widely reported to answer
    // with X-RateLimit-Limit 5 per 2 second window; that is not documented and was not measured
    // here. The default stays at it, so a burst queues locally instead of drawing a 429.
    private static RateLimiter DefaultLimiter() => new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 5,
        TokensPerPeriod = 5,
        ReplenishmentPeriod = TimeSpan.FromSeconds(2),
        QueueLimit = 100,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    });

    [GeneratedRegex(@"^/api/(v\d+/)?webhooks/\d+/[A-Za-z0-9_\-]+/?$", RegexOptions.CultureInvariant)]
    private static partial Regex WebhookPath();
}

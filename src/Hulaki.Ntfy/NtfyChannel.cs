using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Hulaki.Channels;
using Hulaki.Markup;
using Hulaki.Ntfy.Wire;
using Hulaki.Text;

namespace Hulaki.Ntfy;

/// <summary>
/// Publishes to an <see href="https://docs.ntfy.sh/publish/">ntfy</see> topic on ntfy.sh or a
/// self-hosted server. The recipient address is the topic name. The title, priority and
/// <see cref="Message.Link"/> (as the click action) go in their own fields; markup is sent as
/// Markdown.
/// </summary>
public sealed partial class NtfyChannel : ChannelBase
{
    /// <summary>ntfy's default message limit is 4096 bytes of UTF-8 (docs.ntfy.sh/config, <c>message-size-limit</c>).</summary>
    public static CapabilityManifest Manifest { get; } = new(
        "ntfy",
        new TextLimit(4096, TextCounter.Utf8Bytes),
        [
            new(Capability.Text, Availability.Available),
            new(Capability.Title, Availability.Available),
            new(Capability.Markup, Availability.Available, "Markdown; rendered by the web app, shown as text where a client does not render it"),
            new(Capability.Priority, Availability.Available, "Low 2, Normal 3, High 4, Urgent 5"),
            new(Capability.ClickAction, Availability.Available, "Message.Link opens on tap"),
            new(Capability.Images, Availability.NotImplemented),
            new(Capability.IdempotentSend, Availability.UnsupportedByPlatform),
            new(Capability.Delete, Availability.NotImplemented),
        ]);

    private readonly HttpClient _http;
    private readonly NtfyChannelOptions _options;

    /// <summary>Creates the channel.</summary>
    /// <param name="name">Registration name.</param>
    /// <param name="http">Client for the server. The channel does not dispose it.</param>
    /// <param name="options">Options; the defaults publish anonymously to ntfy.sh.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty, or the base address is not absolute.</exception>
    public NtfyChannel(string name, HttpClient http, NtfyChannelOptions options)
        : base(name, Manifest, options, DefaultLimiterFor(options))
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        if (options.BaseAddress is not { IsAbsoluteUri: true })
        {
            throw new ArgumentException("BaseAddress must be an absolute URL.", nameof(options));
        }

        _http = http;
        _options = options;
    }

    /// <inheritdoc />
    protected override void PrepareCore(Message message, Recipient recipient, ICollection<PreparationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(issues);
        if (!TopicName().IsMatch(recipient.Address))
        {
            issues.Add(new("invalid-topic", "An ntfy topic is 1 to 64 letters, digits, '-' or '_'."));
        }
    }

    /// <inheritdoc />
    protected override async Task<DeliveryOutcome> SendOnceAsync(Message message, Recipient recipient, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(recipient);
        var body = new PublishRequest
        {
            Topic = recipient.Address,
            Message = Render(message),
            Title = message.Title,
            Priority = PriorityOf(message.Priority),
            Click = message.Link?.AbsoluteUri,
            Markdown = message.Format == TextFormat.Markup ? true : null,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseAddress)
        {
            Content = JsonContent.Create(body, NtfyJsonContext.Default.PublishRequest),
        };
        if (!string.IsNullOrEmpty(_options.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        }

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            var published = await ReadAsync(response, NtfyJsonContext.Default.PublishResponse, cancellationToken).ConfigureAwait(false);
            return published?.Id is { Length: > 0 } id
                ? DeliveryOutcome.Delivered(id)
                : DeliveryOutcome.Unknown(new HulakiError(HulakiErrorCode.UpstreamFailure, RetryDisposition.ReconcileFirst, "ntfy answered 2xx without a message.") { HttpStatus = (int)response.StatusCode });
        }

        var error = await ReadAsync(response, NtfyJsonContext.Default.NtfyError, cancellationToken).ConfigureAwait(false);
        return HttpHelpers.OutcomeFor(MapError(response, error));
    }

    /// <inheritdoc />
    /// <remarks>The title travels in its own field, so only the body counts toward the limit.</remarks>
    protected override string CountedText(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Render(message);
    }

    internal static int PriorityOf(MessagePriority priority) => priority switch
    {
        MessagePriority.Low => 2,
        MessagePriority.High => 4,
        MessagePriority.Urgent => 5,
        _ => 3,
    };

    /// <summary>Plain text goes as is, with <c>markdown</c> unset. Markup becomes Markdown with every text node escaped.</summary>
    internal static string Render(Message message)
    {
        if (message.Format != TextFormat.Markup)
        {
            return message.Text;
        }

        var builder = new StringBuilder();
        AppendMarkdown(builder, MarkupDocument.Parse(message.Text).Nodes);
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
                    builder.Append("](").Append(l.Target.AbsoluteUri.Replace("(", "%28", StringComparison.Ordinal).Replace(")", "%29", StringComparison.Ordinal)).Append(')');
                    break;
                default:
                    throw new InvalidOperationException($"Unknown node {node.GetType().Name}.");
            }
        }
    }

    // CommonMark lets a backslash escape any ASCII punctuation. These are the ones that start
    // emphasis, code, links, headings, quotes, tables, strikethrough or raw HTML.
    private static void AppendEscaped(StringBuilder builder, string text)
    {
        foreach (var c in text)
        {
            if (c is '\\' or '*' or '_' or '`' or '[' or ']' or '#' or '>' or '~' or '|' or '<')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }
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

    private HulakiError MapError(HttpResponseMessage response, NtfyError? body)
    {
        var status = (int)response.StatusCode;
        var platformCode = body?.Code?.ToString(CultureInfo.InvariantCulture);
        var retryAfter = HttpHelpers.RetryAfter(response, Options.TimeProvider);

        HulakiError Error(HulakiErrorCode code, RetryDisposition retry, string text) =>
            new(code, retry, text) { HttpStatus = status, PlatformCode = platformCode, RetryAfter = retryAfter };

        // ntfy's "error" strings are English prose; only the five-digit code is kept.
        return status switch
        {
            401 => Error(HulakiErrorCode.InvalidConfiguration, RetryDisposition.Never, "ntfy refused the access token."),
            403 => Error(HulakiErrorCode.PermissionDenied, RetryDisposition.Never, "The token may not publish to this topic."),
            413 => Error(HulakiErrorCode.InvalidInput, RetryDisposition.Never, "The message is too large for this ntfy server."),
            // 507 is the attachment or message storage quota; the message was not stored.
            507 => Error(HulakiErrorCode.InvalidInput, RetryDisposition.Never, "The ntfy server has no room for this message."),
            429 => Error(HulakiErrorCode.RateLimited, RetryDisposition.AfterDelay, "ntfy rate limited the request."),
            _ => HttpHelpers.ErrorFor(response.StatusCode, retryAfter, $"ntfy returned {status}.") with { PlatformCode = platformCode },
        };
    }

    // ntfy.sh allows a burst of 60 requests per visitor, refilled one every 5 seconds
    // (docs.ntfy.sh/config, visitor-request-limit-burst and -replenish). A self-hosted server sets
    // its own limits, so it gets no default.
    private static Func<RateLimiter>? DefaultLimiterFor(NtfyChannelOptions options) =>
        options?.BaseAddress is { IsAbsoluteUri: true } address && address.Host.Equals("ntfy.sh", StringComparison.OrdinalIgnoreCase)
            ? () => new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = 60,
                TokensPerPeriod = 1,
                ReplenishmentPeriod = TimeSpan.FromSeconds(5),
                QueueLimit = 100,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            })
            : null;

    [GeneratedRegex("^[-_A-Za-z0-9]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex TopicName();
}

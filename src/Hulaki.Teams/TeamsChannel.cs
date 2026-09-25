using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Hulaki.Channels;
using Hulaki.Markup;
using Hulaki.Teams.Wire;
using Hulaki.Text;

namespace Hulaki.Teams;

/// <summary>
/// Posts an Adaptive Card to a Microsoft Teams channel or chat through a Workflows webhook. The
/// recipient is <see cref="Recipient.Self"/>: where the flow posts. The card holds the title, the
/// text in a wrapping <c>TextBlock</c> and the link as an <c>Action.OpenUrl</c> button.
/// </summary>
public sealed partial class TeamsChannel : ChannelBase
{
    /// <summary>Teams accepts a card of about 28 KB. The limit is the whole serialized request, in UTF-8 bytes.</summary>
    public static CapabilityManifest Manifest { get; } = new(
        "teams",
        new TextLimit(28_000, TextCounter.Utf8Bytes),
        [
            new(Capability.Text, Availability.Available),
            new(Capability.Title, Availability.Available, "A bold TextBlock above the text"),
            new(Capability.Markup, Availability.Available, "Adaptive Card Markdown; code spans are sent as text"),
            new(Capability.Priority, Availability.UnsupportedByPlatform),
            new(Capability.ClickAction, Availability.Available, "An Open link button"),
            new(Capability.Images, Availability.NotImplemented),
            new(Capability.IdempotentSend, Availability.UnsupportedByPlatform),
        ]);

    private readonly HttpClient _http;
    private readonly Uri _workflow;

    /// <summary>Creates the channel.</summary>
    /// <param name="name">Registration name.</param>
    /// <param name="http">Client with no request logging: the workflow URL carries its signature. The channel does not dispose it.</param>
    /// <param name="options">Options with a Workflows URL.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty, or the URL is missing, not HTTPS, or a retired Office 365 connector URL.</exception>
    public TeamsChannel(string name, HttpClient http, TeamsChannelOptions options)
        : base(name, Manifest, options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        if (Problem(options.WorkflowUrl) is { } problem)
        {
            throw new ArgumentException(problem, nameof(options));
        }

        _http = http;
        _workflow = options.WorkflowUrl!;
    }

    /// <inheritdoc />
    protected override void PrepareCore(Message message, Recipient recipient, ICollection<PreparationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(issues);
        if (!recipient.IsSelf)
        {
            issues.Add(new("recipient-must-be-self", "A Workflows webhook posts where its flow says; use Recipient.Self."));
        }
    }

    /// <inheritdoc />
    protected override async Task<DeliveryOutcome> SendOnceAsync(Message message, Recipient recipient, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var response = await _http.PostAsJsonAsync(_workflow, Card(message), TeamsJsonContext.Default.WorkflowMessage, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            // Workflows answers 202: the flow was triggered and posts the card on its own time.
            return DeliveryOutcome.Accepted();
        }

        var code = await ErrorCodeAsync(response, cancellationToken).ConfigureAwait(false);
        return HttpHelpers.OutcomeFor(MapError(response, code));
    }

    /// <inheritdoc />
    /// <remarks>The card's JSON is what Teams measures, so it is what the limit counts.</remarks>
    protected override string CountedText(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return JsonSerializer.Serialize(Card(message), TeamsJsonContext.Default.WorkflowMessage);
    }

    /// <summary>Null when <paramref name="url"/> can be used, else why not. Never quotes the URL.</summary>
    internal static string? Problem(Uri? url)
    {
        if (url is not { IsAbsoluteUri: true } || url.Scheme != Uri.UriSchemeHttps)
        {
            return "WorkflowUrl must be an absolute HTTPS URL.";
        }

        var host = url.Host;
        var connector = (host.Equals("outlook.office.com", StringComparison.OrdinalIgnoreCase) && url.AbsolutePath.StartsWith("/webhook", StringComparison.OrdinalIgnoreCase))
            || host.EndsWith(".webhook.office.com", StringComparison.OrdinalIgnoreCase);
        return connector
            ? "This is an Office 365 connector URL; Microsoft retired those on 2026-05-22. In Teams, create a Workflows flow from the template 'Post to a channel when a webhook request is received' and use its URL."
            : null;
    }

    internal static WorkflowMessage Card(Message message)
    {
        var body = new List<TextBlock>();
        if (message.Title is not null)
        {
            var title = new StringBuilder();
            AppendEscaped(title, message.Title);
            body.Add(new TextBlock { Text = title.ToString(), Weight = "Bolder", Size = "Medium" });
        }

        var text = new StringBuilder();
        if (message.Format == TextFormat.Markup)
        {
            AppendMarkdown(text, MarkupDocument.Parse(message.Text).Nodes);
        }
        else
        {
            AppendEscaped(text, message.Text);
        }

        body.Add(new TextBlock { Text = text.ToString() });
        var card = new AdaptiveCard
        {
            Body = body,
            Actions = message.Link is null ? null : [new OpenUrlAction { Title = "Open link", Url = message.Link.AbsoluteUri }],
        };
        return new WorkflowMessage { Attachments = [new CardAttachment { Content = card }] };
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
                    builder.Append('_');
                    AppendMarkdown(builder, i.Children);
                    builder.Append('_');
                    break;
                case CodeNode c:
                    // Adaptive Card Markdown has no code spans.
                    AppendEscaped(builder, c.Value);
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

    /// <summary>
    /// Backslash-escapes the characters Adaptive Card Markdown gives meaning to. That Teams honours
    /// CommonMark backslash escapes here is from the Markdown spec, not yet checked in a live
    /// Teams client; the release checklist covers it.
    /// </summary>
    internal static void AppendEscaped(StringBuilder builder, string text)
    {
        foreach (var c in text)
        {
            if (c is '\\' or '*' or '_' or '[' or ']')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync(TeamsJsonContext.Default.WorkflowError, cancellationToken).ConfigureAwait(false);
            return error?.Error?.Code is { } code && Identifier().IsMatch(code) ? code : null;
        }
        catch (JsonException)
        {
            return null; // a proxy's HTML error page or an empty body; the status code decides
        }
    }

    private HulakiError MapError(HttpResponseMessage response, string? code)
    {
        var status = (int)response.StatusCode;
        var retryAfter = HttpHelpers.RetryAfter(response, Options.TimeProvider);

        HulakiError Error(HulakiErrorCode hulakiCode, RetryDisposition retry, string text) =>
            new(hulakiCode, retry, text) { HttpStatus = status, PlatformCode = code, RetryAfter = retryAfter };

        return status switch
        {
            400 => Error(HulakiErrorCode.InvalidInput, RetryDisposition.Never, "The workflow refused the card."),
            401 or 403 => Error(HulakiErrorCode.InvalidConfiguration, RetryDisposition.Never, "The workflow refused the URL's signature."),
            404 => Error(HulakiErrorCode.InvalidConfiguration, RetryDisposition.Never, "The workflow does not exist or is turned off."),
            413 => Error(HulakiErrorCode.InvalidInput, RetryDisposition.Never, "The card is too large."),
            429 => Error(HulakiErrorCode.RateLimited, RetryDisposition.AfterDelay, "The workflow is being throttled."),
            _ => HttpHelpers.ErrorFor(response.StatusCode, retryAfter, $"The workflow returned {status}.") with { PlatformCode = code },
        };
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
}

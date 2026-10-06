using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FreeTierMail;
using FreeTierMail.Smtp;
using Hulaki.Channels;
using Hulaki.Markup;
using Hulaki.Text;

namespace Hulaki.Email;

/// <summary>
/// Sends email through <see href="https://github.com/sanamhub/freetiermail">FreeTierMail</see>.
/// With <see cref="EmailChannelOptions.UseFreeTierMail"/> off, the channel sends through the one
/// SMTP server in its options; on, it sends through a shared <see cref="FreeTierMailer"/>, which
/// picks an account (SMTP or an HTTP API such as Resend) by the quota each has left and fails over.
/// The recipient address is an email address. Every message has a plain text part and an HTML
/// part; markup is rendered into the HTML part. The title is the subject.
/// </summary>
public sealed class EmailChannel : ChannelBase
{
    /// <summary>
    /// Providers cap the whole message, commonly at 10 to 40 MB. The manifest's 1,000,000 UTF-16
    /// code units only stops a runaway body.
    /// </summary>
    public static CapabilityManifest Manifest { get; } = new(
        "email",
        new TextLimit(1_000_000, TextCounter.Utf16CodeUnits),
        [
            new(Capability.Text, Availability.Available),
            new(Capability.Title, Availability.Available, "The subject; without a title, the first line of the text"),
            new(Capability.Markup, Availability.Available, "Rendered into the HTML part"),
            new(Capability.Priority, Availability.Available, "High and Urgent are critical mail: providers marked for it first, and the quota reserve"),
            new(Capability.ClickAction, Availability.UnsupportedByPlatform, "The link is appended"),
            new(Capability.Images, Availability.NotImplemented),
            new(Capability.IdempotentSend, Availability.UnsupportedByPlatform),
        ]);

    private static readonly TextLimit SubjectLimit = new(78, TextCounter.Graphemes);

    private readonly EmailAddress _from;

    /// <summary>
    /// Creates a channel that sends through the SMTP server in <paramref name="options"/>.
    /// <see cref="EmailChannelOptions.UseFreeTierMail"/> must be off.
    /// </summary>
    /// <param name="name">Registration name.</param>
    /// <param name="options">Options with a host and a sender.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> or the host is empty, the port is out of range, a remote host has no login, the sender is not an email address, or <see cref="EmailChannelOptions.UseFreeTierMail"/> is on.</exception>
    public EmailChannel(string name, EmailChannelOptions options)
        : this(name, SmtpMailer(name, options), options)
    {
        if (options.UseFreeTierMail)
        {
            throw new ArgumentException("UseFreeTierMail is on: pass the FreeTierMailer to send through.", nameof(options));
        }
    }

    /// <summary>
    /// Creates a channel that sends through <paramref name="mailer"/>. The SMTP settings in
    /// <paramref name="options"/> are not used.
    /// </summary>
    /// <param name="name">Registration name.</param>
    /// <param name="mailer">The mailer, with its providers. Keep one per set of accounts: it holds the quota counts.</param>
    /// <param name="options">Options with a sender.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty, or the sender is not an email address.</exception>
    public EmailChannel(string name, FreeTierMailer mailer, EmailChannelOptions options)
        : base(name, Manifest, options)
    {
        ArgumentNullException.ThrowIfNull(mailer);
        ArgumentNullException.ThrowIfNull(options);
        _from = TryParseAddress(options.From, out var from)
            ? new EmailAddress(from.Address, from.DisplayName.Length == 0 ? null : from.DisplayName)
            : throw new ArgumentException("From must be an email address.", nameof(options));
        Mailer = mailer;
    }

    internal FreeTierMailer Mailer { get; }

    /// <inheritdoc />
    protected override void PrepareCore(Message message, Recipient recipient, ICollection<PreparationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(issues);
        if (!TryParseAddress(recipient.Address, out _))
        {
            issues.Add(new("invalid-address", "The recipient is not an email address."));
        }
    }

    /// <inheritdoc />
    protected override async Task<DeliveryOutcome> SendOnceAsync(Message message, Recipient recipient, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(recipient);
        var result = await Mailer.SendAsync(Build(message, recipient), cancellationToken).ConfigureAwait(false);
        return Map(result);
    }

    /// <inheritdoc />
    /// <remarks>The title is the subject, so only the body counts.</remarks>
    protected override string CountedText(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return PlainBody(message);
    }

    /// <summary>Parses one mailbox with a local part and a domain.</summary>
    internal static bool TryParseAddress(string? text, [NotNullWhen(true)] out MailAddress? address)
    {
        address = null;
        if (string.IsNullOrWhiteSpace(text) || !MailAddress.TryCreate(text, out var parsed) || parsed.User.Length == 0 || parsed.Host.Length == 0)
        {
            return false;
        }

        address = parsed;
        return true;
    }

    internal EmailMessage Build(Message message, Recipient recipient)
    {
        var to = TryParseAddress(recipient.Address, out var parsed)
            ? new EmailAddress(parsed.Address)
            : throw new ArgumentException("The recipient is not an email address.", nameof(recipient));
        return new EmailMessage(_from, [to], message.Title ?? Subject(PlainBody(message)))
        {
            TextBody = PlainBody(message),
            HtmlBody = HtmlBody(message),
            Priority = message.Priority is MessagePriority.High or MessagePriority.Urgent ? EmailPriority.Critical : EmailPriority.Normal,
        };
    }

    /// <summary>
    /// Maps the mailer's answer. The mailer already tried every provider it could, so a failure is
    /// retried only when a later try can go differently: a throttle, a quota or an outage.
    /// Provider reasons are not copied: some quote the server's text, which can hold the address.
    /// </summary>
    internal static DeliveryOutcome Map(FreeTierMail.SendResult result)
    {
        var last = result.Attempts.Count == 0 ? null : result.Attempts[^1];
        HulakiError Error(HulakiErrorCode code, RetryDisposition retry, string text) => new(code, retry, text) { PlatformCode = last?.Provider };

        if (result.Status == FreeTierMail.SendStatus.Sent)
        {
            return DeliveryOutcome.Accepted(result.ProviderMessageId);
        }

        if (result.Status == FreeTierMail.SendStatus.Unknown)
        {
            return DeliveryOutcome.Unknown(Error(HulakiErrorCode.AmbiguousOutcome, RetryDisposition.ReconcileFirst, "A provider may have sent the message; the answer was lost."));
        }

        if (result.Suppressed)
        {
            return DeliveryOutcome.Failed(new HulakiError(HulakiErrorCode.RecipientBlocked, RetryDisposition.Never, "The address bounced or complained before, so no provider was tried."));
        }

        var outcomes = result.Attempts.Select(a => a.Outcome).ToArray();
        if (outcomes.Contains(ProviderOutcome.RecipientRejected))
        {
            return DeliveryOutcome.Failed(Error(HulakiErrorCode.RecipientNotFound, RetryDisposition.Never, "A provider refused the recipient or the message."));
        }

        if (outcomes.Length > 0 && outcomes.All(o => o == ProviderOutcome.ProviderFault))
        {
            return DeliveryOutcome.Failed(Error(HulakiErrorCode.InvalidConfiguration, RetryDisposition.Never, "Every provider refused the account, login or sender; check their settings."));
        }

        if (outcomes.Length == 0 || outcomes.All(o => o is ProviderOutcome.Throttled or ProviderOutcome.QuotaExhausted or ProviderOutcome.ProviderFault))
        {
            return DeliveryOutcome.Failed(Error(HulakiErrorCode.RateLimited, RetryDisposition.AfterDelay, "No provider has quota left right now."));
        }

        return DeliveryOutcome.Failed(Error(HulakiErrorCode.UpstreamFailure, RetryDisposition.AfterDelay, "No provider could take the message."));
    }

    internal static string PlainBody(Message message)
    {
        var text = message.Format == TextFormat.Markup ? MarkupDocument.Parse(message.Text).ToPlainText() : message.Text;
        return message.Link is null ? text : text + "\n\n" + message.Link.AbsoluteUri;
    }

    internal static string HtmlBody(Message message)
    {
        var builder = new StringBuilder("<!DOCTYPE html><html><body><p>");
        if (message.Format == TextFormat.Markup)
        {
            AppendHtml(builder, MarkupDocument.Parse(message.Text).Nodes);
        }
        else
        {
            AppendText(builder, message.Text);
        }

        builder.Append("</p>");
        if (message.Link is not null)
        {
            var link = WebUtility.HtmlEncode(message.Link.AbsoluteUri);
            builder.Append("<p><a href=\"").Append(link).Append("\">").Append(link).Append("</a></p>");
        }

        return builder.Append("</body></html>").ToString();
    }

    // One SMTP provider named after the channel. The SMTP code lives in FreeTierMail.Smtp, so both
    // modes share one implementation of TLS, login and reply mapping.
    private static FreeTierMailer SmtpMailer(string name, EmailChannelOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Host, "options.Host");
        var smtp = new SmtpProvider(new SmtpOptions
        {
            Name = name,
            Host = options.Host,
            Port = options.Port,
            Username = options.Username ?? string.Empty,
            ApiKey = options.Password ?? string.Empty,
        });
        return new FreeTierMailer([smtp], new FreeTierMailerOptions { TimeProvider = options.TimeProvider });
    }

    private static void AppendHtml(StringBuilder builder, IReadOnlyList<MarkupNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode t:
                    AppendText(builder, t.Value);
                    break;
                case BoldNode b:
                    builder.Append("<strong>");
                    AppendHtml(builder, b.Children);
                    builder.Append("</strong>");
                    break;
                case ItalicNode i:
                    builder.Append("<em>");
                    AppendHtml(builder, i.Children);
                    builder.Append("</em>");
                    break;
                case CodeNode c:
                    builder.Append("<code>").Append(WebUtility.HtmlEncode(c.Value)).Append("</code>");
                    break;
                case LinkNode l:
                    builder.Append("<a href=\"").Append(WebUtility.HtmlEncode(l.Target.AbsoluteUri)).Append("\">");
                    AppendText(builder, l.Text);
                    builder.Append("</a>");
                    break;
                default:
                    throw new InvalidOperationException($"Unknown node {node.GetType().Name}.");
            }
        }
    }

    // Encodes, then keeps line breaks, which HTML would otherwise fold into spaces.
    private static void AppendText(StringBuilder builder, string text) =>
        builder.Append(WebUtility.HtmlEncode(text).ReplaceLineEndings("<br>"));

    private static string Subject(string body)
    {
        var firstLine = body.AsSpan().Trim();
        var end = firstLine.IndexOfAny('\r', '\n');
        return TextFit.Truncate((end < 0 ? firstLine : firstLine[..end]).ToString(), SubjectLimit);
    }
}

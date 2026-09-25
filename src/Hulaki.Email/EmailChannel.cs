using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hulaki.Channels;
using Hulaki.Markup;
using Hulaki.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Utils;

namespace Hulaki.Email;

/// <summary>
/// Sends email through an SMTP submission server with MailKit, one connection per send. The
/// recipient address is an email address. Every message has a plain text part and an HTML part;
/// markup is rendered into the HTML part. The title is the subject.
/// </summary>
public sealed class EmailChannel : ChannelBase
{
    /// <summary>
    /// SMTP has no text limit Hulaki can check; servers cap the whole message, commonly at 10 to 35
    /// MB. The manifest's 1,000,000 UTF-16 code units only stops a runaway body.
    /// </summary>
    public static CapabilityManifest Manifest { get; } = new(
        "email",
        new TextLimit(1_000_000, TextCounter.Utf16CodeUnits),
        [
            new(Capability.Text, Availability.Available),
            new(Capability.Title, Availability.Available, "The subject; without a title the first line is"),
            new(Capability.Markup, Availability.Available, "Rendered into the HTML part"),
            new(Capability.Priority, Availability.Available, "Priority, Importance and X-Priority headers"),
            new(Capability.ClickAction, Availability.UnsupportedByPlatform, "The link is appended"),
            new(Capability.Images, Availability.NotImplemented),
            new(Capability.IdempotentSend, Availability.UnsupportedByPlatform),
        ]);

    private static readonly TextLimit SubjectLimit = new(78, TextCounter.Graphemes);

    private readonly EmailChannelOptions _options;
    private readonly MailboxAddress _from;
    private readonly SecureSocketOptions _security;
    private readonly Func<ISmtpSession> _sessions;

    /// <summary>Creates the channel.</summary>
    /// <param name="name">Registration name.</param>
    /// <param name="options">Options with a host and a sender.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> or the host is empty, the port is out of range, or the sender is not an email address.</exception>
    public EmailChannel(string name, EmailChannelOptions options)
        : this(name, options, static () => new MailKitSmtpSession())
    {
    }

    internal EmailChannel(string name, EmailChannelOptions options, Func<ISmtpSession> sessions)
        : base(name, Manifest, options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Host, "options.Host");
        if (options.Port is < 1 or > 65535)
        {
            throw new ArgumentException("Port must be between 1 and 65535.", nameof(options));
        }

        _from = TryParseAddress(options.From, out var from)
            ? from
            : throw new ArgumentException("From must be an email address.", nameof(options));
        _options = options;
        _security = SecurityFor(options.Host, options.Port);
        _sessions = sessions;
    }

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
        using var mime = Build(message, recipient);
        using var session = _sessions();

        if (await ConnectAsync(session, cancellationToken).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        try
        {
            await session.SendAsync(mime, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SmtpCommandException ex)
        {
            // The server answered: whatever it said, it said it about this message.
            return DeliveryOutcome.Failed(ErrorFor(ex));
        }
        catch (Exception ex) when (ex is IOException or SocketException or SmtpProtocolException or TimeoutException or OperationCanceledException)
        {
            // The connection broke after MAIL FROM went out. MailKit does not say whether DATA was
            // answered, so the message may have been queued.
            return DeliveryOutcome.Unknown(new HulakiError(HulakiErrorCode.AmbiguousOutcome, RetryDisposition.ReconcileFirst, "The SMTP connection failed while sending."));
        }

        await DisconnectQuietlyAsync(session, cancellationToken).ConfigureAwait(false);
        return DeliveryOutcome.Accepted(mime.MessageId);
    }

    /// <inheritdoc />
    /// <remarks>The title is the subject, so only the body counts.</remarks>
    protected override string CountedText(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return PlainBody(message);
    }

    /// <summary>
    /// Parses one mailbox with a domain. MimeKit alone also accepts a bare local part such as
    /// <c>alerts</c>, which no submission server can route.
    /// </summary>
    internal static bool TryParseAddress(string? text, [NotNullWhen(true)] out MailboxAddress? address)
    {
        address = null;
        if (text is null || !MailboxAddress.TryParse(text, out var parsed) || parsed.Domain.Length == 0 || parsed.LocalPart.Length == 0)
        {
            return false;
        }

        address = parsed;
        return true;
    }

    internal static SecureSocketOptions SecurityFor(string host, int port) => port switch
    {
        465 => SecureSocketOptions.SslOnConnect,
        _ when IsLoopback(host) => SecureSocketOptions.StartTlsWhenAvailable,
        _ => SecureSocketOptions.StartTls,
    };

    internal MimeMessage Build(Message message, Recipient recipient)
    {
        var mime = new MimeMessage
        {
            Subject = message.Title ?? Subject(PlainBody(message)),
            Date = Options.TimeProvider.GetUtcNow(),
            // MimeKit's default id uses the machine's host name; the sender's domain says less.
            MessageId = MimeUtils.GenerateMessageId(_from.Domain),
        };
        mime.From.Add(_from);
        mime.To.Add(TryParseAddress(recipient.Address, out var to) ? to : throw new ArgumentException("The recipient is not an email address.", nameof(recipient)));
        SetPriority(mime, message.Priority);
        mime.Body = new BodyBuilder { TextBody = PlainBody(message), HtmlBody = HtmlBody(message) }.ToMessageBody();
        return mime;
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

    private static void SetPriority(MimeMessage mime, MessagePriority priority)
    {
        // Clients read different headers: Outlook Importance and X-Priority, others Priority.
        (mime.Priority, mime.Importance, mime.XPriority) = priority switch
        {
            MessagePriority.Low => (MimeKit.MessagePriority.NonUrgent, MessageImportance.Low, XMessagePriority.Low),
            MessagePriority.High => (MimeKit.MessagePriority.Urgent, MessageImportance.High, XMessagePriority.High),
            MessagePriority.Urgent => (MimeKit.MessagePriority.Urgent, MessageImportance.High, XMessagePriority.Highest),
            _ => (MimeKit.MessagePriority.Normal, MessageImportance.Normal, XMessagePriority.Normal),
        };
    }

    private static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));

    /// <summary>Connects and authenticates. Returns null on success, or the outcome of a failure, before any message byte was sent.</summary>
    private async Task<DeliveryOutcome?> ConnectAsync(ISmtpSession session, CancellationToken cancellationToken)
    {
        try
        {
            await session.ConnectAsync(_options.Host, _options.Port, _security, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(_options.Username))
            {
                await session.AuthenticateAsync(_options.Username, _options.Password ?? string.Empty, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AuthenticationException)
        {
            return Refused(HulakiErrorCode.InvalidConfiguration, "The SMTP server refused the credentials.");
        }
        catch (SslHandshakeException)
        {
            return Refused(HulakiErrorCode.InvalidConfiguration, "The TLS handshake with the SMTP server failed.");
        }
        catch (NotSupportedException)
        {
            // MailKit throws this when the server offers no STARTTLS, or no mechanism for the credentials.
            return Refused(HulakiErrorCode.InvalidConfiguration, "The SMTP server does not offer STARTTLS or a usable login.");
        }
        catch (SmtpCommandException ex)
        {
            return DeliveryOutcome.Failed(ErrorFor(ex));
        }
        catch (Exception ex) when (ex is IOException or SocketException or SmtpProtocolException or TimeoutException or OperationCanceledException)
        {
            // No message was sent on this connection, so another attempt cannot duplicate it.
            return DeliveryOutcome.Failed(new HulakiError(HulakiErrorCode.UpstreamFailure, RetryDisposition.AfterDelay, "Could not connect to the SMTP server."));
        }

        static DeliveryOutcome Refused(HulakiErrorCode code, string text) => DeliveryOutcome.Failed(new HulakiError(code, RetryDisposition.Never, text));
    }

    private static async Task DisconnectQuietlyAsync(ISmtpSession session, CancellationToken cancellationToken)
    {
        try
        {
            await session.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or SmtpProtocolException or SmtpCommandException or TimeoutException or OperationCanceledException)
        {
            // The server already accepted the message; a failed QUIT does not change that.
        }
    }

    /// <summary>Maps an SMTP reply by class (RFC 5321 section 4.2.1) and by the command MailKit says it answered.</summary>
    internal static HulakiError ErrorFor(SmtpCommandException ex)
    {
        var status = (int)ex.StatusCode;
        var platformCode = status.ToString(CultureInfo.InvariantCulture);

        // The server's text can quote the address; only the status code is kept.
        HulakiError Error(HulakiErrorCode code, RetryDisposition retry, string text) => new(code, retry, text) { PlatformCode = platformCode };

        if (status is >= 400 and < 500)
        {
            // 4yz is transient by definition: the server did not take the message and asks for a retry.
            return Error(HulakiErrorCode.UpstreamFailure, RetryDisposition.AfterDelay, $"The SMTP server deferred the message ({status}).");
        }

        return ex.ErrorCode switch
        {
            SmtpErrorCode.RecipientNotAccepted when status is 550 or 551 or 553 =>
                Error(HulakiErrorCode.RecipientNotFound, RetryDisposition.Never, "The mailbox does not exist or is unavailable."),
            SmtpErrorCode.RecipientNotAccepted =>
                Error(HulakiErrorCode.UpstreamFailure, RetryDisposition.Never, $"The SMTP server refused the recipient ({status})."),
            SmtpErrorCode.SenderNotAccepted =>
                Error(HulakiErrorCode.InvalidConfiguration, RetryDisposition.Never, "The SMTP server refused the sender address."),
            SmtpErrorCode.MessageNotAccepted when status == 552 =>
                Error(HulakiErrorCode.InvalidInput, RetryDisposition.Never, "The message is larger than the SMTP server accepts."),
            SmtpErrorCode.MessageNotAccepted =>
                Error(HulakiErrorCode.ContentRejected, RetryDisposition.Never, $"The SMTP server rejected the message ({status})."),
            _ when status is 530 or 534 or 535 or 538 =>
                Error(HulakiErrorCode.InvalidConfiguration, RetryDisposition.Never, "The SMTP server requires different authentication."),
            _ => Error(HulakiErrorCode.UpstreamFailure, RetryDisposition.Never, $"The SMTP server refused the command ({status})."),
        };
    }
}

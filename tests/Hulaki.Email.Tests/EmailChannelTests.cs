using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Hulaki.Channels;
using Hulaki.Tests;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using MimeKit;
using Xunit;

namespace Hulaki.Email.Tests;

/// <summary>
/// Reply codes follow RFC 5321 section 4.2 and MailKit's SmtpCommandException. Addresses use the
/// example.org domain reserved by RFC 2606.
/// </summary>
public sealed class EmailChannelTests
{
    internal const string To = "reader@example.org";
    private const string Password = "TEST-smtp-password_0000";

    private static EmailChannelOptions Options(FakeTimeProvider? time = null) => new()
    {
        Host = "smtp.example.org",
        Username = "alerts@example.org",
        Password = Password,
        From = "Route alerts <alerts@example.org>",
        TimeProvider = time ?? new FakeTimeProvider(),
    };

    private static (EmailChannel Channel, FakeSmtpServer Server) Create(EmailChannelOptions? options = null)
    {
        var server = new FakeSmtpServer();
        return (new EmailChannel("mail", options ?? Options(), server.Open), server);
    }

    private static SmtpCommandException Reply(SmtpErrorCode code, int status) =>
        new(code, (SmtpStatusCode)status, "server text quoting reader@example.org and the body");

    [Fact]
    public async Task Sends_with_starttls_on_587_and_logs_in()
    {
        var (channel, server) = Create();

        var outcome = await channel.SendAsync(new Message("Rain warning"), new Recipient(To), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Accepted, outcome.Status);
        Assert.Equal(("smtp.example.org", 587, SecureSocketOptions.StartTls), Assert.Single(server.Connections));
        Assert.Equal(("alerts@example.org", Password), Assert.Single(server.Logins));
        var sent = Assert.Single(server.Sent);
        Assert.Equal(sent.MessageId, outcome.PlatformMessageId);
        Assert.EndsWith("@example.org", sent.MessageId, StringComparison.Ordinal);
        Assert.Equal(1, server.Disconnects);
    }

    [Theory]
    [InlineData("smtp.example.org", 465, SecureSocketOptions.SslOnConnect)]
    [InlineData("smtp.example.org", 25, SecureSocketOptions.StartTls)]
    [InlineData("localhost", 1025, SecureSocketOptions.StartTlsWhenAvailable)]
    [InlineData("127.0.0.1", 25, SecureSocketOptions.StartTlsWhenAvailable)]
    [InlineData("::1", 25, SecureSocketOptions.StartTlsWhenAvailable)]
    [InlineData("localhost", 465, SecureSocketOptions.SslOnConnect)]
    public void Tls_mode_follows_the_port_and_loopback(string host, int port, SecureSocketOptions expected) =>
        Assert.Equal(expected, EmailChannel.SecurityFor(host, port));

    [Fact]
    public async Task No_username_means_no_login()
    {
        var options = Options();
        options.Username = null;
        options.Password = null;
        var (channel, server) = Create(options);

        await channel.SendAsync(new Message("x"), new Recipient(To), TestContext.Current.CancellationToken);

        Assert.Empty(server.Logins);
    }

    [Fact]
    public async Task The_message_has_a_subject_both_parts_and_priority_headers()
    {
        var (channel, server) = Create();
        var message = new Message("**Red** <b>alert</b> & [DHM](https://dhm.gov.np/?a=1&b=2)\nsecond line")
        {
            Format = TextFormat.Markup,
            Title = "Route alert: Pokhara to Beni",
            Priority = MessagePriority.Urgent,
            Link = new Uri("https://example.org/watch/42"),
        };

        await channel.SendAsync(message, new Recipient(To), TestContext.Current.CancellationToken);

        var sent = Assert.Single(server.Sent);
        Assert.Equal("Route alert: Pokhara to Beni", sent.Subject);
        Assert.Equal("alerts@example.org", sent.From.Mailboxes.Single().Address);
        Assert.Equal(To, sent.To.Mailboxes.Single().Address);
        Assert.Equal(MimeKit.MessagePriority.Urgent, sent.Priority);
        Assert.Equal(MessageImportance.High, sent.Importance);
        Assert.Equal(XMessagePriority.Highest, sent.XPriority);
        Assert.Equal("Red <b>alert</b> & DHM (https://dhm.gov.np/?a=1&b=2)\nsecond line\n\nhttps://example.org/watch/42", sent.TextBody!.ReplaceLineEndings("\n").TrimEnd());
        Assert.Contains(
            "<p><strong>Red</strong> &lt;b&gt;alert&lt;/b&gt; &amp; <a href=\"https://dhm.gov.np/?a=1&amp;b=2\">DHM</a><br>second line</p><p><a href=\"https://example.org/watch/42\">https://example.org/watch/42</a></p>",
            sent.HtmlBody!,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_title_the_subject_is_the_first_line_cut_to_78()
    {
        var (channel, _) = Create();

        using var shortOne = channel.Build(new Message("  Road closed at Beni\nDetails follow"), new Recipient(To));
        using var longOne = channel.Build(new Message(new string('x', 100)), new Recipient(To));

        Assert.Equal("Road closed at Beni", shortOne.Subject);
        Assert.Equal(78, longOne.Subject!.Length);
        Assert.EndsWith("…", longOne.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public void Plain_text_is_html_encoded_in_the_html_part() =>
        Assert.Equal("<!DOCTYPE html><html><body><p>a &lt;script&gt; **b**</p></body></html>", EmailChannel.HtmlBody(new Message("a <script> **b**")));

    public static TheoryData<SmtpErrorCode, int, HulakiErrorCode, RetryDisposition> Replies() => new()
    {
        { SmtpErrorCode.RecipientNotAccepted, 550, HulakiErrorCode.RecipientNotFound, RetryDisposition.Never },
        { SmtpErrorCode.RecipientNotAccepted, 551, HulakiErrorCode.RecipientNotFound, RetryDisposition.Never },
        { SmtpErrorCode.RecipientNotAccepted, 553, HulakiErrorCode.RecipientNotFound, RetryDisposition.Never },
        { SmtpErrorCode.RecipientNotAccepted, 552, HulakiErrorCode.UpstreamFailure, RetryDisposition.Never },
        { SmtpErrorCode.SenderNotAccepted, 553, HulakiErrorCode.InvalidConfiguration, RetryDisposition.Never },
        { SmtpErrorCode.MessageNotAccepted, 554, HulakiErrorCode.ContentRejected, RetryDisposition.Never },
        { SmtpErrorCode.MessageNotAccepted, 552, HulakiErrorCode.InvalidInput, RetryDisposition.Never },
        { SmtpErrorCode.UnexpectedStatusCode, 530, HulakiErrorCode.InvalidConfiguration, RetryDisposition.Never },
        { SmtpErrorCode.UnexpectedStatusCode, 502, HulakiErrorCode.UpstreamFailure, RetryDisposition.Never },
        { SmtpErrorCode.RecipientNotAccepted, 450, HulakiErrorCode.UpstreamFailure, RetryDisposition.AfterDelay },
        { SmtpErrorCode.MessageNotAccepted, 451, HulakiErrorCode.UpstreamFailure, RetryDisposition.AfterDelay },
        { SmtpErrorCode.UnexpectedStatusCode, 421, HulakiErrorCode.UpstreamFailure, RetryDisposition.AfterDelay },
    };

    [Theory]
    [MemberData(nameof(Replies))]
    public void Smtp_replies_map_by_class_and_command(SmtpErrorCode command, int status, HulakiErrorCode code, RetryDisposition retry)
    {
        var error = EmailChannel.ErrorFor(Reply(command, status));

        Assert.Equal(code, error.Code);
        Assert.Equal(retry, error.Retry);
        Assert.Equal(status.ToString(System.Globalization.CultureInfo.InvariantCulture), error.PlatformCode);
        Assert.DoesNotContain("reader@example.org", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("server text", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_550_for_the_recipient_fails_once_as_recipient_not_found()
    {
        var (channel, server) = Create();
        server.FailSend(Reply(SmtpErrorCode.RecipientNotAccepted, 550));

        var outcome = await channel.SendAsync(new Message("x"), new Recipient(To), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Failed, outcome.Status);
        Assert.Equal(HulakiErrorCode.RecipientNotFound, outcome.Error!.Code);
        Assert.Single(server.Connections);
    }

    [Fact]
    public async Task A_4xx_reply_is_retried_on_a_new_connection()
    {
        var time = new FakeTimeProvider();
        var (channel, server) = Create(Options(time));
        server.FailSend(Reply(SmtpErrorCode.MessageNotAccepted, 451));

        var outcome = await FakeClock.RunAsync(time, () => channel.SendAsync(new Message("x"), new Recipient(To), TestContext.Current.CancellationToken));

        Assert.Equal(DeliveryStatus.Accepted, outcome.Status);
        Assert.Equal(2, outcome.Attempts);
        Assert.Equal(2, server.Connections.Count);
        Assert.Single(server.Sent);
    }

    [Fact]
    public async Task A_refused_connection_is_retried()
    {
        var time = new FakeTimeProvider();
        var (channel, server) = Create(Options(time));
        server.FailConnect(new SocketException((int)SocketError.ConnectionRefused));

        var outcome = await FakeClock.RunAsync(time, () => channel.SendAsync(new Message("x"), new Recipient(To), TestContext.Current.CancellationToken));

        Assert.Equal(DeliveryStatus.Accepted, outcome.Status);
        Assert.Equal(2, outcome.Attempts);
    }

    public static TheoryData<Exception, HulakiErrorCode> ConnectFailures() => new()
    {
        { new AuthenticationException("535 5.7.8 bad credentials for alerts@example.org"), HulakiErrorCode.InvalidConfiguration },
        { new SslHandshakeException("certificate mismatch"), HulakiErrorCode.InvalidConfiguration },
        { new NotSupportedException("The SMTP server does not support STARTTLS."), HulakiErrorCode.InvalidConfiguration },
    };

    [Theory]
    [MemberData(nameof(ConnectFailures))]
    public async Task Configuration_failures_while_connecting_are_not_retried(Exception failure, HulakiErrorCode code)
    {
        var (channel, server) = Create();
        server.FailConnect(failure);

        var outcome = await channel.SendAsync(new Message("x"), new Recipient(To), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Failed, outcome.Status);
        Assert.Equal(code, outcome.Error!.Code);
        Assert.Equal(RetryDisposition.Never, outcome.Error.Retry);
        Assert.DoesNotContain("example.org", outcome.Error.Message, StringComparison.Ordinal);
        Assert.Empty(server.Sent);
    }

    [Fact]
    public async Task A_connection_lost_while_sending_is_unknown_and_not_resent()
    {
        var (channel, server) = Create();
        server.FailSend(new IOException("connection reset"));

        var outcome = await channel.SendAsync(new Message("x"), new Recipient(To), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Unknown, outcome.Status);
        Assert.Equal(HulakiErrorCode.AmbiguousOutcome, outcome.Error!.Code);
        Assert.Single(server.Connections);
    }

    [Fact]
    public async Task Resend_unknown_opts_into_a_second_attempt()
    {
        var time = new FakeTimeProvider();
        var options = Options(time);
        options.Retry = SendRetryPolicy.Default with { ResendUnknown = true };
        var (channel, server) = Create(options);
        server.FailSend(new SmtpProtocolException("unexpected end of stream"));

        var outcome = await FakeClock.RunAsync(time, () => channel.SendAsync(new Message("x"), new Recipient(To), TestContext.Current.CancellationToken));

        Assert.Equal(DeliveryStatus.Accepted, outcome.Status);
        Assert.Equal(2, outcome.Attempts);
    }

    [Fact]
    public async Task A_failed_quit_after_the_send_keeps_the_success()
    {
        var (channel, server) = Create();
        server.DisconnectFailure = new IOException("reset after QUIT");

        var outcome = await channel.SendAsync(new Message("x"), new Recipient(To), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Accepted, outcome.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not an address")]
    [InlineData("@example.org")]
    public void Invalid_addresses_are_rejected_in_prepare(string address)
    {
        var (channel, _) = Create();

        Assert.Contains(channel.Prepare(new Message("x"), new Recipient(address)), i => i.Code == "invalid-address");
    }

    [Fact]
    public void Constructor_rejects_bad_options_without_echoing_them()
    {
        var noHost = Options();
        noHost.Host = " ";
        var badFrom = Options();
        badFrom.From = "not-an-address-TEST";
        var badPort = Options();
        badPort.Port = 70000;

        Assert.Throws<ArgumentException>(() => new EmailChannel("mail", noHost));
        Assert.DoesNotContain("TEST", Assert.Throws<ArgumentException>(() => new EmailChannel("mail", badFrom)).Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new EmailChannel("mail", badPort));
    }

    [Fact]
    public void Options_bind_from_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["mail:Host"] = "localhost",
            ["mail:Port"] = "1025",
            ["mail:Username"] = "alerts@example.org",
            ["mail:Password"] = Password,
            ["mail:From"] = "alerts@example.org",
        }).Build();
        var services = new ServiceCollection();
        services.AddHulaki().AddEmail("mail", configuration.GetSection("mail"));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();
        var options = provider.GetRequiredService<IOptionsMonitor<EmailChannelOptions>>().Get("mail");

        Assert.Equal("localhost", options.Host);
        Assert.Equal(1025, options.Port);
        Assert.Equal(Password, options.Password);
        Assert.IsType<EmailChannel>(provider.GetRequiredKeyedService<IChannel>("mail"));
    }

    [Theory]
    [InlineData("", "alerts@example.org", "alerts@example.org", "Host is empty")]
    [InlineData("smtp.example.org", "not an address", "alerts@example.org", "From is not")]
    [InlineData("smtp.example.org", "alerts@example.org", "", "Password is set without a Username")]
    public void Validate_on_start_reports_the_problem_without_the_password(string host, string from, string user, string expected)
    {
        var services = new ServiceCollection();
        services.AddHulaki().AddEmail("mail", o =>
        {
            o.Host = host;
            o.From = from;
            o.Username = user;
            o.Password = Password;
        });
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, ex.Message, StringComparison.Ordinal);
    }
}

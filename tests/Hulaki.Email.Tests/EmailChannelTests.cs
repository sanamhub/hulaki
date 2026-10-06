using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FreeTierMail;
using FreeTierMail.Testing;
using Hulaki.Channels;
using Hulaki.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Hulaki.Email.Tests;

/// <summary>
/// The channel over a <see cref="FreeTierMailer"/> with fake providers. SMTP itself is tested in
/// FreeTierMail.Smtp. Addresses use the example.org domain reserved by RFC 2606.
/// </summary>
public sealed class EmailChannelTests
{
    internal const string To = "reader@example.org";
    private const string Password = "TEST-smtp-password_0000";

    private static EmailChannelOptions Options(FakeTimeProvider? time = null) => new()
    {
        UseFreeTierMail = true,
        From = "Route alerts <alerts@example.org>",
        TimeProvider = time ?? new FakeTimeProvider(),
    };

    private static (EmailChannel Channel, FakeEmailProvider Provider) Create(EmailChannelOptions? options = null, FakeEmailProvider? provider = null)
    {
        provider ??= new FakeEmailProvider("resend");
        return (new EmailChannel("mail", new FreeTierMailer([provider]), options ?? Options()), provider);
    }

    [Fact]
    public async Task Sends_through_the_mailer_and_returns_the_providers_message_id()
    {
        var (channel, provider) = Create();

        var outcome = await channel.SendAsync(new Message("Rain warning"), new Recipient(To), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Accepted, outcome.Status);
        Assert.Equal("resend-1", outcome.PlatformMessageId);
        var sent = Assert.Single(provider.Sent);
        Assert.Equal("alerts@example.org", sent.From.Address);
        Assert.Equal("Route alerts", sent.From.DisplayName);
        Assert.Equal(To, Assert.Single(sent.To).Address);
    }

    [Fact]
    public void The_message_has_a_subject_both_parts_and_critical_priority()
    {
        var (channel, _) = Create();
        var message = new Message("**Red** <b>alert</b> & [DHM](https://dhm.gov.np/?a=1&b=2)\nsecond line")
        {
            Format = TextFormat.Markup,
            Title = "Route alert: Pokhara to Beni",
            Priority = MessagePriority.Urgent,
            Link = new Uri("https://example.org/watch/42"),
        };

        var sent = channel.Build(message, new Recipient(To));

        Assert.Equal("Route alert: Pokhara to Beni", sent.Subject);
        Assert.Equal(EmailPriority.Critical, sent.Priority);
        Assert.Equal("Red <b>alert</b> & DHM (https://dhm.gov.np/?a=1&b=2)\nsecond line\n\nhttps://example.org/watch/42", sent.TextBody);
        Assert.Contains(
            "<p><strong>Red</strong> &lt;b&gt;alert&lt;/b&gt; &amp; <a href=\"https://dhm.gov.np/?a=1&amp;b=2\">DHM</a><br>second line</p><p><a href=\"https://example.org/watch/42\">https://example.org/watch/42</a></p>",
            sent.HtmlBody!,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MessagePriority.Low, EmailPriority.Normal)]
    [InlineData(MessagePriority.Normal, EmailPriority.Normal)]
    [InlineData(MessagePriority.High, EmailPriority.Critical)]
    [InlineData(MessagePriority.Urgent, EmailPriority.Critical)]
    public void High_and_urgent_are_critical_mail(MessagePriority priority, EmailPriority expected)
    {
        var (channel, _) = Create();

        Assert.Equal(expected, channel.Build(new Message("x") { Priority = priority }, new Recipient(To)).Priority);
    }

    [Fact]
    public void Without_a_title_the_subject_is_the_first_line_cut_to_78()
    {
        var (channel, _) = Create();

        var shortOne = channel.Build(new Message("  Road closed at Beni\nDetails follow"), new Recipient(To));
        var longOne = channel.Build(new Message(new string('x', 100)), new Recipient(To));

        Assert.Equal("Road closed at Beni", shortOne.Subject);
        Assert.Equal(78, longOne.Subject.Length);
        Assert.EndsWith("…", longOne.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public void Plain_text_is_html_encoded_in_the_html_part() =>
        Assert.Equal("<!DOCTYPE html><html><body><p>a &lt;script&gt; **b**</p></body></html>", EmailChannel.HtmlBody(new Message("a <script> **b**")));

    public static TheoryData<ProviderOutcome[], HulakiErrorCode, RetryDisposition> Failures() => new()
    {
        { [ProviderOutcome.RecipientRejected], HulakiErrorCode.RecipientNotFound, RetryDisposition.Never },
        { [ProviderOutcome.Unavailable, ProviderOutcome.RecipientRejected], HulakiErrorCode.RecipientNotFound, RetryDisposition.Never },
        { [ProviderOutcome.ProviderFault, ProviderOutcome.ProviderFault], HulakiErrorCode.InvalidConfiguration, RetryDisposition.Never },
        { [ProviderOutcome.Throttled, ProviderOutcome.QuotaExhausted], HulakiErrorCode.RateLimited, RetryDisposition.AfterDelay },
        { [ProviderOutcome.ProviderFault, ProviderOutcome.QuotaExhausted], HulakiErrorCode.RateLimited, RetryDisposition.AfterDelay },
        { [], HulakiErrorCode.RateLimited, RetryDisposition.AfterDelay },
        { [ProviderOutcome.Unavailable], HulakiErrorCode.UpstreamFailure, RetryDisposition.AfterDelay },
        { [ProviderOutcome.Throttled, ProviderOutcome.Unavailable], HulakiErrorCode.UpstreamFailure, RetryDisposition.AfterDelay },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void A_failed_send_maps_by_what_every_provider_said(ProviderOutcome[] outcomes, HulakiErrorCode code, RetryDisposition retry)
    {
        var attempts = outcomes.Select((o, i) => new SendAttempt("p" + i, o, TimeSpan.Zero, "reader@example.org said no")).ToArray();

        var outcome = EmailChannel.Map(new FreeTierMail.SendResult(FreeTierMail.SendStatus.Failed, attempts));

        Assert.Equal(DeliveryStatus.Failed, outcome.Status);
        Assert.Equal(code, outcome.Error!.Code);
        Assert.Equal(retry, outcome.Error.Retry);
        Assert.DoesNotContain("reader@example.org", outcome.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_suppressed_address_is_blocked_and_never_retried()
    {
        var outcome = EmailChannel.Map(new FreeTierMail.SendResult(FreeTierMail.SendStatus.Failed, []) { Suppressed = true });

        Assert.Equal(HulakiErrorCode.RecipientBlocked, outcome.Error!.Code);
        Assert.Equal(RetryDisposition.Never, outcome.Error.Retry);
    }

    [Fact]
    public async Task An_unknown_answer_is_unknown_and_not_resent()
    {
        var (channel, provider) = Create(provider: new FakeEmailProvider("resend").Then(ProviderOutcome.Unknown));

        var outcome = await channel.SendAsync(new Message("x"), new Recipient(To), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Unknown, outcome.Status);
        Assert.Equal(HulakiErrorCode.AmbiguousOutcome, outcome.Error!.Code);
        Assert.Single(provider.Sent);
    }

    [Fact]
    public async Task An_outage_is_retried_by_the_channel()
    {
        var time = new FakeTimeProvider();
        var (channel, provider) = Create(Options(time), new FakeEmailProvider("resend").Then(ProviderOutcome.Unavailable));

        var outcome = await FakeClock.RunAsync(time, () => channel.SendAsync(new Message("x"), new Recipient(To), TestContext.Current.CancellationToken));

        Assert.Equal(DeliveryStatus.Accepted, outcome.Status);
        Assert.Equal(2, outcome.Attempts);
        Assert.Equal(2, provider.Sent.Count);
    }

    [Fact]
    public async Task The_mailer_fails_over_before_the_channel_sees_a_failure()
    {
        var first = new FakeEmailProvider("brevo").Then(ProviderOutcome.QuotaExhausted);
        var second = new FakeEmailProvider("resend");
        var channel = new EmailChannel("mail", new FreeTierMailer([first, second], new FreeTierMailerOptions { Strategy = RoutingStrategy.Ordered }), Options());

        var outcome = await channel.SendAsync(new Message("x"), new Recipient(To), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Accepted, outcome.Status);
        Assert.Equal(1, outcome.Attempts);
        Assert.Single(second.Sent);
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
    public void Smtp_mode_builds_one_smtp_provider_named_after_the_channel()
    {
        using var channel = new EmailChannel("mail", new EmailChannelOptions { Host = "localhost", Port = 1025, From = "alerts@example.org" });

        Assert.Equal(["mail"], channel.Mailer.ProviderNames);
    }

    [Fact]
    public void Constructors_reject_bad_options_without_echoing_them()
    {
        var badFrom = new EmailChannelOptions { Host = "smtp.example.org", Username = "u", Password = Password, From = "not-an-address-TEST" };

        Assert.Throws<ArgumentException>(() => new EmailChannel("mail", new EmailChannelOptions { Host = " ", From = "alerts@example.org" }));
        Assert.Throws<ArgumentException>(() => new EmailChannel("mail", new EmailChannelOptions { Host = "smtp.example.org", Port = 70000, Username = "u", Password = Password, From = "alerts@example.org" }));
        Assert.Throws<ArgumentException>(() => new EmailChannel("mail", new EmailChannelOptions { UseFreeTierMail = true, Host = "localhost", From = "alerts@example.org" }));
        var error = Assert.Throws<ArgumentException>(() => new EmailChannel("mail", badFrom));
        Assert.DoesNotContain("TEST", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, error.Message, StringComparison.Ordinal);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Smtp_options_bind_from_configuration()
    {
        var configuration = Configuration(new()
        {
            ["mail:Host"] = "localhost",
            ["mail:Port"] = "1025",
            ["mail:From"] = "alerts@example.org",
        });
        var services = new ServiceCollection();
        services.AddHulaki().AddEmail("mail", configuration.GetSection("mail"));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();
        var options = provider.GetRequiredService<IOptionsMonitor<EmailChannelOptions>>().Get("mail");

        Assert.False(options.UseFreeTierMail);
        Assert.Equal(1025, options.Port);
        Assert.Equal(["mail"], Assert.IsType<EmailChannel>(provider.GetRequiredKeyedService<IChannel>("mail")).Mailer.ProviderNames);
    }

    [Fact]
    public void Use_free_tier_mail_sends_through_the_registered_mailer()
    {
        var configuration = Configuration(new()
        {
            ["FreeTierMail:Providers:resend:ApiKey"] = "test-key-0000000000000000",
            ["Hulaki:Channels:mail:UseFreeTierMail"] = "true",
            ["Hulaki:Channels:mail:From"] = "alerts@example.org",
        });
        var services = new ServiceCollection();
        services.AddFreeTierMail(configuration.GetSection("FreeTierMail")).AddResend();
        services.AddHulaki().AddEmail("mail", configuration.GetSection("Hulaki:Channels:mail"));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();
        var channel = Assert.IsType<EmailChannel>(provider.GetRequiredKeyedService<IChannel>("mail"));

        Assert.Same(provider.GetRequiredService<FreeTierMailer>(), channel.Mailer);
    }

    [Fact]
    public void Use_free_tier_mail_without_a_mailer_names_the_missing_call()
    {
        var services = new ServiceCollection();
        services.AddHulaki().AddEmail("mail", o =>
        {
            o.UseFreeTierMail = true;
            o.From = "alerts@example.org";
        });
        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredKeyedService<IChannel>("mail"));

        Assert.Contains("AddFreeTierMail()", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_channel_switched_off_in_configuration_is_not_registered()
    {
        var configuration = Configuration(new() { ["mail:Enabled"] = "false", ["mail:Host"] = "smtp.example.org" });
        var services = new ServiceCollection();
        services.AddHulaki().AddEmail("mail", configuration.GetSection("mail"));
        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetKeyedService<IChannel>("mail"));
        Assert.Null(provider.GetService<IStartupValidator>()); // no options were added to check
    }

    [Theory]
    [InlineData(false, "", "alerts@example.org", "", "Host is empty")]
    [InlineData(false, "smtp.example.org", "not an address", "u", "From is not")]
    [InlineData(false, "smtp.example.org", "alerts@example.org", "", "Password is set without a Username")]
    [InlineData(true, "smtp.example.org", "alerts@example.org", "", "UseFreeTierMail is true")]
    public void Validate_on_start_reports_the_problem_without_the_password(bool useFreeTierMail, string host, string from, string user, string expected)
    {
        var services = new ServiceCollection();
        services.AddHulaki().AddEmail("mail", o =>
        {
            o.UseFreeTierMail = useFreeTierMail;
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

    [Fact]
    public void A_remote_smtp_server_needs_a_login()
    {
        var services = new ServiceCollection();
        services.AddHulaki().AddEmail("mail", o =>
        {
            o.Host = "smtp.example.org";
            o.From = "alerts@example.org";
        });
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("only a loopback host may skip the login", ex.Message, StringComparison.Ordinal);
    }
}

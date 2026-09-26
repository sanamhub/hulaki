using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Hulaki.Testing;
using Hulaki.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Hulaki.Slack.Tests;

/// <summary>
/// Answers follow https://api.slack.com/messaging/webhooks (handling errors): 200 "ok", or a status
/// with a bare error code as the body. Webhook values are synthetic.
/// </summary>
public sealed class SlackChannelTests
{
    internal const string Webhook = "https://hooks.slack.com/services/T00000000/B00000000/TESTsecret000000000000000";

    private static (SlackChannel Channel, ScriptedHttpHandler Stub) Create(FakeTimeProvider? time = null)
    {
        var stub = new ScriptedHttpHandler();
        var channel = new SlackChannel("slack", new HttpClient(stub), new SlackChannelOptions
        {
            WebhookUrl = new Uri(Webhook),
            TimeProvider = time ?? new FakeTimeProvider(),
            DisableRateLimiting = true,
        });
        return (channel, stub);
    }

    [Fact]
    public async Task Posts_text_to_the_webhook_and_ok_is_delivered()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.OK, "ok", "text/html");

        var outcome = await channel.SendAsync(new Message("Rain warning"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Delivered, outcome.Status);
        var request = Assert.Single(stub.Requests);
        Assert.Equal(Webhook, request.Request.RequestUri!.AbsoluteUri);
        Assert.Equal("Rain warning", JsonDocument.Parse(request.Body).RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public void Markup_renders_as_mrkdwn_with_control_characters_escaped()
    {
        var message = new Message("**Red** in _Myagdi_ & <Mustang> `a<b` [DHM & co](https://dhm.gov.np/a|b)")
        {
            Format = TextFormat.Markup,
            Title = "Route <alert>",
            Link = new Uri("https://example.org/watch/42"),
        };

        Assert.Equal(
            "*Route &lt;alert&gt;*\n*Red* in _Myagdi_ &amp; &lt;Mustang&gt; `a&lt;b` <https://dhm.gov.np/a%7Cb|DHM &amp; co>\n<https://example.org/watch/42>",
            SlackChannel.Render(message));
    }

    [Fact]
    public void Plain_text_escapes_only_the_three_control_characters() =>
        Assert.Equal("a &amp; b &lt;!channel&gt; *c*", SlackChannel.Render(new Message("a & b <!channel> *c*")));

    public static TheoryData<HttpStatusCode, string, HulakiErrorCode> DocumentedErrors() => new()
    {
        { HttpStatusCode.BadRequest, "invalid_payload", HulakiErrorCode.InvalidInput },
        { HttpStatusCode.BadRequest, "no_text", HulakiErrorCode.InvalidInput },
        { HttpStatusCode.NotFound, "channel_not_found", HulakiErrorCode.RecipientNotFound },
        { HttpStatusCode.Gone, "channel_is_archived", HulakiErrorCode.RecipientNotFound },
        { HttpStatusCode.NotFound, "no_service", HulakiErrorCode.InvalidConfiguration },
        { HttpStatusCode.Forbidden, "invalid_token", HulakiErrorCode.InvalidConfiguration },
        { HttpStatusCode.Forbidden, "action_prohibited", HulakiErrorCode.PermissionDenied },
        { HttpStatusCode.BadRequest, "posting_to_general_channel_denied", HulakiErrorCode.PermissionDenied },
    };

    [Theory]
    [MemberData(nameof(DocumentedErrors))]
    public async Task Documented_errors_map_to_codes(HttpStatusCode status, string body, HulakiErrorCode code)
    {
        var (channel, stub) = Create();
        stub.Respond(status, body, "text/html");

        var outcome = await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Failed, outcome.Status);
        Assert.Equal(code, outcome.Error!.Code);
        Assert.Equal(RetryDisposition.Never, outcome.Error.Retry);
        Assert.Equal(body, outcome.Error.PlatformCode);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task A_429_waits_for_retry_after()
    {
        var time = new FakeTimeProvider();
        var (channel, stub) = Create(time);
        stub.Respond(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("rate_limited") };
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
                return response;
            })
            .Respond(HttpStatusCode.OK, "ok", "text/html");
        var started = time.GetUtcNow();

        var outcome = await FakeClock.RunAsync(time, () => channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken), TimeSpan.FromSeconds(1));

        Assert.True(outcome.Succeeded);
        Assert.True(time.GetUtcNow() - started >= TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Html_error_page_from_a_proxy_is_unknown_on_502_with_no_platform_code()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.BadGateway, "<html>bad gateway</html>", "text/html");

        var outcome = await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Unknown, outcome.Status);
        Assert.Null(outcome.Error!.PlatformCode);
    }

    [Fact]
    public async Task A_500_rollup_error_is_unknown()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.InternalServerError, "rollup_error", "text/html");

        var outcome = await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Unknown, outcome.Status);
        Assert.Equal("rollup_error", outcome.Error!.PlatformCode);
    }

    [Fact]
    public async Task An_empty_200_is_not_a_delivery()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.OK, string.Empty, "text/html");

        var outcome = await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.False(outcome.Succeeded);
    }

    [Fact]
    public void A_recipient_other_than_self_is_rejected_in_prepare()
    {
        var (channel, _) = Create();

        Assert.Contains(channel.Prepare(new Message("x"), new Recipient("#general")), i => i.Code == "recipient-must-be-self");
    }

    [Theory]
    [InlineData("http://hooks.slack.com/services/T0/B0/x")]
    [InlineData("https://hooks.slack.com.example.org/services/T0/B0/x")]
    [InlineData("https://hooks.slack.com/workflows/T0/x")]
    [InlineData("https://example.org/services/T0/B0/x")]
    public void Constructor_rejects_urls_that_are_not_slack_webhooks(string address)
    {
        var ex = Assert.Throws<ArgumentException>(() => new SlackChannel("slack", new HttpClient(), new SlackChannelOptions { WebhookUrl = new Uri(address) }));

        Assert.DoesNotContain("T0/B0", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_bind_from_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["slack:WebhookUrl"] = Webhook }).Build();
        var services = new ServiceCollection();
        services.AddHulaki().AddSlack("slack", configuration.GetSection("slack"));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();

        Assert.Equal(new Uri(Webhook), provider.GetRequiredService<IOptionsMonitor<SlackChannelOptions>>().Get("slack").WebhookUrl);
        Assert.IsType<SlackChannel>(provider.GetRequiredKeyedService<IChannel>("slack"));
    }

    [Fact]
    public void Validate_on_start_fails_on_a_foreign_url_without_echoing_it()
    {
        var services = new ServiceCollection();
        services.AddHulaki().AddSlack("slack", o => o.WebhookUrl = new Uri("https://example.org/services/TESTsecret"));
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("hooks.slack.com", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("TESTsecret", ex.Message, StringComparison.Ordinal);
    }
}

public sealed class SlackContractTests : ChannelContractTests<SlackChannel>
{
    protected override Recipient ValidRecipient => Recipient.Self;

    protected override SlackChannel Create(HttpMessageHandler handler, TimeProvider time) =>
        new("slack", new HttpClient(handler), new SlackChannelOptions { WebhookUrl = new Uri(SlackChannelTests.Webhook), TimeProvider = time });

    protected override HttpResponseMessage Success() => new(HttpStatusCode.OK) { Content = new StringContent("ok") };
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Hulaki.Testing;
using Hulaki.Tests;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Hulaki.Discord.Tests;

/// <summary>
/// Response bodies follow https://discord.com/developers/docs/resources/webhook#execute-webhook,
/// https://discord.com/developers/docs/topics/rate-limits and
/// https://discord.com/developers/docs/topics/opcodes-and-status-codes#json. Values are synthetic.
/// </summary>
public sealed class DiscordChannelTests
{
    internal const string Webhook = "https://discord.com/api/webhooks/123456789012345678/TEST-webhook_token_0000000000";

    private static (DiscordChannel Channel, ScriptedHttpHandler Stub) Create(FakeTimeProvider? time = null, string webhook = Webhook)
    {
        var stub = new ScriptedHttpHandler();
        var channel = new DiscordChannel(
            "ops",
            new HttpClient(stub),
            new DiscordChannelOptions { WebhookUrl = new Uri(webhook), TimeProvider = time ?? new FakeTimeProvider(), DisableRateLimiting = true });
        return (channel, stub);
    }

    private static JsonElement Body(RecordedRequest request) => JsonDocument.Parse(request.Body).RootElement;

    [Fact]
    public async Task Posts_to_the_webhook_with_wait_and_returns_the_message_id()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.OK, """{"id":"1100000000000000001","type":0,"content":"x","channel_id":"1000000000000000001"}""");

        var outcome = await channel.SendAsync(new Message("Rain warning"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Delivered, outcome.Status);
        Assert.Equal("1100000000000000001", outcome.PlatformMessageId);
        var request = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Post, request.Request.Method);
        Assert.Equal(Webhook + "?wait=true", request.Request.RequestUri!.AbsoluteUri);
        Assert.Equal("Rain warning", Body(request).GetProperty("content").GetString());
    }

    [Fact]
    public async Task Mentions_are_switched_off_so_everyone_never_pings()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.OK, """{"id":"1"}""");

        await channel.SendAsync(new Message("@everyone the road is closed"), Recipient.Self, TestContext.Current.CancellationToken);

        var mentions = Body(Assert.Single(stub.Requests)).GetProperty("allowed_mentions");
        Assert.Equal(JsonValueKind.Array, mentions.GetProperty("parse").ValueKind);
        Assert.Equal(0, mentions.GetProperty("parse").GetArrayLength());
    }

    [Fact]
    public async Task An_existing_query_is_kept()
    {
        var (channel, stub) = Create(webhook: Webhook + "?thread_id=42");
        stub.Respond(HttpStatusCode.OK, """{"id":"1"}""");

        await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(Webhook + "?thread_id=42&wait=true", Assert.Single(stub.Requests).Request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public void Markup_renders_as_discord_markdown_with_text_escaped()
    {
        var message = new Message(@"**Red** alert, _be careful_: 5\*3 ~ | > # \[x\] <@1> `a_b` [DHM](https://dhm.gov.np/a(b)")
        {
            Format = TextFormat.Markup,
            Title = "Route *alert*",
            Link = new Uri("https://example.org/watch/42"),
        };

        var rendered = DiscordChannel.Render(message);

        Assert.Equal(
            "**Route \\*alert\\***\n**Red** alert, *be careful*: 5\\*3 \\~ \\| \\> \\# \\[x\\] \\<@1\\> `a_b` [DHM](https://dhm.gov.np/a%28b)\nhttps://example.org/watch/42",
            rendered);
    }

    [Fact]
    public void Plain_text_is_escaped_so_it_shows_as_written()
    {
        Assert.Equal("a\\_b \\*c\\* \\\\ \\`d\\`", DiscordChannel.Render(new Message(@"a_b *c* \ `d`")));
    }

    [Fact]
    public async Task No_content_is_delivered_without_an_id()
    {
        var (channel, stub) = Create();
        stub.Respond(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var outcome = await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Delivered, outcome.Status);
        Assert.Null(outcome.PlatformMessageId);
    }

    [Fact]
    public async Task Empty_200_is_unknown_not_delivered()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.OK, string.Empty);

        var outcome = await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Unknown, outcome.Status);
        Assert.Equal(1, outcome.Attempts);
    }

    public static TheoryData<HttpStatusCode, string, HulakiErrorCode, string?> DocumentedErrors() => new()
    {
        { HttpStatusCode.NotFound, """{"message":"Unknown Webhook","code":10015}""", HulakiErrorCode.InvalidConfiguration, "10015" },
        { HttpStatusCode.Unauthorized, """{"message":"Invalid Webhook Token","code":50027}""", HulakiErrorCode.InvalidConfiguration, "50027" },
        { HttpStatusCode.BadRequest, """{"code":50035,"errors":{"content":{"_errors":[{"code":"BASE_TYPE_MAX_LENGTH","message":"Must be 2000 or fewer in length."}]}},"message":"Invalid Form Body"}""", HulakiErrorCode.InvalidInput, "50035" },
        { HttpStatusCode.Forbidden, """{"message":"Missing Permissions","code":50013}""", HulakiErrorCode.PermissionDenied, "50013" },
        { HttpStatusCode.RequestEntityTooLarge, """{"message":"Request entity too large","code":40005}""", HulakiErrorCode.InvalidInput, "40005" },
    };

    [Theory]
    [MemberData(nameof(DocumentedErrors))]
    public async Task Documented_errors_map_to_codes_and_never_retry(HttpStatusCode status, string body, HulakiErrorCode code, string? platformCode)
    {
        var (channel, stub) = Create();
        stub.Respond(status, body);

        var outcome = await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Failed, outcome.Status);
        Assert.Equal(code, outcome.Error!.Code);
        Assert.Equal(RetryDisposition.Never, outcome.Error.Retry);
        Assert.Equal(platformCode, outcome.Error.PlatformCode);
        Assert.Equal((int)status, outcome.Error.HttpStatus);
        Assert.DoesNotContain("Unknown Webhook", outcome.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task Retry_after_in_seconds_as_a_float_is_honoured()
    {
        var time = new FakeTimeProvider();
        var (channel, stub) = Create(time);
        stub.Respond(HttpStatusCode.TooManyRequests, """{"message":"You are being rate limited.","retry_after":2.5,"global":false}""")
            .Respond(HttpStatusCode.OK, """{"id":"2"}""");
        var started = time.GetUtcNow();

        var outcome = await FakeClock.RunAsync(time, () => channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken));

        Assert.True(outcome.Succeeded);
        Assert.Equal(2, outcome.Attempts);
        Assert.True(time.GetUtcNow() - started >= TimeSpan.FromSeconds(2.5));
    }

    [Fact]
    public async Task A_rate_limit_past_the_retry_cap_returns_rate_limited_with_the_wait()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.TooManyRequests, """{"message":"You are being rate limited.","retry_after":600.25,"global":true}""");

        var outcome = await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(HulakiErrorCode.RateLimited, outcome.Error!.Code);
        Assert.Equal(TimeSpan.FromSeconds(600.25), outcome.Error.RetryAfter);
    }

    [Fact]
    public async Task Html_error_page_from_a_proxy_is_unknown_on_502()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.BadGateway, "<html>bad gateway</html>", "text/html");

        var outcome = await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Unknown, outcome.Status);
        Assert.Equal(HulakiErrorCode.UpstreamFailure, outcome.Error!.Code);
    }

    [Fact]
    public void A_recipient_other_than_self_is_rejected_in_prepare()
    {
        var (channel, _) = Create();

        var issues = channel.Prepare(new Message("x"), new Recipient("1000000000000000001"));

        Assert.Contains(issues, i => i.Code == "recipient-must-be-self" && i.IsError);
    }

    [Fact]
    public void Text_over_2000_after_escaping_is_rejected()
    {
        var (channel, _) = Create();

        Assert.Empty(channel.Prepare(new Message(new string('a', 2000)), Recipient.Self));
        Assert.Contains(channel.Prepare(new Message(new string('_', 1001)), Recipient.Self), i => i.Code == "text-too-long");
    }

    public static TheoryData<string> BadWebhooks() =>
    [
        "http://discord.com/api/webhooks/1/abc",
        "https://example.org/api/webhooks/1/abc",
        "https://discord.com.example.org/api/webhooks/1/abc",
        "https://discord.com/api/channels/1/messages",
        "https://discord.com:8443/api/webhooks/1/abc",
    ];

    [Theory]
    [MemberData(nameof(BadWebhooks))]
    public void Constructor_rejects_urls_that_are_not_discord_webhooks_without_echoing_them(string address)
    {
        var ex = Assert.Throws<ArgumentException>(() => new DiscordChannel("ops", new HttpClient(), new DiscordChannelOptions { WebhookUrl = new Uri(address) }));

        Assert.DoesNotContain("abc", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://discordapp.com/api/webhooks/123/TEST_token")]
    [InlineData("https://discord.com/api/v10/webhooks/123/TEST_token")]
    public void Constructor_accepts_the_legacy_host_and_versioned_paths(string address)
    {
        using var channel = new DiscordChannel("ops", new HttpClient(), new DiscordChannelOptions { WebhookUrl = new Uri(address) });

        Assert.Equal("discord", channel.Capabilities.Platform);
    }

    [Fact]
    public void Constructor_rejects_a_missing_webhook_url()
    {
        Assert.Throws<ArgumentException>(() => new DiscordChannel("ops", new HttpClient(), new DiscordChannelOptions()));
    }

    [Fact]
    public async Task Nothing_captured_contains_the_webhook_token_or_the_text()
    {
        using var telemetry = new TelemetryCapture();
        var logs = new CapturingLoggerFactory();
        using var stub = new ScriptedHttpHandler();
        stub.Respond(HttpStatusCode.NotFound, """{"message":"Unknown Webhook canary 9d2e","code":10015}""");
        var name = "discord-redaction-" + Guid.NewGuid().ToString("N");
        using var channel = new DiscordChannel(name, new HttpClient(stub), new DiscordChannelOptions
        {
            WebhookUrl = new Uri(Webhook),
            LoggerFactory = logs,
            DisableRateLimiting = true,
        });

        var outcome = await channel.SendAsync(new Message("canary 9d2e ridge road"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.NotEmpty(logs.Logs);
        IEnumerable<string> captured =
        [
            .. logs.Logs.SelectMany(l => l.AllText()),
            .. telemetry.Spans.Where(s => (s.GetTagItem("hulaki.channel") as string) == name).SelectMany(s => s.TagObjects.Select(t => $"{t.Key}={t.Value}")),
            outcome.ToString(),
            outcome.Error!.Message,
        ];
        foreach (var secret in new[] { "TEST-webhook_token", "123456789012345678", "canary 9d2e" })
        {
            Assert.DoesNotContain(captured, text => text.Contains(secret, StringComparison.OrdinalIgnoreCase));
        }
    }
}

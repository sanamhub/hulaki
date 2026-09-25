using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Hulaki.Telegram.Tests;

public sealed class TelegramChannelTests
{
    private const string Token = "123456:TEST-token_0000000000000000000";

    private static (TelegramChannel Channel, StubHandler Stub) Create(FakeTimeProvider? time = null)
    {
        var stub = new StubHandler();
        var channel = new TelegramChannel(
            "tg",
            new HttpClient(stub),
            new TelegramChannelOptions { BotToken = Token, TimeProvider = time ?? new FakeTimeProvider(), DisableRateLimiting = true });
        return (channel, stub);
    }

    [Fact]
    public async Task Sends_html_to_the_right_path_and_returns_the_message_id()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":77}}""");
        var message = new Message("Rain **Red** in <Myagdi> & Mustang") { Format = TextFormat.Markup, Title = "DHM", Priority = MessagePriority.Low };

        var outcome = await channel.SendAsync(message, new Recipient("-100123"), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Delivered, outcome.Status);
        Assert.Equal("77", outcome.PlatformMessageId);
        var (request, body) = Assert.Single(stub.Requests);
        Assert.Equal($"https://api.telegram.org/bot{Token}/sendMessage", request.RequestUri!.AbsoluteUri);
        Assert.Contains("\"text\":\"\\u003Cb\\u003EDHM\\u003C/b\\u003E\\nRain \\u003Cb\\u003ERed\\u003C/b\\u003E in \\u0026lt;Myagdi\\u0026gt; \\u0026amp; Mustang\"", body, StringComparison.Ordinal);
        Assert.Contains("\"disable_notification\":true", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Blocked_by_user_maps_to_recipient_blocked()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.Forbidden, """{"ok":false,"error_code":403,"description":"Forbidden: bot was blocked by the user"}""");

        var outcome = await channel.SendAsync(new Message("x"), new Recipient("1"), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Failed, outcome.Status);
        Assert.Equal(HulakiErrorCode.RecipientBlocked, outcome.Error!.Code);
        Assert.DoesNotContain("blocked by the user", outcome.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retry_after_in_the_body_is_honoured()
    {
        var time = new FakeTimeProvider();
        var (channel, stub) = Create(time);
        stub.Respond(HttpStatusCode.TooManyRequests, """{"ok":false,"error_code":429,"description":"Too Many Requests: retry after 3","parameters":{"retry_after":3}}""")
            .Respond(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":1}}""");
        var started = time.GetUtcNow();

        var outcome = await Time.RunAsync(time, () => channel.SendAsync(new Message("x"), new Recipient("1"), TestContext.Current.CancellationToken));

        Assert.True(outcome.Succeeded);
        Assert.True(time.GetUtcNow() - started >= TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Html_error_page_from_a_proxy_is_unknown_on_502()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.BadGateway, "<html>bad gateway</html>");

        var outcome = await channel.SendAsync(new Message("x"), new Recipient("1"), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Unknown, outcome.Status);
    }

    [Fact]
    public void Self_recipient_is_rejected_in_prepare()
    {
        var (channel, _) = Create();

        var issues = channel.Prepare(new Message("x"), Recipient.Self);

        Assert.Contains(issues, i => i.Code == "recipient-required");
    }

    [Fact]
    public async Task Cannot_parse_entities_maps_to_invalid_input()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.BadRequest, """{"ok":false,"error_code":400,"description":"Bad Request: can't parse entities: Unsupported start tag \"x\" at byte offset 0"}""");

        var outcome = await channel.SendAsync(new Message("x"), new Recipient("1"), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Failed, outcome.Status);
        Assert.Equal(HulakiErrorCode.InvalidInput, outcome.Error!.Code);
        Assert.Equal(RetryDisposition.Never, outcome.Error.Retry);
        Assert.Equal("400", outcome.Error.PlatformCode);
        Assert.DoesNotContain("parse entities", outcome.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Chat_not_found_maps_to_recipient_not_found()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.BadRequest, """{"ok":false,"error_code":400,"description":"Bad Request: chat not found"}""");

        var outcome = await channel.SendAsync(new Message("x"), new Recipient("1"), TestContext.Current.CancellationToken);

        Assert.Equal(HulakiErrorCode.RecipientNotFound, outcome.Error!.Code);
    }

    [Fact]
    public async Task Unauthorized_maps_to_invalid_configuration()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.Unauthorized, """{"ok":false,"error_code":401,"description":"Unauthorized"}""");

        var outcome = await channel.SendAsync(new Message("x"), new Recipient("1"), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Failed, outcome.Status);
        Assert.Equal(HulakiErrorCode.InvalidConfiguration, outcome.Error!.Code);
        Assert.Equal(401, outcome.Error.HttpStatus);
    }

    [Fact]
    public async Task Ok_false_in_a_200_is_a_failure_not_a_delivery()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.OK, """{"ok":false,"error_code":400,"description":"Bad Request: message text is empty"}""");

        var outcome = await channel.SendAsync(new Message("x"), new Recipient("1"), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Failed, outcome.Status);
        Assert.Equal("400", outcome.Error!.PlatformCode);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task Empty_200_is_a_failure_not_a_delivery()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.OK, "");

        var outcome = await channel.SendAsync(new Message("x"), new Recipient("1"), TestContext.Current.CancellationToken);

        Assert.False(outcome.Succeeded);
    }

    [Fact]
    public async Task Two_sends_to_the_same_chat_are_at_least_a_second_apart()
    {
        var time = new FakeTimeProvider();
        var (channel, stub) = CreateLimited(time);
        stub.Respond(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":1}}""")
            .Respond(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":2}}""");
        var ct = TestContext.Current.CancellationToken;

        var outcomes = await Time.RunAsync(time, () => Task.WhenAll(
            channel.SendAsync(new Message("one"), new Recipient("-100123"), ct),
            channel.SendAsync(new Message("two"), new Recipient("-100123"), ct)));

        Assert.All(outcomes, o => Assert.True(o.Succeeded));
        var times = stub.RequestTimes;
        Assert.Equal(2, times.Count);
        Assert.True((times[1] - times[0]).Duration() >= TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Sends_to_different_chats_are_not_delayed()
    {
        var time = new FakeTimeProvider();
        var (channel, stub) = CreateLimited(time);
        stub.Respond(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":1}}""")
            .Respond(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":2}}""");
        var ct = TestContext.Current.CancellationToken;

        // The fake clock never moves, so a per-chat wait would never end and the real timeout fires.
        var outcomes = await Task.WhenAll(
            channel.SendAsync(new Message("one"), new Recipient("-100123"), ct),
            channel.SendAsync(new Message("two"), new Recipient("-100456"), ct)).WaitAsync(TimeSpan.FromSeconds(10), ct);

        Assert.All(outcomes, o => Assert.True(o.Succeeded));
    }

    [Fact]
    public void Constructor_rejects_an_empty_token()
    {
        Assert.Throws<ArgumentException>(() => new TelegramChannel("tg", new HttpClient(new StubHandler()), new TelegramChannelOptions()));
    }

    private static (TelegramChannel Channel, StubHandler Stub) CreateLimited(FakeTimeProvider time)
    {
        var stub = new StubHandler(time);
        var channel = new TelegramChannel("tg", new HttpClient(stub), new TelegramChannelOptions { BotToken = Token, TimeProvider = time });
        return (channel, stub);
    }
}

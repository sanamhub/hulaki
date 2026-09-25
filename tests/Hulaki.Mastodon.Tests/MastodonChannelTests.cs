using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Hulaki.Idempotency;
using Hulaki.Testing;
using Hulaki.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Hulaki.Mastodon.Tests;

/// <summary>
/// Shapes follow docs.joinmastodon.org: methods/statuses (create, Idempotency-Key), entities/Status,
/// entities/Instance (v2) and api/rate-limits. Tokens and ids are synthetic.
/// </summary>
public sealed class MastodonChannelTests
{
    internal const string Token = "TEST-mastodon-token_000000000000000000000";
    internal const string Posted = """{"id":"113000000000000001","created_at":"2026-09-25T10:00:00.000Z","visibility":"unlisted","uri":"https://social.example.org/users/alerts/statuses/113000000000000001","url":"https://social.example.org/@alerts/113000000000000001","content":"<p>x</p>"}""";
    private const string Instance5000 = """{"domain":"social.example.org","configuration":{"statuses":{"max_characters":5000,"max_media_attachments":4,"characters_reserved_per_url":23}}}""";
    private const string Instance300 = """{"domain":"social.example.org","configuration":{"statuses":{"max_characters":300,"characters_reserved_per_url":23}}}""";

    private static (MastodonChannel Channel, ScriptedHttpHandler Stub) Create(Action<MastodonChannelOptions>? configure = null, FakeTimeProvider? time = null)
    {
        var stub = new ScriptedHttpHandler();
        var options = new MastodonChannelOptions
        {
            InstanceUrl = new Uri("https://social.example.org/"),
            AccessToken = Token,
            TimeProvider = time ?? new FakeTimeProvider(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero)),
            DisableRateLimiting = true,
        };
        configure?.Invoke(options);
        return (new MastodonChannel("masto", new HttpClient(stub), options), stub);
    }

    [Fact]
    public async Task Reads_the_instance_limit_once_then_posts_with_the_key()
    {
        var (channel, stub) = Create(o =>
        {
            o.Visibility = "unlisted";
            o.Language = "ne";
        });
        stub.Respond(HttpStatusCode.OK, Instance5000).Respond(HttpStatusCode.OK, Posted).Respond(HttpStatusCode.OK, Posted);

        var outcome = await channel.SendAsync(new Message("Rain warning") { IdempotencyKey = "watch-42:rev-7", Title = "Route alert" }, Recipient.Self, TestContext.Current.CancellationToken);
        await channel.SendAsync(new Message("again"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Delivered, outcome.Status);
        Assert.Equal("113000000000000001", outcome.PlatformMessageId);
        Assert.Equal(new Uri("https://social.example.org/@alerts/113000000000000001"), outcome.Url);
        Assert.Equal("https://social.example.org/api/v2/instance", stub.Requests[0].Request.RequestUri!.AbsoluteUri);
        var post = stub.Requests[1];
        Assert.Equal("https://social.example.org/api/v1/statuses", post.Request.RequestUri!.AbsoluteUri);
        Assert.Equal(Token, post.Request.Headers.Authorization!.Parameter);
        Assert.Equal("watch-42:rev-7", post.Request.Headers.GetValues("Idempotency-Key").Single());
        var body = JsonDocument.Parse(post.Body).RootElement;
        Assert.Equal("Route alert\nRain warning", body.GetProperty("status").GetString());
        Assert.Equal("unlisted", body.GetProperty("visibility").GetString());
        Assert.Equal("ne", body.GetProperty("language").GetString());
        Assert.Equal(3, stub.Requests.Count);
    }

    [Fact]
    public void Without_a_key_the_fingerprint_is_the_idempotency_key()
    {
        var message = new Message("x") { Title = "t" };

        Assert.Equal(MessageFingerprint.Compute(message), MastodonChannel.IdempotencyKeyFor(message));
    }

    [Fact]
    public async Task A_5xx_is_retried_with_the_same_key_because_mastodon_deduplicates()
    {
        var time = new FakeTimeProvider();
        var (channel, stub) = Create(o => o.MaxCharacters = 500, time);
        stub.Respond(HttpStatusCode.BadGateway, "<html>bad gateway</html>", "text/html").Respond(HttpStatusCode.OK, Posted);

        var outcome = await FakeClock.RunAsync(time, () => channel.SendAsync(new Message("x") { IdempotencyKey = "k-1" }, Recipient.Self, TestContext.Current.CancellationToken));

        Assert.Equal(DeliveryStatus.Delivered, outcome.Status);
        Assert.Equal(2, outcome.Attempts);
        Assert.All(stub.Requests, r => Assert.Equal("k-1", r.Request.Headers.GetValues("Idempotency-Key").Single()));
    }

    [Fact]
    public async Task A_connection_lost_after_sending_is_retried_too()
    {
        var time = new FakeTimeProvider();
        var (channel, stub) = Create(o => o.MaxCharacters = 500, time);
        stub.Throw(new HttpRequestException(HttpRequestError.ResponseEnded, "ended")).Respond(HttpStatusCode.OK, Posted);

        var outcome = await FakeClock.RunAsync(time, () => channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken));

        Assert.Equal(DeliveryStatus.Delivered, outcome.Status);
        Assert.Equal(2, outcome.Attempts);
    }

    [Fact]
    public async Task A_lower_instance_limit_stops_the_post()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.OK, Instance300);

        var outcome = await channel.SendAsync(new Message(new string('a', 301)), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(HulakiErrorCode.InvalidInput, outcome.Error!.Code);
        Assert.Contains("300", outcome.Error.Message, StringComparison.Ordinal);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task An_instance_error_keeps_the_default_and_asks_again_next_time()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.InternalServerError, "oops").Respond(HttpStatusCode.OK, Posted)
            .Respond(HttpStatusCode.OK, Instance5000).Respond(HttpStatusCode.OK, Posted);

        await channel.SendAsync(new Message("one"), Recipient.Self, TestContext.Current.CancellationToken);
        await channel.SendAsync(new Message("two"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(2, stub.Requests.Count(r => r.Request.RequestUri!.AbsolutePath == "/api/v2/instance"));
    }

    [Fact]
    public void Urls_count_as_23_and_the_default_limit_is_500()
    {
        var (channel, _) = Create();
        var url = "https://example.org/" + new string('p', 200);

        Assert.Equal(23 + 1 + 3, MastodonTextCounter.Instance.Count(url + " abc"));
        Assert.Equal(1, MastodonTextCounter.Instance.Count("क्षि"));
        Assert.Empty(channel.Prepare(new Message(new string('a', 476) + " " + url), Recipient.Self));
        Assert.Contains(channel.Prepare(new Message(new string('a', 501)), Recipient.Self), i => i.Code == "text-too-long");
        Assert.Contains(channel.Prepare(new Message("x"), new Recipient("@someone")), i => i.Code == "recipient-must-be-self");
    }

    [Fact]
    public void Max_characters_raises_the_checked_limit()
    {
        var (channel, _) = Create(o => o.MaxCharacters = 5000);

        Assert.Equal(5000, channel.Capabilities.TextLimit.Max);
        Assert.Equal(500, MastodonChannel.Manifest.TextLimit.Max);
        Assert.Equal(Availability.Available, channel.Capabilities.Get(Capability.IdempotentSend));
        Assert.Equal(Availability.UnknownUntilRequest, channel.Capabilities.Get(Capability.Text));
        Assert.Empty(channel.Prepare(new Message(new string('a', 4000)), Recipient.Self));
    }

    public static TheoryData<HttpStatusCode, string, HulakiErrorCode, RetryDisposition> Errors() => new()
    {
        { HttpStatusCode.Unauthorized, """{"error":"The access token is invalid"}""", HulakiErrorCode.ReconnectRequired, RetryDisposition.AfterReconnect },
        { HttpStatusCode.Forbidden, """{"error":"This action is outside the authorized scopes"}""", HulakiErrorCode.PermissionDenied, RetryDisposition.Never },
        { HttpStatusCode.NotFound, """{"error":"Record not found"}""", HulakiErrorCode.InvalidConfiguration, RetryDisposition.Never },
        { HttpStatusCode.UnprocessableEntity, """{"error":"Validation failed: Text character limit of 500 exceeded"}""", HulakiErrorCode.InvalidInput, RetryDisposition.Never },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public async Task Documented_errors_map_to_codes_without_the_platform_text(HttpStatusCode status, string body, HulakiErrorCode code, RetryDisposition retry)
    {
        var (channel, stub) = Create(o => o.MaxCharacters = 500);
        stub.Respond(status, body);

        var outcome = await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Failed, outcome.Status);
        Assert.Equal(code, outcome.Error!.Code);
        Assert.Equal(retry, outcome.Error.Retry);
        Assert.DoesNotContain("Validation failed", outcome.Error.Message, StringComparison.Ordinal);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task A_429_reads_x_ratelimit_reset()
    {
        var (channel, stub) = Create(o => o.MaxCharacters = 500);
        stub.Respond(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("""{"error":"Too many requests"}""") };
            response.Headers.Add("X-RateLimit-Limit", "300");
            response.Headers.Add("X-RateLimit-Remaining", "0");
            response.Headers.Add("X-RateLimit-Reset", "2026-09-25T13:00:00.000Z");
            return response;
        });

        var outcome = await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(HulakiErrorCode.RateLimited, outcome.Error!.Code);
        Assert.Equal(TimeSpan.FromHours(3), outcome.Error.RetryAfter);
    }

    [Fact]
    public void Constructor_rejects_bad_options()
    {
        Assert.Throws<ArgumentException>(() => new MastodonChannel("m", new HttpClient(), new MastodonChannelOptions { AccessToken = Token }));
        Assert.Throws<ArgumentException>(() => new MastodonChannel("m", new HttpClient(), new MastodonChannelOptions { InstanceUrl = new Uri("https://social.example.org/") }));
        Assert.Throws<ArgumentException>(() => new MastodonChannel("m", new HttpClient(), new MastodonChannelOptions { InstanceUrl = new Uri("https://social.example.org/"), AccessToken = Token, MaxCharacters = 0 }));
    }

    [Fact]
    public void Options_bind_from_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["masto:InstanceUrl"] = "https://social.example.org/",
            ["masto:AccessToken"] = Token,
            ["masto:Visibility"] = "private",
            ["masto:MaxCharacters"] = "5000",
        }).Build();
        var services = new ServiceCollection();
        services.AddHulaki().AddMastodon("masto", configuration.GetSection("masto"));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();

        Assert.Equal(5000, provider.GetRequiredKeyedService<IChannel>("masto").Capabilities.TextLimit.Max);
        Assert.Equal("private", provider.GetRequiredService<IOptionsMonitor<MastodonChannelOptions>>().Get("masto").Visibility);
    }

    [Theory]
    [InlineData("followers", "Visibility")]
    [InlineData(null, "AccessToken is empty")]
    public void Validate_on_start_reports_bad_options_without_the_token(string? visibility, string expected)
    {
        var services = new ServiceCollection();
        services.AddHulaki().AddMastodon("masto", o =>
        {
            o.InstanceUrl = new Uri("https://social.example.org/");
            o.AccessToken = visibility is null ? string.Empty : Token;
            o.Visibility = visibility;
        });
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, ex.Message, StringComparison.Ordinal);
    }
}

/// <summary>With MaxCharacters set the channel trusts it and makes one request per send, as the kit scripts.</summary>
public sealed class MastodonContractTests : ChannelContractTests<MastodonChannel>
{
    protected override Recipient ValidRecipient => Recipient.Self;

    protected override MastodonChannel Create(HttpMessageHandler handler, TimeProvider time) =>
        new("masto", new HttpClient(handler), new MastodonChannelOptions
        {
            InstanceUrl = new Uri("https://social.example.org/"),
            AccessToken = MastodonChannelTests.Token,
            MaxCharacters = 500,
            TimeProvider = time,
        });

    protected override HttpResponseMessage Success() =>
        new(HttpStatusCode.OK) { Content = new StringContent(MastodonChannelTests.Posted, System.Text.Encoding.UTF8, "application/json") };
}

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

namespace Hulaki.Ntfy.Tests;

/// <summary>
/// Bodies follow https://docs.ntfy.sh/publish/ (publish as JSON, response, limitations) and the
/// error JSON of the ntfy server (<c>{"code":42901,"http":429,"error":...,"link":...}</c>).
/// Topics and tokens are synthetic.
/// </summary>
public sealed class NtfyChannelTests
{
    internal const string Topic = "hulaki-test-topic_4f7c";
    private const string Token = "tk_TESTtoken00000000000000000000";

    private static (NtfyChannel Channel, ScriptedHttpHandler Stub) Create(Action<NtfyChannelOptions>? configure = null, FakeTimeProvider? time = null)
    {
        var stub = new ScriptedHttpHandler();
        var options = new NtfyChannelOptions { TimeProvider = time ?? new FakeTimeProvider(), DisableRateLimiting = true };
        configure?.Invoke(options);
        return (new NtfyChannel("ntfy", new HttpClient(stub), options), stub);
    }

    private static JsonElement Body(RecordedRequest request) => JsonDocument.Parse(request.Body).RootElement;

    private const string Published = """{"id":"sPs71M8A2T","time":1758800000,"expires":1758843200,"event":"message","topic":"hulaki-test-topic_4f7c","message":"x"}""";

    [Fact]
    public async Task Publishes_json_to_the_server_root_with_every_field()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.OK, Published);
        var message = new Message("Rain warning")
        {
            Title = "Route alert",
            Priority = MessagePriority.High,
            Link = new Uri("https://example.org/watch/42"),
        };

        var outcome = await channel.SendAsync(message, new Recipient(Topic), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Delivered, outcome.Status);
        Assert.Equal("sPs71M8A2T", outcome.PlatformMessageId);
        var request = Assert.Single(stub.Requests);
        Assert.Equal("https://ntfy.sh/", request.Request.RequestUri!.AbsoluteUri);
        Assert.Null(request.Request.Headers.Authorization);
        var body = Body(request);
        Assert.Equal(Topic, body.GetProperty("topic").GetString());
        Assert.Equal("Rain warning", body.GetProperty("message").GetString());
        Assert.Equal("Route alert", body.GetProperty("title").GetString());
        Assert.Equal(4, body.GetProperty("priority").GetInt32());
        Assert.Equal("https://example.org/watch/42", body.GetProperty("click").GetString());
        Assert.False(body.TryGetProperty("markdown", out _));
    }

    [Theory]
    [InlineData(MessagePriority.Low, 2)]
    [InlineData(MessagePriority.Normal, 3)]
    [InlineData(MessagePriority.High, 4)]
    [InlineData(MessagePriority.Urgent, 5)]
    public void Priority_maps_to_ntfy_levels(MessagePriority priority, int expected) =>
        Assert.Equal(expected, NtfyChannel.PriorityOf(priority));

    [Fact]
    public async Task Access_token_is_a_bearer_header_on_a_self_hosted_server()
    {
        var (channel, stub) = Create(o =>
        {
            o.BaseAddress = new Uri("https://ntfy.example.org/base/");
            o.AccessToken = Token;
        });
        stub.Respond(HttpStatusCode.OK, Published);

        await channel.SendAsync(new Message("x"), new Recipient(Topic), TestContext.Current.CancellationToken);

        var request = Assert.Single(stub.Requests).Request;
        Assert.Equal("https://ntfy.example.org/base/", request.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(Token, request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Markup_is_sent_as_escaped_markdown_with_the_flag_set()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.OK, Published);
        var message = new Message(@"**Red** in _Myagdi_: 5\*3 # <b> [DHM](https://dhm.gov.np) `a_b`") { Format = TextFormat.Markup };

        await channel.SendAsync(message, new Recipient(Topic), TestContext.Current.CancellationToken);

        var body = Body(Assert.Single(stub.Requests));
        Assert.True(body.GetProperty("markdown").GetBoolean());
        Assert.Equal(@"**Red** in *Myagdi*: 5\*3 \# \<b\> [DHM](https://dhm.gov.np/) `a_b`", body.GetProperty("message").GetString());
    }

    [Fact]
    public void Plain_text_is_sent_unchanged() =>
        Assert.Equal("a_b *c* <d>", NtfyChannel.Render(new Message("a_b *c* <d>")));

    [Fact]
    public void The_limit_is_4096_utf8_bytes_of_body_and_the_title_does_not_count()
    {
        var (channel, _) = Create();
        var devanagari = new string('क', 1365); // 3 bytes each: 4095 bytes

        Assert.Empty(channel.Prepare(new Message(devanagari) { Title = new string('t', 500) }, new Recipient(Topic)));
        Assert.Contains(channel.Prepare(new Message(devanagari + "कि"), new Recipient(Topic)), i => i.Code == "text-too-long");
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("slash/topic")]
    [InlineData("a-topic-name-that-is-far-longer-than-sixty-four-characters-allowed-by-ntfy")]
    public void Invalid_topics_are_rejected_in_prepare(string topic)
    {
        var (channel, _) = Create();

        Assert.Contains(channel.Prepare(new Message("x"), new Recipient(topic)), i => i.Code == "invalid-topic");
    }

    public static TheoryData<HttpStatusCode, string, HulakiErrorCode, string> DocumentedErrors() => new()
    {
        { HttpStatusCode.Unauthorized, """{"code":40101,"http":401,"error":"unauthorized","link":"https://ntfy.sh/docs/publish/#authentication"}""", HulakiErrorCode.InvalidConfiguration, "40101" },
        { HttpStatusCode.Forbidden, """{"code":40301,"http":403,"error":"forbidden","link":"https://ntfy.sh/docs/publish/#authentication"}""", HulakiErrorCode.PermissionDenied, "40301" },
        { HttpStatusCode.BadRequest, """{"code":40009,"http":400,"error":"invalid request: topic invalid"}""", HulakiErrorCode.InvalidInput, "40009" },
        { HttpStatusCode.RequestEntityTooLarge, """{"code":41301,"http":413,"error":"attachment too large, or bandwidth limit reached"}""", HulakiErrorCode.InvalidInput, "41301" },
        { (HttpStatusCode)507, """{"code":50701,"http":507,"error":"insufficient storage"}""", HulakiErrorCode.InvalidInput, "50701" },
    };

    [Theory]
    [MemberData(nameof(DocumentedErrors))]
    public async Task Documented_errors_map_to_codes_and_are_not_retried(HttpStatusCode status, string body, HulakiErrorCode code, string platformCode)
    {
        var (channel, stub) = Create();
        stub.Respond(status, body);

        var outcome = await channel.SendAsync(new Message("x"), new Recipient(Topic), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Failed, outcome.Status);
        Assert.Equal(code, outcome.Error!.Code);
        Assert.Equal(RetryDisposition.Never, outcome.Error.Retry);
        Assert.Equal(platformCode, outcome.Error.PlatformCode);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task Too_many_requests_42901_is_retried_after_a_backoff()
    {
        var time = new FakeTimeProvider();
        var (channel, stub) = Create(time: time);
        stub.Respond(HttpStatusCode.TooManyRequests, """{"code":42901,"http":429,"error":"limit reached: too many requests","link":"https://ntfy.sh/docs/publish/#limitations"}""")
            .Respond(HttpStatusCode.OK, Published);

        var outcome = await FakeClock.RunAsync(time, () => channel.SendAsync(new Message("x"), new Recipient(Topic), TestContext.Current.CancellationToken));

        Assert.True(outcome.Succeeded);
        Assert.Equal(2, outcome.Attempts);
    }

    [Fact]
    public async Task Html_error_page_from_a_proxy_is_unknown_on_502()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.BadGateway, "<html>bad gateway</html>", "text/html");

        var outcome = await channel.SendAsync(new Message("x"), new Recipient(Topic), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Unknown, outcome.Status);
    }

    [Fact]
    public async Task Empty_200_is_unknown_not_delivered()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.OK, string.Empty);

        var outcome = await channel.SendAsync(new Message("x"), new Recipient(Topic), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Unknown, outcome.Status);
    }

    [Fact]
    public void Constructor_rejects_a_relative_base_address()
    {
        Assert.Throws<ArgumentException>(() => new NtfyChannel("ntfy", new HttpClient(), new NtfyChannelOptions { BaseAddress = new Uri("/relative", UriKind.Relative) }));
    }

    [Fact]
    public void Options_bind_from_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ops:BaseAddress"] = "http://localhost:8090/",
            ["ops:AccessToken"] = Token,
            ["ops:DisableRateLimiting"] = "true",
        }).Build();
        var services = new ServiceCollection();
        services.AddHulaki().AddNtfy("ops", configuration.GetSection("ops"));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();
        var options = provider.GetRequiredService<IOptionsMonitor<NtfyChannelOptions>>().Get("ops");

        Assert.Equal(new Uri("http://localhost:8090/"), options.BaseAddress);
        Assert.Equal(Token, options.AccessToken);
        Assert.True(options.DisableRateLimiting);
        Assert.IsType<NtfyChannel>(provider.GetRequiredKeyedService<IChannel>("ops"));
    }

    [Fact]
    public void Validate_on_start_fails_on_a_plain_http_remote_server()
    {
        var services = new ServiceCollection();
        services.AddHulaki().AddNtfy("ops", o =>
        {
            o.BaseAddress = new Uri("http://ntfy.example.org/");
            o.AccessToken = Token;
        });
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("BaseAddress", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, ex.Message, StringComparison.Ordinal);
    }
}

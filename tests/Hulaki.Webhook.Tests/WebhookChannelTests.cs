using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Hulaki.Testing;
using Hulaki.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Hulaki.Webhook.Tests;

public sealed class WebhookChannelTests
{
    internal const string Url = "https://hooks.example.org/hulaki/in";
    private const string Secret = "TEST-shared-secret_000000";

    private static (WebhookChannel Channel, ScriptedHttpHandler Stub) Create(Action<WebhookChannelOptions>? configure = null, FakeTimeProvider? time = null)
    {
        var stub = new ScriptedHttpHandler();
        var options = new WebhookChannelOptions { Url = new Uri(Url), TimeProvider = time ?? new FakeTimeProvider() };
        configure?.Invoke(options);
        return (new WebhookChannel("hook", new HttpClient(stub), options), stub);
    }

    [Fact]
    public async Task Posts_the_default_json_body()
    {
        var (channel, stub) = Create();
        stub.Respond(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var message = new Message("**Orange** rain [DHM](https://dhm.gov.np)")
        {
            Format = TextFormat.Markup,
            Title = "Route alert",
            Priority = MessagePriority.Urgent,
            Link = new Uri("https://example.org/watch/42"),
        };

        var outcome = await channel.SendAsync(message, Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Delivered, outcome.Status);
        var request = Assert.Single(stub.Requests);
        Assert.Equal(Url, request.Request.RequestUri!.AbsoluteUri);
        Assert.Equal("application/json", request.Request.Content!.Headers.ContentType!.MediaType);
        Assert.False(request.Request.Headers.Contains("X-Hulaki-Signature"));
        var body = JsonDocument.Parse(request.Body).RootElement;
        Assert.Equal("Route alert", body.GetProperty("title").GetString());
        Assert.Equal("Orange rain DHM (https://dhm.gov.np)", body.GetProperty("text").GetString());
        Assert.Equal("urgent", body.GetProperty("priority").GetString());
        Assert.Equal("https://example.org/watch/42", body.GetProperty("link").GetString());
    }

    [Fact]
    public void Null_fields_are_left_out_of_the_default_body()
    {
        var body = Encoding.UTF8.GetString(WebhookChannel.BuildBody(new Message("x"), new WebhookChannelOptions()));

        Assert.Equal("""{"text":"x","priority":"normal"}""", body);
    }

    [Fact]
    public void Signature_matches_the_published_hmac_sha256_vector()
    {
        // The example HMAC_SHA256("key", "The quick brown fox jumps over the lazy dog") from the Wikipedia HMAC article.
        var signature = WebhookChannel.Sign(Encoding.UTF8.GetBytes("key"), Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog"));

        Assert.Equal("sha256=f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8", signature);
    }

    [Fact]
    public async Task With_a_secret_the_signature_covers_the_raw_body_bytes()
    {
        var (channel, stub) = Create(o => o.Secret = Secret);
        stub.Respond(HttpStatusCode.OK, "{}");

        await channel.SendAsync(new Message("वर्षा चेतावनी \"quoted\""), Recipient.Self, TestContext.Current.CancellationToken);

        var request = Assert.Single(stub.Requests);
        var expected = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(request.Body)));
        Assert.Equal(expected, Assert.Single(request.Request.Headers.GetValues("X-Hulaki-Signature")));
    }

    [Fact]
    public void A_json_template_escapes_values_for_a_json_string()
    {
        var options = new WebhookChannelOptions { BodyTemplate = """{"content":"{{title}}: {{text}}","level":"{{priority}}","url":"{{link}}"}""" };
        var message = new Message("Line 1\n\"Myagdi\" \\ वर्षा") { Title = "Route <alert>", Priority = MessagePriority.High };

        var body = Encoding.UTF8.GetString(WebhookChannel.BuildBody(message, options));

        var parsed = JsonDocument.Parse(body).RootElement;
        Assert.Equal("Route <alert>: Line 1\n\"Myagdi\" \\ वर्षा", parsed.GetProperty("content").GetString());
        Assert.Equal("high", parsed.GetProperty("level").GetString());
        Assert.Equal(string.Empty, parsed.GetProperty("url").GetString());
    }

    [Fact]
    public void A_form_template_url_encodes_values()
    {
        var options = new WebhookChannelOptions { BodyTemplate = "title={{title}}&body={{text}}", ContentType = "application/x-www-form-urlencoded" };

        var body = Encoding.UTF8.GetString(WebhookChannel.BuildBody(new Message("a&b=c d") { Title = "t?" }, options));

        Assert.Equal("title=t%3F&body=a%26b%3Dc+d", body);
    }

    [Fact]
    public async Task Configured_headers_and_content_type_are_sent()
    {
        var (channel, stub) = Create(o =>
        {
            o.Headers["Authorization"] = "Bearer TEST-receiver-token";
            o.Headers["X-Tenant"] = "t-1";
            o.BodyTemplate = "{{text}}";
            o.ContentType = "text/plain; charset=utf-8";
        });
        stub.Respond(HttpStatusCode.OK, string.Empty);

        await channel.SendAsync(new Message("plain body"), Recipient.Self, TestContext.Current.CancellationToken);

        var request = Assert.Single(stub.Requests);
        Assert.Equal("Bearer TEST-receiver-token", request.Request.Headers.Authorization!.ToString());
        Assert.Equal("t-1", Assert.Single(request.Request.Headers.GetValues("X-Tenant")));
        Assert.Equal("text/plain", request.Request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("plain body", request.Body);
    }

    [Fact]
    public async Task Accepted_is_accepted_not_delivered()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.Accepted, string.Empty);

        var outcome = await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Accepted, outcome.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, DeliveryStatus.Failed, HulakiErrorCode.InvalidInput)]
    [InlineData(HttpStatusCode.Unauthorized, DeliveryStatus.Failed, HulakiErrorCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound, DeliveryStatus.Failed, HulakiErrorCode.RecipientNotFound)]
    [InlineData(HttpStatusCode.InternalServerError, DeliveryStatus.Unknown, HulakiErrorCode.UpstreamFailure)]
    public async Task Errors_follow_the_http_status(HttpStatusCode status, DeliveryStatus expected, HulakiErrorCode code)
    {
        var (channel, stub) = Create();
        stub.Respond(status, "<html>error page with the text echoed: x</html>", "text/html");

        var outcome = await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(expected, outcome.Status);
        Assert.Equal(code, outcome.Error!.Code);
        Assert.Equal($"The webhook returned {(int)status}.", outcome.Error.Message);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task A_503_is_retried_after_the_retry_after_header()
    {
        var time = new FakeTimeProvider();
        var (channel, stub) = Create(time: time);
        stub.Respond(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(4));
                return response;
            })
            .Respond(HttpStatusCode.OK, string.Empty);
        var started = time.GetUtcNow();

        var outcome = await FakeClock.RunAsync(time, () => channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken));

        Assert.True(outcome.Succeeded);
        Assert.True(time.GetUtcNow() - started >= TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void The_text_limit_follows_the_options()
    {
        var (channel, _) = Create(o => o.MaxTextBytes = 10);

        Assert.Equal(10, channel.Capabilities.TextLimit.Max);
        Assert.Equal(65536, WebhookChannel.Manifest.TextLimit.Max);
        Assert.Empty(channel.Prepare(new Message("0123456789") { Title = "a long title does not count" }, Recipient.Self));
        Assert.Contains(channel.Prepare(new Message("क्षि"), Recipient.Self), i => i.Code == "text-too-long");
    }

    [Fact]
    public void A_recipient_other_than_self_is_rejected_in_prepare()
    {
        var (channel, _) = Create();

        Assert.Contains(channel.Prepare(new Message("x"), new Recipient("someone")), i => i.Code == "recipient-must-be-self");
    }

    [Fact]
    public void Constructor_rejects_a_missing_url_and_a_bad_limit()
    {
        Assert.Throws<ArgumentException>(() => new WebhookChannel("hook", new HttpClient(), new WebhookChannelOptions()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WebhookChannel("hook", new HttpClient(), new WebhookChannelOptions { Url = new Uri(Url), MaxTextBytes = 0 }));
    }

    [Fact]
    public void Options_bind_from_configuration_including_headers()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["hook:Url"] = Url,
            ["hook:Secret"] = Secret,
            ["hook:Headers:X-Tenant"] = "t-1",
            ["hook:BodyTemplate"] = "{{text}}",
            ["hook:ContentType"] = "text/plain",
            ["hook:MaxTextBytes"] = "2000",
        }).Build();
        var services = new ServiceCollection();
        services.AddHulaki().AddWebhook("hook", configuration.GetSection("hook"));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();
        var options = provider.GetRequiredService<IOptionsMonitor<WebhookChannelOptions>>().Get("hook");

        Assert.Equal(new Uri(Url), options.Url);
        Assert.Equal(Secret, options.Secret);
        Assert.Equal("t-1", options.Headers["x-tenant"]);
        Assert.Equal("{{text}}", options.BodyTemplate);
        Assert.Equal("text/plain", options.ContentType);
        Assert.Equal(2000, provider.GetRequiredKeyedService<IChannel>("hook").Capabilities.TextLimit.Max);
    }

    [Theory]
    [InlineData("http://hooks.example.org/in", "Url must be")]
    [InlineData("https://hooks.example.org/in", "ContentType")]
    public void Validate_on_start_fails_on_bad_options_without_echoing_secrets(string address, string expected)
    {
        var services = new ServiceCollection();
        services.AddHulaki().AddWebhook("hook", o =>
        {
            o.Url = new Uri(address);
            o.Secret = Secret;
            o.ContentType = expected == "ContentType" ? "not a media type" : "application/json";
        });
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("hooks.example.org", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_captured_contains_the_url_secret_or_text()
    {
        using var telemetry = new TelemetryCapture();
        var logs = new CapturingLoggerFactory();
        using var stub = new ScriptedHttpHandler();
        stub.Respond(HttpStatusCode.BadRequest, "canary 3a7f echoed");
        var name = "hook-redaction-" + Guid.NewGuid().ToString("N");
        using var channel = new WebhookChannel(name, new HttpClient(stub), new WebhookChannelOptions { Url = new Uri(Url), Secret = Secret, LoggerFactory = logs });

        var outcome = await channel.SendAsync(new Message("canary 3a7f text"), Recipient.Self, TestContext.Current.CancellationToken);

        IEnumerable<string> captured =
        [
            .. logs.Logs.SelectMany(l => l.AllText()),
            .. telemetry.Spans.Where(s => (s.GetTagItem("hulaki.channel") as string) == name).SelectMany(s => s.TagObjects.Select(t => $"{t.Key}={t.Value}")),
            outcome.ToString(),
            outcome.Error!.Message,
        ];
        Assert.NotEmpty(logs.Logs);
        foreach (var secret in new[] { "hooks.example.org", Secret, "canary 3a7f" })
        {
            Assert.DoesNotContain(captured, text => text.Contains(secret, StringComparison.OrdinalIgnoreCase));
        }
    }
}

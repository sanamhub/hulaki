using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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

namespace Hulaki.WebPush.Tests;

/// <summary>
/// RFC 8030 (push protocol), RFC 8291 (encryption, with its section 5 example) and RFC 8292
/// (VAPID). Push service answers are status codes; their bodies are not read.
/// </summary>
public sealed class WebPushChannelTests
{
    private static WebPushChannelOptions Options(FakeTimeProvider? time = null) => new()
    {
        VapidPublicKey = VapidKeys.Shared.PublicKey,
        VapidPrivateKey = VapidKeys.Shared.PrivateKey,
        VapidSubject = "mailto:ops@example.org",
        TimeProvider = time ?? new FakeTimeProvider(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero)),
    };

    private static (WebPushChannel Channel, ScriptedHttpHandler Stub) Create(WebPushChannelOptions? options = null)
    {
        var stub = new ScriptedHttpHandler();
        return (new WebPushChannel("push", new HttpClient(stub), options ?? Options()), stub);
    }

    private static HttpResponseMessage Created() => new(HttpStatusCode.Created);

    /// <summary>
    /// Answers 201 and keeps the body bytes and content headers. The channel disposes its request
    /// after the send, and the handler's recorded body is text, so binary bodies are read here.
    /// </summary>
    private static Func<HttpRequestMessage, HttpResponseMessage> Capture(List<SentBody> sent) => request =>
    {
        using var stream = request.Content!.ReadAsStream();
        using var copy = new System.IO.MemoryStream();
        stream.CopyTo(copy);
        sent.Add(new SentBody(copy.ToArray(), [.. request.Content.Headers.ContentEncoding], request.Content.Headers.ContentType?.MediaType));
        return Created();
    };

    private sealed record SentBody(byte[] Bytes, IReadOnlyList<string> ContentEncoding, string? ContentType);

    [Fact]
    public void Encryption_reproduces_the_rfc_8291_section_5_example()
    {
        using var sender = Rfc8291.SenderKey();

        var body = WebPushEncryption.Encrypt(
            Encoding.ASCII.GetBytes(Rfc8291.Plaintext),
            Base64Url.DecodeFromChars(Rfc8291.ReceiverPublic),
            Base64Url.DecodeFromChars(Rfc8291.AuthSecret),
            sender,
            Base64Url.DecodeFromChars(Rfc8291.Salt));

        Assert.Equal(Rfc8291.Body, Base64Url.EncodeToString(body));
        Assert.Equal(Rfc8291.Plaintext, Rfc8291.DecryptText(body));
    }

    [Fact]
    public async Task A_send_through_the_seam_with_the_rfc_keys_is_what_a_browser_decrypts()
    {
        var stub = new ScriptedHttpHandler();
        var sent = new List<SentBody>();
        stub.Respond(Capture(sent));
        using var channel = new WebPushChannel("push", new HttpClient(stub), Options(), Rfc8291.SenderKey, () => Base64Url.DecodeFromChars(Rfc8291.Salt));
        var message = new Message("**Orange** rain [DHM](https://dhm.gov.np)")
        {
            Format = TextFormat.Markup,
            Title = "Route alert",
            Link = new Uri("https://example.org/watch/42"),
        };

        var outcome = await channel.SendAsync(message, Rfc8291.Subscriber(), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Accepted, outcome.Status);
        var body = Assert.Single(sent).Bytes;
        Assert.Equal(Base64Url.DecodeFromChars(Rfc8291.Salt), body[..16]);
        var payload = JsonDocument.Parse(Rfc8291.Decrypt(body)).RootElement;
        Assert.Equal("Route alert", payload.GetProperty("title").GetString());
        Assert.Equal("Orange rain DHM (https://dhm.gov.np)", payload.GetProperty("body").GetString());
        Assert.Equal("https://example.org/watch/42", payload.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Each_message_uses_a_new_server_key_and_salt()
    {
        var (channel, stub) = Create();
        var sent = new List<SentBody>();
        stub.Respond(Capture(sent)).Respond(Capture(sent));

        await channel.SendAsync(new Message("x"), Rfc8291.Subscriber(), TestContext.Current.CancellationToken);
        await channel.SendAsync(new Message("x"), Rfc8291.Subscriber(), TestContext.Current.CancellationToken);

        var bodies = sent.Select(b => b.Bytes).ToArray();

        Assert.NotEqual(bodies[0][..86], bodies[1][..86]);
        Assert.Equal("{\"body\":\"x\"}", Rfc8291.DecryptText(bodies[0]));
        Assert.Equal("{\"body\":\"x\"}", Rfc8291.DecryptText(bodies[1]));
    }

    [Fact]
    public async Task Headers_follow_rfc_8030_and_8292()
    {
        var (channel, stub) = Create();
        var sent = new List<SentBody>();
        stub.Respond(Capture(sent));

        await channel.SendAsync(new Message("x") { Priority = MessagePriority.Urgent }, Rfc8291.Subscriber(), TestContext.Current.CancellationToken);

        var request = Assert.Single(stub.Requests).Request;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://push.example.net/push/JzLQ3raZJfFBR0aqvOMsLrt54w4rJUsV", request.RequestUri!.AbsoluteUri);
        Assert.Equal("aes128gcm", Assert.Single(Assert.Single(sent).ContentEncoding));
        Assert.Equal("application/octet-stream", sent[0].ContentType);
        Assert.Equal("86400", Assert.Single(request.Headers.GetValues("TTL")));
        Assert.Equal("high", Assert.Single(request.Headers.GetValues("Urgency")));
        Assert.Equal("vapid", request.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task The_vapid_jwt_is_es256_for_the_endpoint_origin_and_verifies()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));
        var (channel, stub) = Create(Options(time));
        stub.Respond(_ => Created());

        await channel.SendAsync(new Message("x"), Rfc8291.Subscriber("https://fcm.googleapis.com:443/wp/abc123"), TestContext.Current.CancellationToken);

        var parameter = Assert.Single(stub.Requests).Request.Headers.Authorization!.Parameter!;
        var parts = parameter.Split(", ");
        Assert.StartsWith("t=", parts[0], StringComparison.Ordinal);
        Assert.Equal("k=" + VapidKeys.Shared.PublicKey, parts[1]);
        var jwt = parts[0][2..].Split('.');
        Assert.Equal("""{"typ":"JWT","alg":"ES256"}""", Encoding.UTF8.GetString(Base64Url.DecodeFromChars(jwt[0])));
        var claims = JsonDocument.Parse(Base64Url.DecodeFromChars(jwt[1])).RootElement;
        Assert.Equal("https://fcm.googleapis.com", claims.GetProperty("aud").GetString());
        Assert.Equal("mailto:ops@example.org", claims.GetProperty("sub").GetString());
        var expires = DateTimeOffset.FromUnixTimeSeconds(claims.GetProperty("exp").GetInt64());
        Assert.True(expires > time.GetUtcNow() && expires <= time.GetUtcNow().AddHours(24));

        var q = Base64Url.DecodeFromChars(VapidKeys.Shared.PublicKey);
        using var verifier = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = q[1..33], Y = q[33..] } });
        var signature = Base64Url.DecodeFromChars(jwt[2]);
        Assert.Equal(64, signature.Length);
        Assert.True(verifier.VerifyData(Encoding.ASCII.GetBytes(jwt[0] + "." + jwt[1]), signature, HashAlgorithmName.SHA256));
    }

    [Fact]
    public async Task One_token_serves_an_audience_until_an_hour_before_it_expires()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));
        var (channel, stub) = Create(Options(time));
        for (var i = 0; i < 4; i++)
        {
            stub.Respond(_ => Created());
        }

        await channel.SendAsync(new Message("x"), Rfc8291.Subscriber("https://push.example.net/a"), TestContext.Current.CancellationToken);
        await channel.SendAsync(new Message("x"), Rfc8291.Subscriber("https://push.example.net/b"), TestContext.Current.CancellationToken);
        await channel.SendAsync(new Message("x"), Rfc8291.Subscriber("https://other.example.net/c"), TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromHours(11.5));
        await channel.SendAsync(new Message("x"), Rfc8291.Subscriber("https://push.example.net/d"), TestContext.Current.CancellationToken);

        var tokens = stub.Requests.Select(r => r.Request.Headers.Authorization!.Parameter!.Split(", ")[0]).ToArray();
        Assert.Equal(tokens[0], tokens[1]);
        Assert.NotEqual(tokens[0], tokens[2]);
        Assert.NotEqual(tokens[0], tokens[3]);
    }

    [Theory]
    [InlineData(MessagePriority.Low, "very-low")]
    [InlineData(MessagePriority.Normal, "normal")]
    [InlineData(MessagePriority.High, "high")]
    [InlineData(MessagePriority.Urgent, "high")]
    public void Priority_maps_to_urgency(MessagePriority priority, string urgency) =>
        Assert.Equal(urgency, WebPushChannel.UrgencyOf(priority));

    public static TheoryData<HttpStatusCode, HulakiErrorCode, RetryDisposition> Answers() => new()
    {
        { HttpStatusCode.NotFound, HulakiErrorCode.RecipientNotFound, RetryDisposition.Never },
        { HttpStatusCode.Gone, HulakiErrorCode.RecipientNotFound, RetryDisposition.Never },
        { HttpStatusCode.Unauthorized, HulakiErrorCode.InvalidConfiguration, RetryDisposition.Never },
        { HttpStatusCode.Forbidden, HulakiErrorCode.InvalidConfiguration, RetryDisposition.Never },
        { HttpStatusCode.RequestEntityTooLarge, HulakiErrorCode.InvalidInput, RetryDisposition.Never },
        { HttpStatusCode.BadRequest, HulakiErrorCode.InvalidInput, RetryDisposition.Never },
    };

    [Theory]
    [MemberData(nameof(Answers))]
    public async Task Push_service_errors_map_by_status(HttpStatusCode status, HulakiErrorCode code, RetryDisposition retry)
    {
        var (channel, stub) = Create();
        stub.Respond(status, "push subscription has unsubscribed or expired.", "text/plain");

        var outcome = await channel.SendAsync(new Message("x"), Rfc8291.Subscriber(), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Failed, outcome.Status);
        Assert.Equal(code, outcome.Error!.Code);
        Assert.Equal(retry, outcome.Error.Retry);
        Assert.Equal((int)status, outcome.Error.HttpStatus);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task A_429_is_retried_after_retry_after()
    {
        var time = new FakeTimeProvider();
        var (channel, stub) = Create(Options(time));
        stub.Respond(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
                return response;
            })
            .Respond(_ => Created());
        var started = time.GetUtcNow();

        var outcome = await FakeClock.RunAsync(time, () => channel.SendAsync(new Message("x"), Rfc8291.Subscriber(), TestContext.Current.CancellationToken));

        Assert.Equal(DeliveryStatus.Accepted, outcome.Status);
        Assert.True(time.GetUtcNow() - started >= TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task A_500_is_unknown()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.InternalServerError, "<html>oops</html>", "text/html");

        var outcome = await channel.SendAsync(new Message("x"), Rfc8291.Subscriber(), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Unknown, outcome.Status);
    }

    [Fact]
    public async Task A_p256dh_that_is_not_on_the_curve_fails_without_a_request()
    {
        var (channel, stub) = Create();
        var bogus = Base64Url.EncodeToString([4, .. Enumerable.Repeat((byte)1, 64)]);
        var subscriber = new Recipient("https://push.example.net/x", new Dictionary<string, string> { ["p256dh"] = bogus, ["auth"] = Rfc8291.AuthSecret });

        var outcome = await channel.SendAsync(new Message("x"), subscriber, TestContext.Current.CancellationToken);

        Assert.Equal(HulakiErrorCode.InvalidInput, outcome.Error!.Code);
        Assert.Empty(stub.Requests);
    }

    public static TheoryData<string, string?, string?, string> BadSubscriptions() => new()
    {
        { "http://push.example.net/x", Rfc8291.ReceiverPublic, Rfc8291.AuthSecret, "invalid-endpoint" },
        { "https://127.0.0.1/x", Rfc8291.ReceiverPublic, Rfc8291.AuthSecret, "invalid-endpoint" },
        { "https://localhost/x", Rfc8291.ReceiverPublic, Rfc8291.AuthSecret, "invalid-endpoint" },
        { "https://[::1]/x", Rfc8291.ReceiverPublic, Rfc8291.AuthSecret, "invalid-endpoint" },
        { "not a url", Rfc8291.ReceiverPublic, Rfc8291.AuthSecret, "invalid-endpoint" },
        { "https://push.example.net/x", null, Rfc8291.AuthSecret, "invalid-p256dh" },
        { "https://push.example.net/x", "AAAA", Rfc8291.AuthSecret, "invalid-p256dh" },
        { "https://push.example.net/x", Rfc8291.ReceiverPublic, null, "invalid-auth" },
        { "https://push.example.net/x", Rfc8291.ReceiverPublic, "AAAA", "invalid-auth" },
    };

    [Theory]
    [MemberData(nameof(BadSubscriptions))]
    public void Bad_subscriptions_are_rejected_in_prepare(string endpoint, string? p256dh, string? auth, string issue)
    {
        var (channel, _) = Create();
        var properties = new Dictionary<string, string>();
        if (p256dh is not null)
        {
            properties["p256dh"] = p256dh;
        }

        if (auth is not null)
        {
            properties["auth"] = auth;
        }

        Assert.Contains(channel.Prepare(new Message("x"), new Recipient(endpoint, properties)), i => i.Code == issue);
    }

    [Fact]
    public void Standard_base64_with_padding_is_accepted_for_keys()
    {
        var (channel, _) = Create();
        var standard = Convert.ToBase64String(Base64Url.DecodeFromChars(Rfc8291.ReceiverPublic));
        var subscriber = new Recipient("https://push.example.net/x", new Dictionary<string, string> { ["p256dh"] = standard, ["auth"] = "BTBZMqHH6r4Tts7J/aSIgg==" });

        Assert.Empty(channel.Prepare(new Message("x"), subscriber));
    }

    [Fact]
    public void The_payload_limit_is_3993_bytes_of_json()
    {
        var (channel, _) = Create();
        var fits = new string('a', 3993 - "{\"body\":\"\"}".Length);

        Assert.Empty(channel.Prepare(new Message(fits), Rfc8291.Subscriber()));
        Assert.Contains(channel.Prepare(new Message(fits + "a"), Rfc8291.Subscriber()), i => i.Code == "text-too-long");
    }

    [Fact]
    public void Constructor_rejects_a_mismatched_key_pair_without_echoing_either_key()
    {
        var other = VapidKeys.Create();
        var options = Options();
        options.VapidPrivateKey = other.PrivateKey;

        var ex = Assert.Throws<ArgumentException>(() => new WebPushChannel("push", new HttpClient(), options));

        Assert.Contains("does not belong", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(other.PrivateKey, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(VapidKeys.Shared.PublicKey, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ops@example.org")]
    [InlineData("http://example.org/contact")]
    [InlineData("")]
    public void Constructor_rejects_a_subject_that_is_not_mailto_or_https(string subject)
    {
        var options = Options();
        options.VapidSubject = subject;

        Assert.Contains("VapidSubject", Assert.Throws<ArgumentException>(() => new WebPushChannel("push", new HttpClient(), options)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_bind_from_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["push:VapidPublicKey"] = VapidKeys.Shared.PublicKey,
            ["push:VapidPrivateKey"] = VapidKeys.Shared.PrivateKey,
            ["push:VapidSubject"] = "https://example.org/contact",
            ["push:TimeToLive"] = "01:00:00",
        }).Build();
        var services = new ServiceCollection();
        services.AddHulaki().AddWebPush("push", configuration.GetSection("push"));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();
        var options = provider.GetRequiredService<IOptionsMonitor<WebPushChannelOptions>>().Get("push");

        Assert.Equal(TimeSpan.FromHours(1), options.TimeToLive);
        Assert.Equal("https://example.org/contact", options.VapidSubject);
        Assert.IsType<WebPushChannel>(provider.GetRequiredKeyedService<IChannel>("push"));
    }

    [Fact]
    public void Validate_on_start_fails_on_a_bad_private_key_without_echoing_it()
    {
        var services = new ServiceCollection();
        services.AddHulaki().AddWebPush("push", o =>
        {
            o.VapidPublicKey = VapidKeys.Shared.PublicKey;
            o.VapidPrivateKey = "TESTnotakey";
            o.VapidSubject = "mailto:ops@example.org";
        });
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("VapidPrivateKey", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("TESTnotakey", ex.Message, StringComparison.Ordinal);
    }
}

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Hulaki.Credentials;
using Hulaki.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Hulaki.Bluesky.Tests;

/// <summary>
/// Shapes follow docs.bsky.app: com.atproto.server.createSession and refreshSession,
/// com.atproto.repo.createRecord, the app.bsky.feed.post lexicon, rich-text facets and the rate
/// limit headers. Handles, DIDs, tokens and passwords are synthetic.
/// </summary>
public sealed class BlueskyChannelTests
{
    internal const string Did = "did:plc:testaaaaaaaaaaaaaaaaaaaa";
    internal const string AppPassword = "TEST-aaaa-bbbb-cccc";
    internal static readonly DateTimeOffset Start = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    /// <summary>An unsigned JWT with an exp claim; the channel reads exp and never verifies.</summary>
    internal static string Jwt(string subject, DateTimeOffset expires) =>
        Base64Url.EncodeToString("""{"typ":"at+jwt","alg":"ES256K"}"""u8) + "." +
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes($$"""{"scope":"com.atproto.appPass","sub":"{{subject}}","exp":{{expires.ToUnixTimeSeconds()}}}""")) +
        ".TESTsignature";

    private static string Session(string access, string refresh) =>
        $$"""{"did":"{{Did}}","handle":"alerts.example.org","accessJwt":"{{access}}","refreshJwt":"{{refresh}}","active":true}""";

    private static string Created(string rkey) =>
        $$"""{"uri":"at://{{Did}}/app.bsky.feed.post/{{rkey}}","cid":"bafyreitest{{rkey}}","commit":{"cid":"bafyreicommit","rev":"3ltest"},"validationStatus":"valid"}""";

    private static (BlueskyChannel Channel, ScriptedHttpHandler Stub, FakeTimeProvider Time) Create(Action<BlueskyChannelOptions>? configure = null, ICredentialStore? store = null)
    {
        var time = new FakeTimeProvider(Start);
        var stub = new ScriptedHttpHandler();
        var options = new BlueskyChannelOptions
        {
            Identifier = "alerts.example.org",
            AppPassword = AppPassword,
            TimeProvider = time,
            DisableRateLimiting = true,
            CredentialStore = store,
        };
        configure?.Invoke(options);
        return (new BlueskyChannel("bsky", new HttpClient(stub), options), stub, time);
    }

    private static JsonElement Record(RecordedRequest request) => JsonDocument.Parse(request.Body).RootElement.GetProperty("record");

    [Fact]
    public async Task Creates_a_session_then_the_post()
    {
        var (channel, stub, _) = Create(o => o.Languages.Add("ne"));
        stub.Respond(HttpStatusCode.OK, Session(Jwt("a1", Start.AddHours(2)), Jwt("r1", Start.AddDays(60))))
            .Respond(HttpStatusCode.OK, Created("3ltestpost1"));

        var outcome = await channel.SendAsync(new Message("Rain warning for Myagdi"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Delivered, outcome.Status);
        Assert.Equal($"at://{Did}/app.bsky.feed.post/3ltestpost1", outcome.PlatformMessageId);
        Assert.Equal(new Uri($"https://bsky.app/profile/{Did}/post/3ltestpost1"), outcome.Url);

        var login = stub.Requests[0];
        Assert.Equal("https://bsky.social/xrpc/com.atproto.server.createSession", login.Request.RequestUri!.AbsoluteUri);
        var credentials = JsonDocument.Parse(login.Body).RootElement;
        Assert.Equal("alerts.example.org", credentials.GetProperty("identifier").GetString());
        Assert.Equal(AppPassword, credentials.GetProperty("password").GetString());

        var post = stub.Requests[1];
        Assert.Equal("https://bsky.social/xrpc/com.atproto.repo.createRecord", post.Request.RequestUri!.AbsoluteUri);
        Assert.Equal(Jwt("a1", Start.AddHours(2)), post.Request.Headers.Authorization!.Parameter);
        var body = JsonDocument.Parse(post.Body).RootElement;
        Assert.Equal(Did, body.GetProperty("repo").GetString());
        Assert.Equal("app.bsky.feed.post", body.GetProperty("collection").GetString());
        var record = body.GetProperty("record");
        Assert.Equal("app.bsky.feed.post", record.GetProperty("$type").GetString());
        Assert.Equal("Rain warning for Myagdi", record.GetProperty("text").GetString());
        Assert.Equal("2026-09-25T10:00:00.000Z", record.GetProperty("createdAt").GetString());
        Assert.Equal("ne", record.GetProperty("langs")[0].GetString());
        Assert.False(record.TryGetProperty("facets", out _));
    }

    [Fact]
    public async Task The_session_is_reused_across_sends()
    {
        var (channel, stub, _) = Create();
        stub.Respond(HttpStatusCode.OK, Session(Jwt("a1", Start.AddHours(2)), Jwt("r1", Start.AddDays(60))))
            .Respond(HttpStatusCode.OK, Created("p1"))
            .Respond(HttpStatusCode.OK, Created("p2"));

        await channel.SendAsync(new Message("one"), Recipient.Self, TestContext.Current.CancellationToken);
        await channel.SendAsync(new Message("two"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Single(stub.Requests, r => r.Request.RequestUri!.AbsolutePath.EndsWith("createSession", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Two_channels_on_one_store_create_one_session()
    {
        var store = new InMemoryCredentialStore();
        var (first, stub1, _) = Create(store: store);
        var (second, stub2, _) = Create(store: store);
        stub1.Respond(HttpStatusCode.OK, Session(Jwt("a1", Start.AddHours(2)), Jwt("r1", Start.AddDays(60)))).Respond(HttpStatusCode.OK, Created("p1"));
        stub2.Respond(HttpStatusCode.OK, Created("p2"));

        await first.SendAsync(new Message("one"), Recipient.Self, TestContext.Current.CancellationToken);
        var outcome = await second.SendAsync(new Message("two"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.True(outcome.Succeeded);
        Assert.Single(stub2.Requests);
    }

    [Fact]
    public async Task A_session_near_expiry_is_refreshed_with_the_refresh_token()
    {
        var (channel, stub, time) = Create();
        stub.Respond(HttpStatusCode.OK, Session(Jwt("a1", Start.AddHours(2)), Jwt("r1", Start.AddDays(60))))
            .Respond(HttpStatusCode.OK, Created("p1"))
            .Respond(HttpStatusCode.OK, Session(Jwt("a2", Start.AddHours(4)), Jwt("r2", Start.AddDays(60))))
            .Respond(HttpStatusCode.OK, Created("p2"));

        await channel.SendAsync(new Message("one"), Recipient.Self, TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMinutes(116));
        var outcome = await channel.SendAsync(new Message("two"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.True(outcome.Succeeded);
        var refresh = stub.Requests[2].Request;
        Assert.Equal("https://bsky.social/xrpc/com.atproto.server.refreshSession", refresh.RequestUri!.AbsoluteUri);
        Assert.Equal(Jwt("r1", Start.AddDays(60)), refresh.Headers.Authorization!.Parameter);
        Assert.Equal(Jwt("a2", Start.AddHours(4)), stub.Requests[3].Request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Expired_token_on_create_record_refreshes_and_posts_once()
    {
        var (channel, stub, _) = Create();
        stub.Respond(HttpStatusCode.OK, Session(Jwt("a1", Start.AddHours(2)), Jwt("r1", Start.AddDays(60))))
            .Respond(HttpStatusCode.BadRequest, """{"error":"ExpiredToken","message":"Token has expired"}""")
            .Respond(HttpStatusCode.OK, Session(Jwt("a2", Start.AddHours(2)), Jwt("r2", Start.AddDays(60))))
            .Respond(HttpStatusCode.OK, Created("p1"));

        var outcome = await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Delivered, outcome.Status);
        Assert.Equal(1, outcome.Attempts);
        Assert.EndsWith("refreshSession", stub.Requests[2].Request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refreshed_session_refused_again_is_reconnect_required()
    {
        var (channel, stub, _) = Create();
        stub.Respond(HttpStatusCode.OK, Session(Jwt("a1", Start.AddHours(2)), Jwt("r1", Start.AddDays(60))))
            .Respond(HttpStatusCode.Unauthorized, """{"error":"InvalidToken","message":"Token could not be verified"}""")
            .Respond(HttpStatusCode.OK, Session(Jwt("a2", Start.AddHours(2)), Jwt("r2", Start.AddDays(60))))
            .Respond(HttpStatusCode.Unauthorized, """{"error":"InvalidToken","message":"Token could not be verified"}""");

        var outcome = await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(HulakiErrorCode.ReconnectRequired, outcome.Error!.Code);
        Assert.Equal(RetryDisposition.AfterReconnect, outcome.Error.Retry);
        Assert.Equal(4, stub.Requests.Count);
    }

    [Fact]
    public async Task An_expired_refresh_token_falls_back_to_the_app_password()
    {
        var store = new InMemoryCredentialStore();
        await store.CompareAndSetAsync("bluesky:bsky.social:ALERTS.EXAMPLE.ORG", 0, new StoredCredential(1, Jwt("old", Start.AddMinutes(-1)), Jwt("rold", Start.AddMinutes(-1)), Start.AddMinutes(-1), new Dictionary<string, string> { ["did"] = Did }), TestContext.Current.CancellationToken);
        var (channel, stub, _) = Create(store: store);
        stub.Respond(HttpStatusCode.BadRequest, """{"error":"ExpiredToken","message":"Token has expired"}""")
            .Respond(HttpStatusCode.OK, Session(Jwt("a1", Start.AddHours(2)), Jwt("r1", Start.AddDays(60))))
            .Respond(HttpStatusCode.OK, Created("p1"));

        var outcome = await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.True(outcome.Succeeded);
        Assert.Equal(["refreshSession", "createSession", "createRecord"], stub.Requests.Select(r => r.Request.RequestUri!.AbsolutePath.Split('.')[^1]));
    }

    [Fact]
    public async Task A_refused_app_password_is_reconnect_required_and_posts_nothing()
    {
        var (channel, stub, _) = Create();
        stub.Respond(HttpStatusCode.Unauthorized, """{"error":"AuthenticationRequired","message":"Invalid identifier or password"}""");

        var outcome = await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Failed, outcome.Status);
        Assert.Equal(HulakiErrorCode.ReconnectRequired, outcome.Error!.Code);
        Assert.Equal("AuthenticationRequired", outcome.Error.PlatformCode);
        Assert.DoesNotContain("Invalid identifier", outcome.Error.Message, StringComparison.Ordinal);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task Links_become_facets_with_utf8_byte_offsets()
    {
        var (channel, stub, _) = Create();
        stub.Respond(HttpStatusCode.OK, Session(Jwt("a1", Start.AddHours(2)), Jwt("r1", Start.AddDays(60)))).Respond(HttpStatusCode.OK, Created("p1"));
        var message = new Message("**म्याग्दी** 🌧 [DHM](https://dhm.gov.np/warn) and https://example.org/a_b.")
        {
            Format = TextFormat.Markup,
            Title = "वर्षा",
            Link = new Uri("https://example.org/watch/42"),
        };

        await channel.SendAsync(message, Recipient.Self, TestContext.Current.CancellationToken);

        var record = Record(stub.Requests[1]);
        var text = record.GetProperty("text").GetString()!;
        Assert.Equal("वर्षा\nम्याग्दी 🌧 DHM and https://example.org/a_b.\nhttps://example.org/watch/42", text);
        var bytes = Encoding.UTF8.GetBytes(text);
        var facets = record.GetProperty("facets").EnumerateArray().Select(f =>
        (
            Text: Encoding.UTF8.GetString(bytes[f.GetProperty("index").GetProperty("byteStart").GetInt32()..f.GetProperty("index").GetProperty("byteEnd").GetInt32()]),
            Uri: f.GetProperty("features")[0].GetProperty("uri").GetString(),
            Type: f.GetProperty("features")[0].GetProperty("$type").GetString()
        )).ToArray();
        Assert.Equal(
            [("DHM", "https://dhm.gov.np/warn"), ("https://example.org/a_b", "https://example.org/a_b"), ("https://example.org/watch/42", "https://example.org/watch/42")],
            facets.Select(f => (f.Text, f.Uri)));
        Assert.All(facets, f => Assert.Equal("app.bsky.richtext.facet#link", f.Type));
    }

    [Fact]
    public void The_limit_is_300_graphemes_with_conjuncts_counted_once()
    {
        var (channel, _, _) = Create();
        var conjuncts = string.Concat(Enumerable.Repeat("क्षि", 300));

        Assert.Empty(channel.Prepare(new Message(conjuncts), Recipient.Self));
        Assert.Contains(channel.Prepare(new Message(conjuncts + "क"), Recipient.Self), i => i.Code == "text-too-long");
        Assert.Contains(channel.Prepare(new Message("x"), new Recipient("someone.example.org")), i => i.Code == "recipient-must-be-self");
    }

    private static string LongText() =>
        string.Join(' ', Enumerable.Range(1, 90).Select(i => $"word{i:000}")) + " see https://example.org/end";

    [Fact]
    public async Task Long_text_becomes_a_thread_of_replies()
    {
        var (channel, stub, _) = Create(o => o.ThreadLongPosts = true);
        stub.Respond(HttpStatusCode.OK, Session(Jwt("a1", Start.AddHours(2)), Jwt("r1", Start.AddDays(60))))
            .Respond(HttpStatusCode.OK, Created("p1"))
            .Respond(HttpStatusCode.OK, Created("p2"))
            .Respond(HttpStatusCode.OK, Created("p3"));

        Assert.Empty(channel.Prepare(new Message(LongText()), Recipient.Self));
        var outcome = await channel.SendAsync(new Message(LongText()), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Delivered, outcome.Status);
        Assert.Equal($"at://{Did}/app.bsky.feed.post/p1", outcome.PlatformMessageId);
        var posts = stub.Requests.Skip(1).Select(Record).ToArray();
        Assert.Equal(3, posts.Length);
        Assert.False(posts[0].TryGetProperty("reply", out _));
        Assert.Equal($"at://{Did}/app.bsky.feed.post/p1", posts[1].GetProperty("reply").GetProperty("root").GetProperty("uri").GetString());
        Assert.Equal($"at://{Did}/app.bsky.feed.post/p1", posts[1].GetProperty("reply").GetProperty("parent").GetProperty("uri").GetString());
        Assert.Equal("bafyreitestp1", posts[2].GetProperty("reply").GetProperty("root").GetProperty("cid").GetString());
        Assert.Equal($"at://{Did}/app.bsky.feed.post/p2", posts[2].GetProperty("reply").GetProperty("parent").GetProperty("uri").GetString());
        Assert.Equal(LongText(), string.Join(' ', posts.Select(p => p.GetProperty("text").GetString())));
        Assert.All(posts, p => Assert.True(Hulaki.Text.TextCounter.Graphemes.Count(p.GetProperty("text").GetString()!) <= 300));
        var last = posts[^1];
        var lastText = last.GetProperty("text").GetString()!;
        var facet = last.GetProperty("facets")[0].GetProperty("index");
        Assert.Equal("https://example.org/end", Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(lastText)[facet.GetProperty("byteStart").GetInt32()..facet.GetProperty("byteEnd").GetInt32()]));
    }

    [Fact]
    public async Task Without_threading_long_text_is_refused()
    {
        var (channel, stub, _) = Create();

        var outcome = await channel.SendAsync(new Message(LongText()), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.NotSubmitted, outcome.Status);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task A_thread_that_fails_after_its_first_part_is_not_retried()
    {
        var (channel, stub, _) = Create(o =>
        {
            o.ThreadLongPosts = true;
            o.Retry = Hulaki.Channels.SendRetryPolicy.Default with { ResendUnknown = true };
        });
        stub.Respond(HttpStatusCode.OK, Session(Jwt("a1", Start.AddHours(2)), Jwt("r1", Start.AddDays(60))))
            .Respond(HttpStatusCode.OK, Created("p1"))
            .Throw(new HttpRequestException(HttpRequestError.ConnectionError, "refused"));

        var outcome = await channel.SendAsync(new Message(LongText()), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Failed, outcome.Status);
        Assert.Equal(RetryDisposition.ReconcileFirst, outcome.Error!.Retry);
        Assert.StartsWith("Posted 1 of 3 thread parts", outcome.Error.Message, StringComparison.Ordinal);
        Assert.Equal(1, outcome.Attempts);
        Assert.Equal(3, stub.Requests.Count);
    }

    [Fact]
    public async Task A_rate_limit_reads_ratelimit_reset()
    {
        var (channel, stub, _) = Create();
        stub.Respond(HttpStatusCode.OK, Session(Jwt("a1", Start.AddHours(2)), Jwt("r1", Start.AddDays(60))))
            .Respond(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("""{"error":"RateLimitExceeded","message":"Rate Limit Exceeded"}""", Encoding.UTF8, "application/json"),
                };
                response.Headers.Add("ratelimit-limit", "5000");
                response.Headers.Add("ratelimit-remaining", "0");
                response.Headers.Add("ratelimit-reset", Start.AddMinutes(30).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
                return response;
            });

        var outcome = await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(HulakiErrorCode.RateLimited, outcome.Error!.Code);
        Assert.Equal(TimeSpan.FromMinutes(30), outcome.Error.RetryAfter);
        Assert.Equal("RateLimitExceeded", outcome.Error.PlatformCode);
    }

    [Fact]
    public async Task An_invalid_record_is_invalid_input()
    {
        var (channel, stub, _) = Create();
        stub.Respond(HttpStatusCode.OK, Session(Jwt("a1", Start.AddHours(2)), Jwt("r1", Start.AddDays(60))))
            .Respond(HttpStatusCode.BadRequest, """{"error":"InvalidRequest","message":"Invalid app.bsky.feed.post record: Record/text must not be longer than 300 graphemes"}""");

        var outcome = await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(HulakiErrorCode.InvalidInput, outcome.Error!.Code);
        Assert.Equal("InvalidRequest", outcome.Error.PlatformCode);
    }

    [Fact]
    public async Task Html_502_from_create_record_is_unknown()
    {
        var (channel, stub, _) = Create();
        stub.Respond(HttpStatusCode.OK, Session(Jwt("a1", Start.AddHours(2)), Jwt("r1", Start.AddDays(60))))
            .Respond(HttpStatusCode.BadGateway, "<html>bad gateway</html>", "text/html");

        var outcome = await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Unknown, outcome.Status);
    }

    [Fact]
    public void Expiry_is_read_from_the_exp_claim()
    {
        Assert.Equal(Start.AddHours(2), BlueskyChannel.ExpiryOf(Jwt("a", Start.AddHours(2))));
        Assert.Null(BlueskyChannel.ExpiryOf("not-a-jwt"));
    }

    [Fact]
    public void Options_bind_and_a_registered_store_is_used()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["bsky:Identifier"] = "alerts.example.org",
            ["bsky:AppPassword"] = AppPassword,
            ["bsky:Languages:0"] = "ne",
            ["bsky:Languages:1"] = "en",
            ["bsky:ThreadLongPosts"] = "true",
        }).Build();
        var store = new InMemoryCredentialStore();
        var services = new ServiceCollection();
        services.AddSingleton<ICredentialStore>(store);
        services.AddHulaki().AddBluesky("bsky", configuration.GetSection("bsky"));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();
        Assert.IsType<BlueskyChannel>(provider.GetRequiredKeyedService<IChannel>("bsky"));
        var options = provider.GetRequiredService<IOptionsMonitor<BlueskyChannelOptions>>().Get("bsky");

        Assert.Equal(["ne", "en"], options.Languages);
        Assert.True(options.ThreadLongPosts);
        Assert.Same(store, options.CredentialStore);
    }

    [Fact]
    public void Validate_on_start_fails_on_a_missing_app_password()
    {
        var services = new ServiceCollection();
        services.AddHulaki().AddBluesky("bsky", o => o.Identifier = "alerts.example.org");
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("AppPassword is empty", ex.Message, StringComparison.Ordinal);
    }
}

/// <summary>
/// The kit scripts one response per request. A stored session makes a send one request, as it is
/// in steady state.
/// </summary>
public sealed class BlueskyContractTests : ChannelContractTests<BlueskyChannel>
{
    protected override Recipient ValidRecipient => Recipient.Self;

    protected override BlueskyChannel Create(HttpMessageHandler handler, TimeProvider time)
    {
        var store = new InMemoryCredentialStore();
        var session = new StoredCredential(1, "TEST-access", "TEST-refresh", time.GetUtcNow().AddDays(365), new Dictionary<string, string> { ["did"] = BlueskyChannelTests.Did });
        store.CompareAndSetAsync("bluesky:bsky.social:ALERTS.EXAMPLE.ORG", 0, session, default).AsTask().GetAwaiter().GetResult();
        return new BlueskyChannel("bsky", new HttpClient(handler), new BlueskyChannelOptions
        {
            Identifier = "alerts.example.org",
            AppPassword = BlueskyChannelTests.AppPassword,
            CredentialStore = store,
            TimeProvider = time,
        });
    }

    protected override HttpResponseMessage Success() =>
        new(HttpStatusCode.OK) { Content = new StringContent($$"""{"uri":"at://{{BlueskyChannelTests.Did}}/app.bsky.feed.post/p1","cid":"bafyreitestp1"}""", Encoding.UTF8, "application/json") };
}

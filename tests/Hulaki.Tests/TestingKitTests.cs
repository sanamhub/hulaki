using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hulaki.Testing;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Hulaki.Tests;

public sealed class FakeChannelTests
{
    [Fact]
    public async Task Records_every_send_and_delivers_by_default()
    {
        var channel = new FakeChannel("fake");
        var client = new HulakiClient([channel]);

        var result = await client.SendAsync(new Message("hi"), [new Target("fake", new Recipient("1")), new Target("fake", new Recipient("2"))], TestContext.Current.CancellationToken);

        Assert.Equal(SendStatus.Complete, result.Status);
        Assert.Equal(["1", "2"], channel.Sent.Select(s => s.Recipient.Address).Order());
        Assert.All(channel.Sent, s => Assert.Equal("hi", s.Message.Text));
    }

    [Fact]
    public async Task Returns_outcomes_from_the_function()
    {
        var blocked = DeliveryOutcome.Failed(new HulakiError(HulakiErrorCode.RecipientBlocked, RetryDisposition.Never, "blocked"));
        var channel = new FakeChannel("fake", respond: (_, r) => r.Address == "2" ? blocked : DeliveryOutcome.Delivered());

        var outcome = await channel.SendAsync(new Message("hi"), new Recipient("2"), TestContext.Current.CancellationToken);

        Assert.Same(blocked, outcome);
    }

    [Fact]
    public void Uses_the_given_manifest_and_prepares_nothing()
    {
        var channel = new FakeChannel("fake", ScriptedChannel.DefaultManifest);

        Assert.Same(ScriptedChannel.DefaultManifest, channel.Capabilities);
        Assert.Empty(channel.Prepare(new Message("hi"), Recipient.Self));
    }

    [Fact]
    public async Task Cancelled_send_throws_and_is_not_recorded()
    {
        var channel = new FakeChannel("fake");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => channel.SendAsync(new Message("hi"), Recipient.Self, new CancellationToken(canceled: true)));
        Assert.Empty(channel.Sent);
    }
}

public sealed class ScriptedHttpHandlerTests
{
    [Fact]
    public async Task Answers_in_order_and_records_bodies_and_times()
    {
        var time = new FakeTimeProvider();
        using var handler = new ScriptedHttpHandler(time);
        handler.Respond(HttpStatusCode.OK, "{}").Respond(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        using var http = new HttpClient(handler);
        var ct = TestContext.Current.CancellationToken;

        using var first = await http.PostAsync(new Uri("https://example.test/a"), new StringContent("one"), ct);
        time.Advance(TimeSpan.FromSeconds(2));
        using var second = await http.PostAsync(new Uri("https://example.test/b"), new StringContent("two"), ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(["one", "two"], handler.Requests.Select(r => r.Body));
        Assert.Equal(TimeSpan.FromSeconds(2), handler.Requests[1].ReceivedAt - handler.Requests[0].ReceivedAt);
    }

    [Fact]
    public async Task Queued_exception_is_thrown_and_the_request_still_recorded()
    {
        using var handler = new ScriptedHttpHandler();
        handler.Throw(new HttpRequestException(HttpRequestError.ConnectionError, "refused"));
        using var http = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(new Uri("https://example.test/"), TestContext.Current.CancellationToken));

        Assert.Equal(HttpRequestError.ConnectionError, ex.HttpRequestError);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Empty_script_throws()
    {
        using var handler = new ScriptedHttpHandler();
        using var http = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => http.GetAsync(new Uri("https://example.test/"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Throw_on_any_request_refuses_every_request()
    {
        using var handler = ScriptedHttpHandler.ThrowOnAnyRequest();
        using var http = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => http.GetAsync(new Uri("https://example.test/"), TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }
}

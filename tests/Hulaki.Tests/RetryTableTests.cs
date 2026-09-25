using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Hulaki.Channels;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Hulaki.Tests;

public sealed class RetryTableTests
{
    private static readonly Message Hello = new("hello");

    private static HulakiError RateLimited(int seconds) =>
        new(HulakiErrorCode.RateLimited, RetryDisposition.AfterDelay, "slow down") { RetryAfter = TimeSpan.FromSeconds(seconds) };

    [Fact]
    public async Task Rate_limited_then_delivered_waits_retry_after_and_counts_attempts()
    {
        var time = new FakeTimeProvider();
        var channel = new ScriptedChannel(
            new TestOptions { TimeProvider = time },
            null,
            () => DeliveryOutcome.Failed(RateLimited(5)),
            () => DeliveryOutcome.Delivered("42"));
        var started = time.GetUtcNow();

        var outcome = await Time.RunAsync(time, () => channel.SendAsync(Hello, new Recipient("1"), TestContext.Current.CancellationToken));

        Assert.Equal(DeliveryStatus.Delivered, outcome.Status);
        Assert.Equal(2, outcome.Attempts);
        Assert.True(time.GetUtcNow() - started >= TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Retry_after_over_the_cap_returns_the_failure_without_waiting()
    {
        var time = new FakeTimeProvider();
        var channel = new ScriptedChannel(
            new TestOptions { TimeProvider = time },
            null,
            () => DeliveryOutcome.Failed(RateLimited(600)));

        var started = time.GetUtcNow();

        var outcome = await channel.SendAsync(Hello, new Recipient("1"), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Failed, outcome.Status);
        Assert.Equal(HulakiErrorCode.RateLimited, outcome.Error!.Code);
        Assert.Equal(1, outcome.Attempts);
        Assert.Equal(started, time.GetUtcNow());
    }

    [Fact]
    public async Task Rate_limit_on_one_send_pauses_a_concurrent_send_on_the_same_channel()
    {
        var time = new FakeTimeProvider();
        var channel = new ScriptedChannel(
            new TestOptions { TimeProvider = time },
            null,
            () => DeliveryOutcome.Failed(RateLimited(5)),
            () => DeliveryOutcome.Delivered(),
            () => DeliveryOutcome.Delivered());
        var started = time.GetUtcNow();
        var ct = TestContext.Current.CancellationToken;

        // The first attempt runs synchronously and sets the pause before the second send starts.
        var first = channel.SendAsync(Hello, new Recipient("1"), ct);
        var second = channel.SendAsync(Hello, new Recipient("2"), ct);
        var outcomes = await Time.RunAsync(time, () => Task.WhenAll(first, second));

        Assert.All(outcomes, o => Assert.Equal(DeliveryStatus.Delivered, o.Status));
        Assert.Equal(1, outcomes[1].Attempts);
        var times = channel.CallTimes;
        Assert.Equal(3, times.Count);
        Assert.All(times.Skip(1), t => Assert.True(t - started >= TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Unknown_is_not_resent_by_default()
    {
        var channel = new ScriptedChannel(
            new TestOptions(),
            null,
            () => DeliveryOutcome.Unknown(new HulakiError(HulakiErrorCode.AmbiguousOutcome, RetryDisposition.ReconcileFirst, "lost")));

        var outcome = await channel.SendAsync(Hello, new Recipient("1"), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Unknown, outcome.Status);
        Assert.Equal(1, channel.Calls);
    }

    [Fact]
    public async Task Unknown_is_resent_when_the_caller_prefers_duplicates_over_losses()
    {
        var time = new FakeTimeProvider();
        var channel = new ScriptedChannel(
            new TestOptions { TimeProvider = time, Retry = SendRetryPolicy.Default with { ResendUnknown = true } },
            null,
            () => DeliveryOutcome.Unknown(new HulakiError(HulakiErrorCode.AmbiguousOutcome, RetryDisposition.ReconcileFirst, "lost")),
            () => DeliveryOutcome.Delivered());

        var outcome = await Time.RunAsync(time, () => channel.SendAsync(Hello, new Recipient("1"), TestContext.Current.CancellationToken));

        Assert.Equal(DeliveryStatus.Delivered, outcome.Status);
        Assert.Equal(2, channel.Calls);
    }

    [Theory]
    [InlineData(HttpRequestError.ConnectionError)]
    [InlineData(HttpRequestError.NameResolutionError)]
    [InlineData(HttpRequestError.SecureConnectionError)]
    public async Task Errors_before_dispatch_are_retried_because_nothing_was_sent(HttpRequestError error)
    {
        var time = new FakeTimeProvider();
        var channel = new ScriptedChannel(
            new TestOptions { TimeProvider = time },
            null,
            () => throw new HttpRequestException(error, "not sent"),
            () => DeliveryOutcome.Delivered());

        var outcome = await Time.RunAsync(time, () => channel.SendAsync(Hello, new Recipient("1"), TestContext.Current.CancellationToken));

        Assert.Equal(DeliveryStatus.Delivered, outcome.Status);
        Assert.Equal(2, outcome.Attempts);
    }

    [Fact]
    public async Task Response_lost_after_send_is_unknown_and_not_retried()
    {
        var channel = new ScriptedChannel(
            new TestOptions(),
            null,
            () => throw new HttpRequestException(HttpRequestError.ResponseEnded, "reset"));

        var outcome = await channel.SendAsync(Hello, new Recipient("1"), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Unknown, outcome.Status);
        Assert.Equal(HulakiErrorCode.AmbiguousOutcome, outcome.Error!.Code);
        Assert.Equal(1, channel.Calls);
    }

    [Fact]
    public async Task Response_lost_on_an_idempotent_platform_is_retried()
    {
        var time = new FakeTimeProvider();
        var manifest = new CapabilityManifest(
            "idem",
            ScriptedChannel.DefaultManifest.TextLimit,
            [new(Capability.Text, Availability.Available), new(Capability.IdempotentSend, Availability.Available)]);
        var channel = new ScriptedChannel(
            new TestOptions { TimeProvider = time },
            manifest,
            () => throw new HttpRequestException(HttpRequestError.ResponseEnded, "reset"),
            () => DeliveryOutcome.Delivered());

        var outcome = await Time.RunAsync(time, () => channel.SendAsync(Hello, new Recipient("1"), TestContext.Current.CancellationToken));

        Assert.Equal(DeliveryStatus.Delivered, outcome.Status);
    }

    [Fact]
    public async Task Timeout_that_is_not_the_callers_cancel_is_unknown()
    {
        var channel = new ScriptedChannel(new TestOptions(), null, () => throw new TaskCanceledException("HttpClient.Timeout"));

        var outcome = await channel.SendAsync(Hello, new Recipient("1"), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Unknown, outcome.Status);
        Assert.Equal(HulakiErrorCode.Timeout, outcome.Error!.Code);
    }

    [Fact]
    public async Task Never_disposition_is_not_retried()
    {
        var channel = new ScriptedChannel(
            new TestOptions(),
            null,
            () => DeliveryOutcome.Failed(new HulakiError(HulakiErrorCode.RecipientBlocked, RetryDisposition.Never, "blocked")));

        var outcome = await channel.SendAsync(Hello, new Recipient("1"), TestContext.Current.CancellationToken);

        Assert.Equal(1, channel.Calls);
        Assert.Equal(HulakiErrorCode.RecipientBlocked, outcome.Error!.Code);
    }

    [Fact]
    public async Task Too_long_text_is_not_submitted_and_makes_no_call()
    {
        var channel = new ScriptedChannel(new TestOptions(), null);

        var outcome = await channel.SendAsync(new Message(new string('x', 21)), new Recipient("1"), TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.NotSubmitted, outcome.Status);
        Assert.Equal("text-too-long", Assert.Single(outcome.Issues).Code);
        Assert.Equal(0, channel.Calls);
    }

    [Fact]
    public async Task Truncate_overflow_cuts_to_the_limit_before_sending()
    {
        var channel = new ScriptedChannel(new TestOptions(), null, () => DeliveryOutcome.Delivered());
        var message = new Message("the quick brown fox jumps over") { Overflow = OverflowBehavior.Truncate };

        var outcome = await channel.SendAsync(message, new Recipient("1"), TestContext.Current.CancellationToken);

        Assert.True(outcome.Succeeded);
        Assert.Equal("the quick brown fox…", channel.LastMessage!.Text);
    }

    [Fact]
    public async Task Caller_cancelling_during_a_backoff_throws_and_makes_no_further_attempt()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var channel = new ScriptedChannel(
            new TestOptions { TimeProvider = new FakeTimeProvider() },
            null,
            () => DeliveryOutcome.Failed(new HulakiError(HulakiErrorCode.UpstreamFailure, RetryDisposition.AfterDelay, "busy")),
            () => DeliveryOutcome.Delivered());

        // The fake clock never advances, so the send is parked in its backoff delay when cancelled.
        var send = channel.SendAsync(Hello, new Recipient("1"), cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        Assert.Equal(1, channel.Calls);
    }

    [Fact]
    public async Task Backoff_with_half_jitter_waits_three_quarters_then_one_and_a_half_seconds()
    {
        var time = new FakeTimeProvider();
        var busy = new HulakiError(HulakiErrorCode.UpstreamFailure, RetryDisposition.AfterDelay, "busy");
        var channel = new ScriptedChannel(
            new TestOptions { TimeProvider = time },
            null,
            () => DeliveryOutcome.Failed(busy),
            () => DeliveryOutcome.Failed(busy),
            () => DeliveryOutcome.Delivered());

        var outcome = await Time.RunAsync(
            time,
            () => channel.SendAsync(Hello, new Recipient("1"), TestContext.Current.CancellationToken),
            TimeSpan.FromMilliseconds(250));

        var times = channel.CallTimes;
        Assert.True(outcome.Succeeded);
        Assert.Equal(TimeSpan.FromMilliseconds(750), times[1] - times[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), times[2] - times[1]);
    }

    [Fact]
    public async Task Three_retryable_failures_end_as_failed_after_three_attempts()
    {
        var time = new FakeTimeProvider();
        var busy = new HulakiError(HulakiErrorCode.UpstreamFailure, RetryDisposition.AfterDelay, "busy");
        var channel = new ScriptedChannel(
            new TestOptions { TimeProvider = time },
            null,
            () => DeliveryOutcome.Failed(busy),
            () => DeliveryOutcome.Failed(busy),
            () => DeliveryOutcome.Failed(busy));

        var outcome = await Time.RunAsync(time, () => channel.SendAsync(Hello, new Recipient("1"), TestContext.Current.CancellationToken));

        Assert.Equal(DeliveryStatus.Failed, outcome.Status);
        Assert.Equal(3, outcome.Attempts);
        Assert.Equal(3, channel.Calls);
    }

    [Fact]
    public async Task Title_on_a_channel_without_titles_is_sent_and_keeps_the_warning()
    {
        var channel = new ScriptedChannel(new TestOptions(), null, () => DeliveryOutcome.Delivered());

        var outcome = await channel.SendAsync(Hello with { Title = "Alert" }, new Recipient("1"), TestContext.Current.CancellationToken);

        Assert.True(outcome.Succeeded);
        var warning = Assert.Single(outcome.Issues);
        Assert.Equal("title-inlined", warning.Code);
        Assert.False(warning.IsError);
        Assert.Equal(1, outcome.Attempts);
    }

    [Fact]
    public async Task Full_rate_limiter_queue_fails_without_an_attempt()
    {
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = 1, QueueLimit = 0 });
        using var held = limiter.AttemptAcquire();
        var channel = new ScriptedChannel(new TestOptions { RateLimiter = limiter }, null, () => DeliveryOutcome.Delivered());

        var outcome = await channel.SendAsync(Hello, new Recipient("1"), TestContext.Current.CancellationToken);

        Assert.True(held.IsAcquired);
        Assert.Equal(DeliveryStatus.Failed, outcome.Status);
        Assert.Equal(HulakiErrorCode.RateLimited, outcome.Error!.Code);
        Assert.Equal(0, outcome.Attempts);
        Assert.Equal(0, channel.Calls);
    }
}

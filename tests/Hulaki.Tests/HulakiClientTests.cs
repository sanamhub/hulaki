using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hulaki.Idempotency;
using Xunit;

namespace Hulaki.Tests;

public sealed class HulakiClientTests
{
    [Fact]
    public async Task Idempotent_repeat_replays_success_and_resends_retryable_failures()
    {
        var a = new ScriptedChannel(new TestOptions(), null, () => DeliveryOutcome.Delivered("a1"));
        var b = new ScriptedChannelNamed("b", () => DeliveryOutcome.Failed(new HulakiError(HulakiErrorCode.UpstreamFailure, RetryDisposition.AfterDelay, "x")), () => DeliveryOutcome.Delivered("b1"));
        var client = new HulakiClient([a, b]);
        var message = new Message("hi") { IdempotencyKey = "k1" };
        Target[] targets = [new("scripted", new Recipient("1")), new("b", new Recipient("2"))];

        var first = await client.SendAsync(message, targets, TestContext.Current.CancellationToken);
        var second = await client.SendAsync(message, targets, TestContext.Current.CancellationToken);

        Assert.Equal(SendStatus.Partial, first.Status);
        Assert.Equal(SendStatus.Complete, second.Status);
        Assert.True(second.Outcomes[0].Outcome.IsReplay);
        Assert.False(second.Outcomes[1].Outcome.IsReplay);
        Assert.Equal(1, a.Calls);
    }

    [Fact]
    public async Task Same_key_with_changed_content_is_a_conflict()
    {
        var a = new ScriptedChannel(new TestOptions(), null, () => DeliveryOutcome.Delivered());
        var client = new HulakiClient([a]);
        Target[] targets = [new("scripted", new Recipient("1"))];

        await client.SendAsync(new Message("v1") { IdempotencyKey = "k" }, targets, TestContext.Current.CancellationToken);
        var second = await client.SendAsync(new Message("v2") { IdempotencyKey = "k" }, targets, TestContext.Current.CancellationToken);

        Assert.Equal(HulakiErrorCode.IdempotencyConflict, second.Outcomes[0].Outcome.Error!.Code);
        Assert.Equal(1, a.Calls);
    }

    [Fact]
    public void Fingerprint_ignores_the_key_and_sees_the_title()
    {
        var m = new Message("x") { IdempotencyKey = "a" };

        Assert.Equal(MessageFingerprint.Compute(m), MessageFingerprint.Compute(m with { IdempotencyKey = "b" }));
        Assert.NotEqual(MessageFingerprint.Compute(m), MessageFingerprint.Compute(m with { Title = "t" }));
    }

    [Fact]
    public void Recipient_to_string_never_shows_the_address()
    {
        Assert.Equal("Recipient(***)", new Recipient("+9779800000000").ToString());
    }

    [Fact]
    public async Task Outcomes_are_in_input_order_when_sends_finish_out_of_order()
    {
        var slowGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new GatedChannel("slow", slowGate.Task, "slow-1");
        var fast = new GatedChannel("fast", Task.CompletedTask, "fast-1", onSent: slowGate.SetResult);
        var client = new HulakiClient([slow, fast]);
        Target[] targets = [new("slow", new Recipient("1")), new("fast", new Recipient("2"))];

        var result = await client.SendAsync(new Message("hi"), targets, TestContext.Current.CancellationToken);

        Assert.True(fast.CompletedAt < slow.CompletedAt);
        Assert.Same(targets[0], result.Outcomes[0].Target);
        Assert.Equal("slow-1", result.Outcomes[0].Outcome.PlatformMessageId);
        Assert.Same(targets[1], result.Outcomes[1].Target);
        Assert.Equal("fast-1", result.Outcomes[1].Outcome.PlatformMessageId);
    }

    [Fact]
    public async Task A_channel_that_throws_fails_only_its_own_target()
    {
        var good = new ScriptedChannel(new TestOptions(), null, () => DeliveryOutcome.Delivered("ok"));
        var client = new HulakiClient([good, new ThrowingChannel("broken")]);
        Target[] targets = [new("broken", new Recipient("1")), new("scripted", new Recipient("2"))];

        var result = await client.SendAsync(new Message("hi"), targets, TestContext.Current.CancellationToken);

        Assert.Equal(SendStatus.Partial, result.Status);
        var failed = result.Outcomes[0].Outcome;
        Assert.Equal(DeliveryStatus.Failed, failed.Status);
        Assert.Equal(HulakiErrorCode.UpstreamFailure, failed.Error!.Code);
        Assert.Equal(RetryDisposition.Never, failed.Error.Retry);
        Assert.Equal("Channel threw InvalidOperationException.", failed.Error.Message);
        Assert.True(result.Outcomes[1].Outcome.Succeeded);
    }

    [Fact]
    public async Task Max_concurrency_one_never_runs_two_sends_at_once()
    {
        var channel = new CountingChannel("counted");
        var client = new HulakiClient([channel], new HulakiClientOptions { MaxConcurrency = 1 });
        var targets = Enumerable.Range(0, 8).Select(i => new Target("counted", new Recipient(i.ToString(System.Globalization.CultureInfo.InvariantCulture)))).ToArray();

        var result = await client.SendAsync(new Message("hi"), targets, TestContext.Current.CancellationToken);

        Assert.Equal(SendStatus.Complete, result.Status);
        Assert.Equal(8, channel.Sends);
        Assert.Equal(1, channel.MaxInFlight);
    }

    [Fact]
    public async Task Unknown_channel_name_throws()
    {
        var client = new HulakiClient([new ScriptedChannel(new TestOptions(), null)]);

        await Assert.ThrowsAsync<ArgumentException>(() => client.SendAsync(new Message("hi"), [new Target("missing", new Recipient("1"))], TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Duplicate_channel_names_throw()
    {
        Assert.Throws<ArgumentException>(() => new HulakiClient([new ThrowingChannel("x"), new ThrowingChannel("x")]));
    }

    private abstract class TestChannel(string name) : IChannel
    {
        public string Name => name;

        public CapabilityManifest Capabilities => ScriptedChannel.DefaultManifest;

        public IReadOnlyList<PreparationIssue> Prepare(Message message, Recipient recipient) => [];

        public abstract Task<DeliveryOutcome> SendAsync(Message message, Recipient recipient, CancellationToken cancellationToken = default);
    }

    private sealed class GatedChannel(string name, Task gate, string id, Action? onSent = null) : TestChannel(name)
    {
        private static long _order;

        public long CompletedAt { get; private set; }

        public override async Task<DeliveryOutcome> SendAsync(Message message, Recipient recipient, CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            CompletedAt = Interlocked.Increment(ref _order);
            onSent?.Invoke();
            return DeliveryOutcome.Delivered(id);
        }
    }

    private sealed class ThrowingChannel(string name) : TestChannel(name)
    {
        public override Task<DeliveryOutcome> SendAsync(Message message, Recipient recipient, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("provider bug");
    }

    private sealed class CountingChannel(string name) : TestChannel(name)
    {
        private int _inFlight;
        private int _maxInFlight;
        private int _sends;

        public int MaxInFlight => Volatile.Read(ref _maxInFlight);

        public int Sends => Volatile.Read(ref _sends);

        public override async Task<DeliveryOutcome> SendAsync(Message message, Recipient recipient, CancellationToken cancellationToken = default)
        {
            var now = Interlocked.Increment(ref _inFlight);
            int seen;
            while (now > (seen = Volatile.Read(ref _maxInFlight)) && Interlocked.CompareExchange(ref _maxInFlight, now, seen) != seen)
            {
            }

            await Task.Delay(5, cancellationToken);
            Interlocked.Decrement(ref _inFlight);
            Interlocked.Increment(ref _sends);
            return DeliveryOutcome.Delivered();
        }
    }

    private sealed class ScriptedChannelNamed(string name, params Func<DeliveryOutcome>[] script) : IChannel
    {
        private readonly Queue<Func<DeliveryOutcome>> _script = new(script);

        public string Name => name;

        public CapabilityManifest Capabilities => ScriptedChannel.DefaultManifest;

        public IReadOnlyList<PreparationIssue> Prepare(Message message, Recipient recipient) => [];

        public Task<DeliveryOutcome> SendAsync(Message message, Recipient recipient, CancellationToken cancellationToken = default) =>
            Task.FromResult(_script.Dequeue()());
    }
}

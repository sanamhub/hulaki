using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hulaki.Channels;
using Hulaki.Text;
using Microsoft.Extensions.Time.Testing;

namespace Hulaki.Tests;

internal sealed class TestOptions : ChannelOptions;

/// <summary>A channel whose attempts return scripted results, for the retry table.</summary>
internal sealed class ScriptedChannel : ChannelBase
{
    private readonly Queue<Func<DeliveryOutcome>> _script;
    private readonly Lock _gate = new();
    private readonly List<DateTimeOffset> _callTimes = [];

    public ScriptedChannel(ChannelOptions options, CapabilityManifest? manifest = null, params Func<DeliveryOutcome>[] script)
        : base("scripted", manifest ?? DefaultManifest, options, defaultLimiter: null, jitter: () => 0.5)
    {
        _script = new Queue<Func<DeliveryOutcome>>(script);
    }

    public static CapabilityManifest DefaultManifest { get; } = new(
        "scripted",
        new TextLimit(20, TextCounter.Graphemes),
        [new(Capability.Text, Availability.Available)]);

    public int Calls
    {
        get
        {
            lock (_gate)
            {
                return _callTimes.Count;
            }
        }
    }

    /// <summary>The message given to the last attempt, after truncation.</summary>
    public Message? LastMessage { get; private set; }

    /// <summary>When each attempt started, on the channel's clock.</summary>
    public IReadOnlyList<DateTimeOffset> CallTimes
    {
        get
        {
            lock (_gate)
            {
                return [.. _callTimes];
            }
        }
    }

    protected override Task<DeliveryOutcome> SendOnceAsync(Message message, Recipient recipient, CancellationToken cancellationToken)
    {
        Func<DeliveryOutcome> next;
        lock (_gate)
        {
            _callTimes.Add(Options.TimeProvider.GetUtcNow());
            LastMessage = message;
            next = _script.Dequeue();
        }

        return Task.FromResult(next());
    }
}

/// <summary>Returns queued responses and records requests with their bodies.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    public StubHandler Respond(HttpStatusCode status, string json)
    {
        _responses.Enqueue(_ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        return this;
    }

    public StubHandler Throw(Exception exception)
    {
        _responses.Enqueue(_ => throw exception);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Requests.Add((request, body));
        return _responses.TryDequeue(out var next) ? next(request) : throw new InvalidOperationException("No stub response left.");
    }
}

internal static class Time
{
    /// <summary>Runs <paramref name="start"/> while advancing <paramref name="time"/> until it completes, so Task.Delay on the fake clock fires.</summary>
    public static async Task<T> RunAsync<T>(FakeTimeProvider time, Func<Task<T>> start, TimeSpan? step = null)
    {
        var task = start();
        for (var i = 0; i < 10_000 && !task.IsCompleted; i++)
        {
            await Task.Yield();
            time.Advance(step ?? TimeSpan.FromMilliseconds(100));
        }

        return await task;
    }
}

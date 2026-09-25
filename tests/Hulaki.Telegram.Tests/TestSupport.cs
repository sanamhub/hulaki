using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;

namespace Hulaki.Telegram.Tests;

/// <summary>Returns queued responses and records requests with their bodies and arrival times.</summary>
internal sealed class StubHandler(TimeProvider? clock = null) : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
    private readonly ConcurrentQueue<(HttpRequestMessage Request, string Body, DateTimeOffset At)> _requests = new();

    public IReadOnlyList<(HttpRequestMessage Request, string Body)> Requests => [.. _requests.Select(r => (r.Request, r.Body))];

    public IReadOnlyList<DateTimeOffset> RequestTimes => [.. _requests.Select(r => r.At)];

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
        _requests.Enqueue((request, body, (clock ?? TimeProvider.System).GetUtcNow()));
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

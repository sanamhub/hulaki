using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace Hulaki.Telegram;

/// <summary>
/// A token bucket of one token that refills once per interval, with a bounded queue: the shape of
/// Telegram's "1 message per second per chat" (core.telegram.org/bots/faq). It reads time from a
/// <see cref="TimeProvider"/>. The built-in <see cref="TokenBucketRateLimiter"/> reads the system
/// clock, so its waits cannot be tested with a fake clock.
/// </summary>
internal sealed class ChatRateLimiter : RateLimiter
{
    private static readonly RateLimitLease Granted = new Lease(isAcquired: true);
    private static readonly RateLimitLease Refused = new Lease(isAcquired: false);

    private readonly TimeProvider _time;
    private readonly TimeSpan _interval;
    private readonly int _queueLimit;
    private readonly Lock _gate = new();
    private DateTimeOffset _nextFree;
    private int _queued;
    private long _failed;
    private long _succeeded;

    public ChatRateLimiter(TimeProvider time, TimeSpan interval, int queueLimit)
    {
        _time = time;
        _interval = interval;
        _queueLimit = queueLimit;
        _nextFree = DateTimeOffset.MinValue;
    }

    /// <summary>
    /// Partitions by chat so each chat has its own bucket. The partition key is the recipient
    /// address, which stays inside this process (ADR-0011).
    /// </summary>
    public static PartitionedRateLimiter<string> PerChat(TimeProvider time) =>
        PartitionedRateLimiter.Create<string, string>(
            chat => RateLimitPartition.Get(chat, _ => new ChatRateLimiter(time, TimeSpan.FromSeconds(1), queueLimit: 20)),
            StringComparer.Ordinal);

    public override TimeSpan? IdleDuration
    {
        get
        {
            lock (_gate)
            {
                var now = _time.GetUtcNow();
                return _queued == 0 && now >= _nextFree ? now - _nextFree : null;
            }
        }
    }

    public override RateLimiterStatistics? GetStatistics()
    {
        lock (_gate)
        {
            return new RateLimiterStatistics
            {
                CurrentAvailablePermits = _time.GetUtcNow() >= _nextFree ? 1 : 0,
                CurrentQueuedCount = _queued,
                TotalFailedLeases = Interlocked.Read(ref _failed),
                TotalSuccessfulLeases = Interlocked.Read(ref _succeeded),
            };
        }
    }

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(permitCount, 1);
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (permitCount == 0 || now >= _nextFree)
            {
                if (permitCount == 1)
                {
                    _nextFree = now + _interval;
                }

                Interlocked.Increment(ref _succeeded);
                return Granted;
            }
        }

        Interlocked.Increment(ref _failed);
        return Refused;
    }

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(permitCount, 1);
        TimeSpan wait;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            var slot = now >= _nextFree ? now : _nextFree;
            wait = slot - now;
            if (wait > TimeSpan.Zero && _queued >= _queueLimit)
            {
                Interlocked.Increment(ref _failed);
                return Refused;
            }

            if (permitCount == 1)
            {
                _nextFree = slot + _interval;
            }

            _queued += wait > TimeSpan.Zero ? 1 : 0;
        }

        // A cancelled wait keeps its slot, so the next send to this chat waits up to one interval longer.
        if (wait > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(wait, _time, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                lock (_gate)
                {
                    _queued--;
                }
            }
        }

        Interlocked.Increment(ref _succeeded);
        return Granted;
    }

    private sealed class Lease(bool isAcquired) : RateLimitLease
    {
        public override bool IsAcquired { get; } = isAcquired;

        public override IEnumerable<string> MetadataNames => [];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
    }
}

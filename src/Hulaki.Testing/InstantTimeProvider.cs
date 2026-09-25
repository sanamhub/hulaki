using System;
using System.Threading;

namespace Hulaki.Testing;

/// <summary>
/// A clock where every wait ends at once and the time jumps forward by the wait, so retry backoff
/// and 429 pauses run in microseconds with their arithmetic intact. The contract kit uses it so it
/// needs no fake-clock package.
/// </summary>
internal sealed class InstantTimeProvider : TimeProvider
{
    private long _offsetTicks;

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (dueTime == Timeout.InfiniteTimeSpan)
        {
            return base.CreateTimer(callback, state, dueTime, period);
        }

        if (dueTime > TimeSpan.Zero)
        {
            Interlocked.Add(ref _offsetTicks, dueTime.Ticks);
        }

        return base.CreateTimer(callback, state, TimeSpan.Zero, period);
    }
}

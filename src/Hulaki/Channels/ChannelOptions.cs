using System;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;

namespace Hulaki.Channels;

/// <summary>Retry rules for sends (ADR-0005). Immutable.</summary>
public sealed record SendRetryPolicy
{
    /// <summary>Three attempts, 1 s base backoff, 30 s cap, Retry-After honoured up to 2 minutes, unknown outcomes not resent.</summary>
    public static SendRetryPolicy Default { get; } = new();

    /// <summary>One attempt, no retries.</summary>
    public static SendRetryPolicy None { get; } = new() { MaxAttempts = 1 };

    /// <summary>Total attempts including the first. At least 1.</summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>Backoff before the second attempt; doubles each time.</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Largest backoff between attempts.</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>A platform <c>Retry-After</c> longer than this ends the retries and returns the failure to the caller.</summary>
    public TimeSpan MaxRetryAfter { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Resend after an <see cref="DeliveryStatus.Unknown"/> outcome even though the platform does not
    /// deduplicate. Trades a possible duplicate for a lower chance of a lost message. Off by default.
    /// </summary>
    public bool ResendUnknown { get; init; }
}

/// <summary>Settings every channel shares. Provider options derive from it.</summary>
public abstract class ChannelOptions
{
    /// <summary>Retry rules. Defaults to <see cref="SendRetryPolicy.Default"/>.</summary>
    public SendRetryPolicy Retry { get; set; } = SendRetryPolicy.Default;

    /// <summary>
    /// Local rate limiter applied before each attempt. Null means the provider default, which
    /// encodes the platform's published limit. Set <see cref="DisableRateLimiting"/> to turn it off.
    /// </summary>
    public RateLimiter? RateLimiter { get; set; }

    /// <summary>Turns off local rate limiting. The platform still enforces its own.</summary>
    public bool DisableRateLimiting { get; set; }

    /// <summary>Clock for backoff and pauses. Tests pass a fake.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>
    /// Creates the channel's logger. Null means no logging. Hulaki logs channel names, platform
    /// ids, error codes and counts, never content, addresses or tokens (ADR-0012).
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; set; }
}

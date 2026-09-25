using System;
using Hulaki.Channels;

namespace Hulaki.Providers;

/// <summary>
/// The configuration-bindable settings every provider shares. A provider's options class also
/// holds a <see cref="System.Threading.RateLimiting.RateLimiter"/> and a <see cref="TimeProvider"/>,
/// which configuration cannot express and the binding generator rejects (SYSLIB1100), so each
/// provider binds its section to a settings type derived from this one and copies the values
/// across. Null means "keep the default".
/// </summary>
/// <remarks>
/// Compiled into every provider assembly from <c>src/Shared</c>, so the providers share the code
/// without a public type in the core.
/// </remarks>
internal abstract class ChannelSettings
{
    public bool? DisableRateLimiting { get; set; }

    public RetrySettings? Retry { get; set; }

    protected void ApplyShared(ChannelOptions options)
    {
        options.DisableRateLimiting = DisableRateLimiting ?? options.DisableRateLimiting;
        if (Retry is { } retry)
        {
            options.Retry = retry.ApplyTo(options.Retry);
        }
    }
}

/// <summary>Bindable form of <see cref="SendRetryPolicy"/>. Null keeps the current value.</summary>
internal sealed class RetrySettings
{
    public int? MaxAttempts { get; set; }

    public TimeSpan? BaseDelay { get; set; }

    public TimeSpan? MaxDelay { get; set; }

    public TimeSpan? MaxRetryAfter { get; set; }

    public bool? ResendUnknown { get; set; }

    public SendRetryPolicy ApplyTo(SendRetryPolicy policy) => policy with
    {
        MaxAttempts = MaxAttempts ?? policy.MaxAttempts,
        BaseDelay = BaseDelay ?? policy.BaseDelay,
        MaxDelay = MaxDelay ?? policy.MaxDelay,
        MaxRetryAfter = MaxRetryAfter ?? policy.MaxRetryAfter,
        ResendUnknown = ResendUnknown ?? policy.ResendUnknown,
    };
}

/// <summary>Address checks shared by the provider validators.</summary>
internal static class Endpoints
{
    /// <summary>HTTPS, or HTTP on loopback for a local server (ADR-0015).</summary>
    public static bool IsHttpsOrLoopback(Uri? address) =>
        address is { IsAbsoluteUri: true }
        && (address.Scheme == Uri.UriSchemeHttps || (address.Scheme == Uri.UriSchemeHttp && address.IsLoopback));
}

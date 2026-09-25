using System;
using System.Net;
using System.Net.Http;

namespace Hulaki.Channels;

/// <summary>Helpers for providers that talk HTTP. Shared so every provider reads headers the same way.</summary>
public static class HttpHelpers
{
    /// <summary>
    /// Reads <c>Retry-After</c> as seconds or an HTTP date. Returns null when absent, unparsable
    /// or in the past.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <param name="timeProvider">Clock for the HTTP-date form.</param>
    /// <returns>The wait, or null.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static TimeSpan? RetryAfter(HttpResponseMessage response, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(timeProvider);
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta > TimeSpan.Zero ? delta : null;
        }

        if (header?.Date is { } date)
        {
            var wait = date - timeProvider.GetUtcNow();
            return wait > TimeSpan.Zero ? wait : null;
        }

        return null;
    }

    /// <summary>
    /// Default mapping from an HTTP status to an error, for providers whose error bodies say
    /// nothing more useful. A 5xx is <see cref="RetryDisposition.ReconcileFirst"/> except 503,
    /// which servers use for "not processed, come back later".
    /// </summary>
    /// <param name="status">The status.</param>
    /// <param name="retryAfter">Parsed <c>Retry-After</c>, if any.</param>
    /// <param name="message">Redacted description.</param>
    /// <returns>The error.</returns>
    public static HulakiError ErrorFor(HttpStatusCode status, TimeSpan? retryAfter, string message)
    {
        var code = (int)status;
        var (errorCode, retry) = code switch
        {
            400 or 413 or 415 or 422 => (HulakiErrorCode.InvalidInput, RetryDisposition.Never),
            401 => (HulakiErrorCode.Unauthorized, RetryDisposition.AfterReconnect),
            402 => (HulakiErrorCode.BillingRequired, RetryDisposition.Never),
            403 => (HulakiErrorCode.PermissionDenied, RetryDisposition.Never),
            404 or 410 => (HulakiErrorCode.RecipientNotFound, RetryDisposition.Never),
            408 => (HulakiErrorCode.Timeout, RetryDisposition.AfterDelay),
            429 => (HulakiErrorCode.RateLimited, RetryDisposition.AfterDelay),
            503 => (HulakiErrorCode.UpstreamFailure, RetryDisposition.AfterDelay),
            >= 500 => (HulakiErrorCode.UpstreamFailure, RetryDisposition.ReconcileFirst),
            _ => (HulakiErrorCode.UpstreamFailure, RetryDisposition.Never),
        };
        return new HulakiError(errorCode, retry, message) { HttpStatus = code, RetryAfter = retryAfter };
    }

    /// <summary>
    /// Turns an error into an outcome: <see cref="DeliveryStatus.Unknown"/> when the platform may have
    /// acted (<see cref="RetryDisposition.ReconcileFirst"/>), otherwise <see cref="DeliveryStatus.Failed"/>.
    /// </summary>
    /// <param name="error">The error.</param>
    /// <returns>The outcome.</returns>
    public static DeliveryOutcome OutcomeFor(HulakiError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Retry == RetryDisposition.ReconcileFirst ? DeliveryOutcome.Unknown(error) : DeliveryOutcome.Failed(error);
    }
}

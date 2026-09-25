using System;
using System.Collections.Generic;
using System.Linq;

namespace Hulaki;

/// <summary>What happened to one target (ADR-0005).</summary>
public enum DeliveryStatus
{
    /// <summary>Not sent. <see cref="DeliveryOutcome.Issues"/> or <see cref="DeliveryOutcome.Error"/> says why. Safe to fix and resend.</summary>
    NotSubmitted = 0,

    /// <summary>The platform accepted the message and will deliver it later (a queue, a push service).</summary>
    Accepted = 1,

    /// <summary>The platform confirmed delivery or publication.</summary>
    Delivered = 2,

    /// <summary>The platform rejected the message. <see cref="DeliveryOutcome.Error"/> says whether a retry can help.</summary>
    Failed = 3,

    /// <summary>
    /// The request was sent and no answer came back. The message may or may not exist. Reconcile
    /// before resending, or accept the risk of a duplicate.
    /// </summary>
    Unknown = 4,
}

/// <summary>Stable error codes. New codes may be added in minor versions; treat unknown values as <see cref="UpstreamFailure"/>.</summary>
public enum HulakiErrorCode
{
    /// <summary>The channel is misconfigured (missing token, bad URL).</summary>
    InvalidConfiguration = 0,

    /// <summary>The message cannot be sent as given (too long, too many attachments, unsupported media type).</summary>
    InvalidInput = 1,

    /// <summary>The channel does not offer a capability the message needs.</summary>
    UnsupportedCapability = 2,

    /// <summary>The credential was refused. Refreshing it may help.</summary>
    Unauthorized = 3,

    /// <summary>The credential is revoked or expired beyond refresh. A person has to reconnect the account.</summary>
    ReconnectRequired = 4,

    /// <summary>The credential lacks a scope, or the app lacks platform approval.</summary>
    PermissionDenied = 5,

    /// <summary>The recipient does not exist (unknown chat, expired Web Push subscription).</summary>
    RecipientNotFound = 6,

    /// <summary>The recipient blocked the sender or left. Stop sending to them.</summary>
    RecipientBlocked = 7,

    /// <summary>Rate limited. <see cref="HulakiError.RetryAfter"/> says when to try again, if the platform said.</summary>
    RateLimited = 8,

    /// <summary>The platform refused the content (policy, spam filter, template not approved).</summary>
    ContentRejected = 9,

    /// <summary>An attachment could not be read, uploaded or processed.</summary>
    MediaError = 10,

    /// <summary>The platform failed (5xx, malformed response).</summary>
    UpstreamFailure = 11,

    /// <summary>The request went out and the answer was lost. See <see cref="DeliveryStatus.Unknown"/>.</summary>
    AmbiguousOutcome = 12,

    /// <summary>The attempt timed out.</summary>
    Timeout = 13,

    /// <summary>The same idempotency key was used with different content.</summary>
    IdempotencyConflict = 14,

    /// <summary>The platform wants payment (plan limit, billing not set up).</summary>
    BillingRequired = 15,
}

/// <summary>What a caller can do about a failure.</summary>
public enum RetryDisposition
{
    /// <summary>Retrying the same request will not help.</summary>
    Never = 0,

    /// <summary>Retry after <see cref="HulakiError.RetryAfter"/>, or after a backoff if it is null.</summary>
    AfterDelay = 1,

    /// <summary>Retry after a person reconnects the account.</summary>
    AfterReconnect = 2,

    /// <summary>Find out whether the first attempt landed before retrying.</summary>
    ReconcileFirst = 3,
}

/// <summary>
/// A failure, safe to log: <see cref="Message"/> never contains tokens, recipient addresses or
/// message content (ADR-0007).
/// </summary>
/// <param name="Code">Stable code.</param>
/// <param name="Retry">What the caller can do.</param>
/// <param name="Message">Short, redacted description.</param>
public sealed record HulakiError(HulakiErrorCode Code, RetryDisposition Retry, string Message)
{
    /// <summary>How long the platform asked us to wait, when it said.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>HTTP status of the platform response, when there was one.</summary>
    public int? HttpStatus { get; init; }

    /// <summary>The platform's own error code, when it has one (for example Telegram <c>error_code</c>, Slack <c>invalid_payload</c>).</summary>
    public string? PlatformCode { get; init; }
}

/// <summary>A problem found by <see cref="IChannel.Prepare"/> before any network call.</summary>
/// <param name="Code">Stable kebab-case code, for example <c>text-too-long</c>.</param>
/// <param name="Message">Human description, redacted.</param>
/// <param name="IsError">True when the message cannot be sent; false for a warning such as a dropped title.</param>
public sealed record PreparationIssue(string Code, string Message, bool IsError = true);

/// <summary>The result of sending to one target. Returned, not thrown, for anything the platform decides (ADR-0007).</summary>
public sealed class DeliveryOutcome
{
    private DeliveryOutcome(DeliveryStatus status) => Status = status;

    /// <summary>What happened.</summary>
    public DeliveryStatus Status { get; }

    /// <summary>The platform's id for the message, when it gave one.</summary>
    public string? PlatformMessageId { get; private init; }

    /// <summary>Public link to the message, for social posts.</summary>
    public Uri? Url { get; private init; }

    /// <summary>The error for <see cref="DeliveryStatus.Failed"/>, <see cref="DeliveryStatus.Unknown"/> and some <see cref="DeliveryStatus.NotSubmitted"/> outcomes.</summary>
    public HulakiError? Error { get; private init; }

    /// <summary>Preparation issues, including warnings on successful sends. Never null.</summary>
    public IReadOnlyList<PreparationIssue> Issues { get; private init; } = [];

    /// <summary>How many attempts were made. Zero when nothing was sent.</summary>
    public int Attempts { get; init; }

    /// <summary>True when the outcome came from the idempotency store rather than a new send.</summary>
    public bool IsReplay { get; init; }

    /// <summary>True for <see cref="DeliveryStatus.Delivered"/> and <see cref="DeliveryStatus.Accepted"/>.</summary>
    public bool Succeeded => Status is DeliveryStatus.Delivered or DeliveryStatus.Accepted;

    /// <summary>Creates a delivered outcome.</summary>
    /// <param name="platformMessageId">Platform id, if any.</param>
    /// <param name="url">Public link, if any.</param>
    /// <param name="warnings">Non-fatal preparation issues.</param>
    /// <returns>The outcome.</returns>
    public static DeliveryOutcome Delivered(string? platformMessageId = null, Uri? url = null, IReadOnlyList<PreparationIssue>? warnings = null) =>
        new(DeliveryStatus.Delivered) { PlatformMessageId = platformMessageId, Url = url, Issues = warnings ?? [] };

    /// <summary>Creates an accepted outcome.</summary>
    /// <param name="platformMessageId">Platform id, if any.</param>
    /// <returns>The outcome.</returns>
    public static DeliveryOutcome Accepted(string? platformMessageId = null) =>
        new(DeliveryStatus.Accepted) { PlatformMessageId = platformMessageId };

    /// <summary>Creates a failed outcome.</summary>
    /// <param name="error">The error.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
    public static DeliveryOutcome Failed(HulakiError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(DeliveryStatus.Failed) { Error = error };
    }

    /// <summary>Creates an unknown outcome: the request went out and no answer came back.</summary>
    /// <param name="error">Usually <see cref="HulakiErrorCode.AmbiguousOutcome"/> or <see cref="HulakiErrorCode.Timeout"/> with <see cref="RetryDisposition.ReconcileFirst"/>.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
    public static DeliveryOutcome Unknown(HulakiError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(DeliveryStatus.Unknown) { Error = error };
    }

    /// <summary>Creates a not-submitted outcome from preparation issues.</summary>
    /// <param name="issues">At least one issue with <see cref="PreparationIssue.IsError"/> set.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="issues"/> is null.</exception>
    public static DeliveryOutcome NotSubmitted(IReadOnlyList<PreparationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        return new(DeliveryStatus.NotSubmitted) { Issues = issues };
    }

    /// <summary>Creates a not-submitted outcome from an error found before sending, such as an idempotency conflict.</summary>
    /// <param name="error">The error.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
    public static DeliveryOutcome NotSubmitted(HulakiError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(DeliveryStatus.NotSubmitted) { Error = error };
    }

    /// <summary>Returns a copy with the attempt count and replay flag set. Used by the send pipeline.</summary>
    /// <param name="attempts">Attempts made.</param>
    /// <param name="isReplay">Whether the outcome is a replay.</param>
    /// <returns>The copy.</returns>
    public DeliveryOutcome With(int attempts, bool isReplay = false) =>
        new(Status)
        {
            PlatformMessageId = PlatformMessageId,
            Url = Url,
            Error = Error,
            Issues = Issues,
            Attempts = attempts,
            IsReplay = isReplay,
        };

    /// <summary>Returns a copy with the non-error <paramref name="issues"/> appended to <see cref="Issues"/>.</summary>
    internal DeliveryOutcome WithWarnings(IReadOnlyList<PreparationIssue> issues)
    {
        var warnings = issues.Where(i => !i.IsError).ToArray();
        return warnings.Length == 0
            ? this
            : new(Status)
            {
                PlatformMessageId = PlatformMessageId,
                Url = Url,
                Error = Error,
                Issues = [.. Issues, .. warnings],
                Attempts = Attempts,
                IsReplay = IsReplay,
            };
    }

    /// <summary>Throws unless the outcome succeeded. For callers who prefer exceptions.</summary>
    /// <exception cref="HulakiDeliveryException">The outcome is not <see cref="DeliveryStatus.Delivered"/> or <see cref="DeliveryStatus.Accepted"/>.</exception>
    public void EnsureSucceeded()
    {
        if (!Succeeded)
        {
            throw new HulakiDeliveryException(this);
        }
    }

    /// <inheritdoc />
    public override string ToString() => Error is null ? $"{Status}" : $"{Status} ({Error.Code})";
}

/// <summary>Thrown by <see cref="DeliveryOutcome.EnsureSucceeded"/>.</summary>
public sealed class HulakiDeliveryException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="outcome">The failed outcome.</param>
    public HulakiDeliveryException(DeliveryOutcome outcome)
        : base($"Delivery did not succeed: {outcome}.") => Outcome = outcome;

    /// <summary>Not supported; use the outcome constructor.</summary>
    public HulakiDeliveryException() : base("Delivery did not succeed.") => Outcome = DeliveryOutcome.NotSubmitted([]);

    /// <summary>Not supported; use the outcome constructor.</summary>
    /// <param name="message">Message.</param>
    public HulakiDeliveryException(string message) : base(message) => Outcome = DeliveryOutcome.NotSubmitted([]);

    /// <summary>Not supported; use the outcome constructor.</summary>
    /// <param name="message">Message.</param>
    /// <param name="innerException">Inner exception.</param>
    public HulakiDeliveryException(string message, Exception innerException) : base(message, innerException) => Outcome = DeliveryOutcome.NotSubmitted([]);

    /// <summary>The outcome that failed.</summary>
    public DeliveryOutcome Outcome { get; }
}

/// <summary>Aggregate status of a multi-target send.</summary>
public enum SendStatus
{
    /// <summary>Every target succeeded.</summary>
    Complete = 0,

    /// <summary>Some targets succeeded, some did not.</summary>
    Partial = 1,

    /// <summary>No target succeeded.</summary>
    Failed = 2,
}

/// <summary>One target and what happened to it.</summary>
/// <param name="Target">The target.</param>
/// <param name="Outcome">Its outcome.</param>
public sealed record TargetOutcome(Target Target, DeliveryOutcome Outcome);

/// <summary>The result of <c>HulakiClient.SendAsync</c>: one outcome per target, in input order.</summary>
public sealed class SendResult
{
    /// <summary>Creates a result.</summary>
    /// <param name="outcomes">Outcomes in input order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="outcomes"/> is null.</exception>
    public SendResult(IReadOnlyList<TargetOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        Outcomes = outcomes;
        var succeeded = outcomes.Count(o => o.Outcome.Succeeded);
        Status = succeeded == outcomes.Count ? SendStatus.Complete : succeeded == 0 ? SendStatus.Failed : SendStatus.Partial;
    }

    /// <summary>Outcomes in input order.</summary>
    public IReadOnlyList<TargetOutcome> Outcomes { get; }

    /// <summary>Aggregate status.</summary>
    public SendStatus Status { get; }
}

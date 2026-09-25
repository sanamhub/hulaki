using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Hulaki.Diagnostics;
using Hulaki.Markup;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hulaki.Channels;

/// <summary>
/// Base for channel implementations. Owns preparation checks, local rate limiting, the shared
/// pause after a 429, and the retry rules of ADR-0005. A provider implements
/// <see cref="SendOnceAsync"/> and maps the platform's answer to an outcome.
/// </summary>
public abstract class ChannelBase : IChannel, IDisposable
{
    private readonly RateLimiter? _limiter;
    private readonly bool _ownsLimiter;
    private readonly Func<double> _jitter;
    private readonly ILogger _logger;
    private long _pausedUntilTicks;

    /// <summary>Creates the channel.</summary>
    /// <param name="name">Registration name.</param>
    /// <param name="capabilities">The manifest.</param>
    /// <param name="options">Shared options.</param>
    /// <param name="defaultLimiter">Factory for the platform's default limiter, or null for none. Called only when the caller did not supply one.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    protected ChannelBase(string name, CapabilityManifest capabilities, ChannelOptions options, Func<RateLimiter>? defaultLimiter = null)
        : this(name, capabilities, options, defaultLimiter, jitter: null)
    {
    }

    internal ChannelBase(string name, CapabilityManifest capabilities, ChannelOptions options, Func<RateLimiter>? defaultLimiter, Func<double>? jitter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Retry.MaxAttempts < 1)
        {
            throw new ArgumentException("Retry.MaxAttempts must be at least 1.", nameof(options));
        }

        Name = name;
        Capabilities = capabilities;
        Options = options;
        _jitter = jitter ?? Random.Shared.NextDouble;
        _logger = (options.LoggerFactory ?? NullLoggerFactory.Instance).CreateLogger(GetType().FullName ?? nameof(ChannelBase));
        if (!options.DisableRateLimiting)
        {
            _limiter = options.RateLimiter;
            if (_limiter is null && defaultLimiter is not null)
            {
                _limiter = defaultLimiter();
                _ownsLimiter = true;
            }
        }
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public CapabilityManifest Capabilities { get; }

    /// <summary>Shared options.</summary>
    protected ChannelOptions Options { get; }

    /// <inheritdoc />
    public IReadOnlyList<PreparationIssue> Prepare(Message message, Recipient recipient)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(recipient);

        var issues = new List<PreparationIssue>();
        if (message.Format == TextFormat.Markup && message.Overflow == OverflowBehavior.Truncate)
        {
            issues.Add(new("truncate-markup-unsupported", "Truncation works on plain text only in 0.x."));
        }
        else if (message.Overflow == OverflowBehavior.Reject && !Capabilities.TextLimit.Fits(CountedText(message)))
        {
            issues.Add(new(
                "text-too-long",
                $"Text is over {Capabilities.TextLimit.Max} {Capabilities.TextLimit.Counter.Unit}."));
        }

        if (message.Media.Count > Capabilities.MaxAttachments)
        {
            issues.Add(new("too-many-attachments", $"At most {Capabilities.MaxAttachments} attachments."));
        }

        foreach (var media in message.Media)
        {
            var needed = media.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ? Capability.Video : Capability.Images;
            if (!Capabilities.Supports(needed))
            {
                issues.Add(new("unsupported-media", $"{needed} is {Capabilities.Get(needed)} on {Capabilities.Platform}."));
            }
        }

        if (message.Title is not null && !Capabilities.Supports(Capability.Title))
        {
            issues.Add(new("title-inlined", "The title is sent as the first line.", IsError: false));
        }

        PrepareCore(message, recipient, issues);
        return issues;
    }

    /// <inheritdoc />
    public async Task<DeliveryOutcome> SendAsync(Message message, Recipient recipient, CancellationToken cancellationToken = default)
    {
        var issues = Prepare(message, recipient);
        using var activity = HulakiDiagnostics.StartSend(Name, Capabilities.Platform);
        var started = Options.TimeProvider.GetTimestamp();

        DeliveryOutcome outcome;
        if (issues.Any(i => i.IsError))
        {
            outcome = DeliveryOutcome.NotSubmitted(issues);
        }
        else
        {
            if (message.Overflow == OverflowBehavior.Truncate)
            {
                message = message.WithTruncatedText(Capabilities.TextLimit, CountedText);
            }

            // Warnings such as title-inlined describe what the recipient sees, so they stay on the outcome.
            outcome = await SendWithRetriesAsync(message, recipient, cancellationToken).ConfigureAwait(false);
            outcome = issues.Count == 0 ? outcome : outcome.WithWarnings(issues);
        }

        HulakiDiagnostics.Complete(activity, outcome, isReplay: false);
        HulakiDiagnostics.RecordSend(Capabilities.Platform, outcome, Options.TimeProvider.GetElapsedTime(started));
        LogOutcome(outcome);
        return outcome;
    }

    /// <summary>
    /// Sends once. Map every platform answer to an outcome. Let <see cref="HttpRequestException"/>
    /// and timeouts propagate: the base class decides whether the request was dispatched.
    /// </summary>
    /// <param name="message">The message, already checked and truncated if asked.</param>
    /// <param name="recipient">The recipient.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    /// <returns>The outcome of this one attempt.</returns>
    protected abstract Task<DeliveryOutcome> SendOnceAsync(Message message, Recipient recipient, CancellationToken cancellationToken);

    private async Task<DeliveryOutcome> SendWithRetriesAsync(Message message, Recipient recipient, CancellationToken cancellationToken)
    {
        var retry = Options.Retry;
        for (var attempt = 1; ; attempt++)
        {
            await WaitForPauseAsync(cancellationToken).ConfigureAwait(false);

            using var lease = _limiter is null ? null : await _limiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
            if (lease is { IsAcquired: false })
            {
                return DeliveryOutcome.Failed(new HulakiError(HulakiErrorCode.RateLimited, RetryDisposition.AfterDelay, "Local rate limiter queue is full.")).With(attempt - 1);
            }

            var outcome = await SendOnceGuardedAsync(message, recipient, cancellationToken).ConfigureAwait(false);
            if (attempt >= retry.MaxAttempts || !ShouldRetry(outcome, retry))
            {
                return outcome.With(attempt);
            }

            var delay = outcome.Error!.RetryAfter ?? Backoff(attempt, retry);
            if (delay > retry.MaxRetryAfter)
            {
                return outcome.With(attempt);
            }

            if (outcome.Error.Code == HulakiErrorCode.RateLimited)
            {
                var until = Options.TimeProvider.GetUtcNow() + delay;
                PauseUntil(until);
                Log.ChannelPaused(_logger, Name, until);
            }

            HulakiDiagnostics.RecordRetry(Capabilities.Platform, outcome.Error.Code);
            Log.Retrying(_logger, Name, Capabilities.Platform, outcome.Error.Code, delay);
            await Task.Delay(delay, Options.TimeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Provider-specific preparation checks. Must not do I/O.</summary>
    /// <param name="message">The message.</param>
    /// <param name="recipient">The recipient.</param>
    /// <param name="issues">Add issues here.</param>
    protected virtual void PrepareCore(Message message, Recipient recipient, ICollection<PreparationIssue> issues)
    {
    }

    /// <summary>
    /// The text exactly as the platform counts it. The default is the title line, if the channel has
    /// no title field, followed by the body rendered as plain text.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>Text to measure against <see cref="CapabilityManifest.TextLimit"/>.</returns>
    protected virtual string CountedText(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var body = message.Format == TextFormat.Markup ? MarkupDocument.Parse(message.Text).ToPlainText() : message.Text;
        return message.Title is not null && !Capabilities.Supports(Capability.Title) ? message.Title + "\n" + body : body;
    }

    /// <summary>Releases the default rate limiter, if this channel created it.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases resources.</summary>
    /// <param name="disposing">True from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing && _ownsLimiter)
        {
            _limiter?.Dispose();
        }
    }

    private void LogOutcome(DeliveryOutcome outcome)
    {
        switch (outcome.Status)
        {
            case DeliveryStatus.Delivered or DeliveryStatus.Accepted:
                Log.SendDelivered(_logger, Name, Capabilities.Platform, outcome.Attempts);
                break;
            case DeliveryStatus.Failed:
                Log.SendFailed(_logger, Name, Capabilities.Platform, outcome.Error!.Code);
                break;
            case DeliveryStatus.Unknown:
                Log.OutcomeUnknown(_logger, Name, Capabilities.Platform);
                break;
            default:
                break;
        }
    }

    private static bool ShouldRetry(DeliveryOutcome outcome, SendRetryPolicy retry) => outcome switch
    {
        { Status: DeliveryStatus.Failed, Error.Retry: RetryDisposition.AfterDelay } => true,
        { Status: DeliveryStatus.Unknown } => retry.ResendUnknown,
        _ => false,
    };

    private async Task<DeliveryOutcome> SendOnceGuardedAsync(Message message, Recipient recipient, CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await SendOnceAsync(message, recipient, cancellationToken).ConfigureAwait(false);
            if (outcome.Status == DeliveryStatus.Unknown && Capabilities.Supports(Capability.IdempotentSend))
            {
                // The platform deduplicates by key, so resending cannot create a duplicate.
                return DeliveryOutcome.Failed(outcome.Error! with { Retry = RetryDisposition.AfterDelay });
            }

            return outcome;
        }
        catch (HttpRequestException ex) when (IsBeforeDispatch(ex.HttpRequestError))
        {
            return DeliveryOutcome.Failed(new HulakiError(HulakiErrorCode.UpstreamFailure, RetryDisposition.AfterDelay, $"Could not connect ({ex.HttpRequestError})."));
        }
        catch (HttpRequestException ex)
        {
            return Ambiguous(new HulakiError(HulakiErrorCode.AmbiguousOutcome, RetryDisposition.ReconcileFirst, $"Connection failed after the request was sent ({ex.HttpRequestError})."));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout. The request may have been sent.
            return Ambiguous(new HulakiError(HulakiErrorCode.Timeout, RetryDisposition.ReconcileFirst, "The attempt timed out."));
        }
    }

    private DeliveryOutcome Ambiguous(HulakiError error) =>
        Capabilities.Supports(Capability.IdempotentSend)
            ? DeliveryOutcome.Failed(error with { Retry = RetryDisposition.AfterDelay })
            : DeliveryOutcome.Unknown(error);

    // Errors raised before any request byte left this process. Safe to retry any method.
    private static bool IsBeforeDispatch(HttpRequestError error) =>
        error is HttpRequestError.NameResolutionError
            or HttpRequestError.ConnectionError
            or HttpRequestError.SecureConnectionError
            or HttpRequestError.ProxyTunnelError;

    private TimeSpan Backoff(int attempt, SendRetryPolicy retry)
    {
        // Equal jitter: half fixed, half random, so concurrent senders spread out but never retry instantly.
        var exponential = retry.BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var capped = Math.Min(exponential, retry.MaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds((capped / 2) + (_jitter() * capped / 2));
    }

    private void PauseUntil(DateTimeOffset until)
    {
        var ticks = until.UtcTicks;
        long current;
        do
        {
            current = Interlocked.Read(ref _pausedUntilTicks);
            if (ticks <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _pausedUntilTicks, ticks, current) != current);
    }

    private Task WaitForPauseAsync(CancellationToken cancellationToken)
    {
        var until = Interlocked.Read(ref _pausedUntilTicks);
        var wait = until - Options.TimeProvider.GetUtcNow().UtcTicks;
        return wait > 0 ? Task.Delay(TimeSpan.FromTicks(wait), Options.TimeProvider, cancellationToken) : Task.CompletedTask;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hulaki.Diagnostics;
using Hulaki.Idempotency;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hulaki;

/// <summary>Options for <see cref="HulakiClient"/>.</summary>
public sealed class HulakiClientOptions
{
    /// <summary>Most targets sent at once. Defaults to 8.</summary>
    public int MaxConcurrency { get; set; } = 8;

    /// <summary>Idempotency scope, for example a tenant id. Defaults to <c>default</c>.</summary>
    public string IdempotencyScope { get; set; } = "default";
}

/// <summary>
/// Sends one message to many targets across channels. Each target gets its own outcome; one
/// failure never stops the others (ADR-0005). Thread-safe.
/// </summary>
public sealed class HulakiClient
{
    private readonly Dictionary<string, IChannel> _channels;
    private readonly HulakiClientOptions _options;
    private readonly IIdempotencyStore _store;
    private readonly ILogger<HulakiClient> _logger;

    /// <summary>Creates a client.</summary>
    /// <param name="channels">Channels, with unique names.</param>
    /// <param name="options">Options. Null means defaults.</param>
    /// <param name="idempotencyStore">Store for idempotency keys. Null means an <see cref="InMemoryIdempotencyStore"/>.</param>
    /// <param name="logger">Logger for client events. Null means no logging. Hulaki never logs content or addresses.</param>
    /// <exception cref="ArgumentNullException"><paramref name="channels"/> is null.</exception>
    /// <exception cref="ArgumentException">Two channels share a name, or <see cref="HulakiClientOptions.MaxConcurrency"/> is below 1.</exception>
    public HulakiClient(
        IEnumerable<IChannel> channels,
        HulakiClientOptions? options = null,
        IIdempotencyStore? idempotencyStore = null,
        ILogger<HulakiClient>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(channels);
        _options = options ?? new HulakiClientOptions();
        if (_options.MaxConcurrency < 1)
        {
            throw new ArgumentException("MaxConcurrency must be at least 1.", nameof(options));
        }

        _channels = new Dictionary<string, IChannel>(StringComparer.Ordinal);
        foreach (var channel in channels)
        {
            if (!_channels.TryAdd(channel.Name, channel))
            {
                throw new ArgumentException($"Two channels are named '{channel.Name}'.", nameof(channels));
            }
        }

        _store = idempotencyStore ?? new InMemoryIdempotencyStore();
        _logger = logger ?? NullLogger<HulakiClient>.Instance;
    }

    /// <summary>Registered channels by name.</summary>
    public IReadOnlyDictionary<string, IChannel> Channels => _channels;

    /// <summary>
    /// Sends <paramref name="message"/> to every target. With an idempotency key, a repeat call
    /// with the same content replays stored outcomes and sends only to targets that have none.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="targets">Targets, in the order outcomes are returned.</param>
    /// <param name="cancellationToken">Cancels sends that have not started. Sends already in flight follow <see cref="IChannel.SendAsync"/>.</param>
    /// <returns>One outcome per target.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">A target names an unregistered channel.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<SendResult> SendAsync(Message message, IReadOnlyList<Target> targets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(targets);
        foreach (var target in targets)
        {
            if (!_channels.ContainsKey(target.Channel))
            {
                throw new ArgumentException($"No channel is named '{target.Channel}'.", nameof(targets));
            }
        }

        IReadOnlyDictionary<string, DeliveryOutcome> replay = new Dictionary<string, DeliveryOutcome>();
        var key = message.IdempotencyKey;
        if (key is not null)
        {
            var claim = await _store.ClaimAsync(_options.IdempotencyScope, key, MessageFingerprint.Compute(message), cancellationToken).ConfigureAwait(false);
            if (claim.Status == ClaimStatus.Conflict)
            {
                Log.IdempotencyConflict(_logger, targets.Select(t => t.Channel).Distinct(StringComparer.Ordinal).Count());
                var conflict = DeliveryOutcome.NotSubmitted(new HulakiError(
                    HulakiErrorCode.IdempotencyConflict, RetryDisposition.Never, "The idempotency key was used with different content."));
                return new SendResult([.. targets.Select(t => new TargetOutcome(t, conflict))]);
            }

            replay = claim.Outcomes;
        }

        var outcomes = new TargetOutcome[targets.Count];
        await Parallel.ForAsync(
            0,
            targets.Count,
            new ParallelOptions { MaxDegreeOfParallelism = _options.MaxConcurrency, CancellationToken = cancellationToken },
            async (index, ct) =>
            {
                var target = targets[index];
                if (replay.TryGetValue(target.Key, out var stored) && IsFinal(stored))
                {
                    var replayed = stored.With(stored.Attempts, isReplay: true);
                    using (var activity = HulakiDiagnostics.StartSend(target.Channel, _channels[target.Channel].Capabilities.Platform))
                    {
                        HulakiDiagnostics.Complete(activity, replayed, isReplay: true);
                    }

                    outcomes[index] = new TargetOutcome(target, replayed);
                    return;
                }

                var outcome = await SendOneAsync(_channels[target.Channel], message, target.Recipient, ct).ConfigureAwait(false);
                if (key is not null)
                {
                    await _store.SaveOutcomeAsync(_options.IdempotencyScope, key, target.Key, outcome, ct).ConfigureAwait(false);
                }

                outcomes[index] = new TargetOutcome(target, outcome);
            }).ConfigureAwait(false);

        return new SendResult(outcomes);
    }

    // A provider bug must cost one target, not the batch (AC-3.2). Cancellation still propagates.
    private async Task<DeliveryOutcome> SendOneAsync(IChannel channel, Message message, Recipient recipient, CancellationToken cancellationToken)
    {
        try
        {
            return await channel.SendAsync(message, recipient, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.ProviderThrew(_logger, channel.Name, ex.GetType().Name);
            return DeliveryOutcome.Failed(new HulakiError(
                HulakiErrorCode.UpstreamFailure, RetryDisposition.Never, $"Channel threw {ex.GetType().Name}."));
        }
    }

    // A failure the platform asked us to retry is resent on a repeat call. Everything else,
    // including Unknown, is replayed: resending Unknown could post twice.
    private static bool IsFinal(DeliveryOutcome stored) =>
        stored.Status != DeliveryStatus.Failed || stored.Error?.Retry != RetryDisposition.AfterDelay;
}

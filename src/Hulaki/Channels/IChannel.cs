using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hulaki;

/// <summary>
/// A configured destination platform: a bot token, a webhook, a social account. Thread-safe and
/// meant to be long-lived. Implement it by deriving from <see cref="Channels.ChannelBase"/>,
/// which supplies the retry rules of ADR-0005.
/// </summary>
public interface IChannel
{
    /// <summary>Name given at registration, unique within a <c>HulakiClient</c>.</summary>
    string Name { get; }

    /// <summary>What the channel can do.</summary>
    CapabilityManifest Capabilities { get; }

    /// <summary>
    /// Checks <paramref name="message"/> against the channel's limits. Pure: no I/O, no clock, no
    /// randomness. Returns an empty list when the message can be sent as is.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="recipient">The recipient.</param>
    /// <returns>Issues found. Errors block the send; warnings do not.</returns>
    /// <exception cref="System.ArgumentNullException">An argument is null.</exception>
    IReadOnlyList<PreparationIssue> Prepare(Message message, Recipient recipient);

    /// <summary>
    /// Sends <paramref name="message"/> to <paramref name="recipient"/>. Never throws for a
    /// platform decision; the outcome carries it.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="recipient">The recipient.</param>
    /// <param name="cancellationToken">Cancels the send. A cancel after the request went out yields <see cref="DeliveryStatus.Unknown"/> only if the platform may have received it; otherwise it throws.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="System.ArgumentNullException">An argument is null.</exception>
    /// <exception cref="System.OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before the request went out.</exception>
    Task<DeliveryOutcome> SendAsync(Message message, Recipient recipient, CancellationToken cancellationToken = default);
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hulaki.Text;

namespace Hulaki.Testing;

/// <summary>
/// An <see cref="IChannel"/> that records every send and answers from a function, for testing code
/// that uses <see cref="HulakiClient"/>. It makes no network call. Thread-safe.
/// </summary>
public sealed class FakeChannel : IChannel
{
    private readonly ConcurrentQueue<(Message Message, Recipient Recipient)> _sent = new();
    private readonly Func<Message, Recipient, DeliveryOutcome> _respond;

    /// <summary>Creates the channel.</summary>
    /// <param name="name">Registration name.</param>
    /// <param name="capabilities">The manifest to report. Null means <see cref="DefaultManifest"/>.</param>
    /// <param name="respond">Returns the outcome of each send. Null means every send is <see cref="DeliveryStatus.Delivered"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public FakeChannel(string name, CapabilityManifest? capabilities = null, Func<Message, Recipient, DeliveryOutcome>? respond = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Capabilities = capabilities ?? DefaultManifest;
        _respond = respond ?? ((_, _) => DeliveryOutcome.Delivered());
    }

    /// <summary>Platform <c>fake</c>: text only, 4096 graphemes.</summary>
    public static CapabilityManifest DefaultManifest { get; } = new(
        "fake",
        new TextLimit(4096, TextCounter.Graphemes),
        [new(Capability.Text, Availability.Available)]);

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public CapabilityManifest Capabilities { get; }

    /// <summary>Every message and recipient given to <see cref="SendAsync"/>, in the order the calls arrived.</summary>
    public IReadOnlyList<(Message Message, Recipient Recipient)> Sent => [.. _sent];

    /// <summary>Checks nothing. The fake accepts any message.</summary>
    /// <param name="message">The message.</param>
    /// <param name="recipient">The recipient.</param>
    /// <returns>An empty list.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public IReadOnlyList<PreparationIssue> Prepare(Message message, Recipient recipient)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(recipient);
        return [];
    }

    /// <summary>Records the send and returns the configured outcome.</summary>
    /// <param name="message">The message.</param>
    /// <param name="recipient">The recipient.</param>
    /// <param name="cancellationToken">Checked before the send is recorded.</param>
    /// <returns>The outcome from the response function.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task<DeliveryOutcome> SendAsync(Message message, Recipient recipient, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(recipient);
        cancellationToken.ThrowIfCancellationRequested();
        _sent.Enqueue((message, recipient));
        return Task.FromResult(_respond(message, recipient));
    }
}

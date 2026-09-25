using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Hulaki.Idempotency;

/// <summary>Result of <see cref="IIdempotencyStore.ClaimAsync"/>.</summary>
public enum ClaimStatus
{
    /// <summary>First use of the key. Send.</summary>
    New = 0,

    /// <summary>The key was used before with the same fingerprint. Replay stored outcomes; send only targets without one.</summary>
    Existing = 1,

    /// <summary>The key was used with a different fingerprint. Do not send.</summary>
    Conflict = 2,
}

/// <summary>A claim on an idempotency key.</summary>
/// <param name="Status">New, existing or conflict.</param>
/// <param name="Outcomes">Stored outcomes by target key, for <see cref="ClaimStatus.Existing"/>. Empty otherwise.</param>
public sealed record IdempotencyClaim(ClaimStatus Status, IReadOnlyDictionary<string, DeliveryOutcome> Outcomes);

/// <summary>
/// Stores idempotency claims and per-target outcomes (ADR-0005). The in-memory store suits a
/// single process; a multi-instance app implements this over its database.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>Claims <paramref name="key"/> in <paramref name="scope"/> for content with <paramref name="fingerprint"/>. Atomic.</summary>
    /// <param name="scope">Tenant or app scope, so two tenants never share a key.</param>
    /// <param name="key">The caller's key.</param>
    /// <param name="fingerprint">From <see cref="MessageFingerprint.Compute"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The claim.</returns>
    ValueTask<IdempotencyClaim> ClaimAsync(string scope, string key, string fingerprint, CancellationToken cancellationToken);

    /// <summary>Records the outcome for one target. Only final outcomes (not <see cref="DeliveryStatus.NotSubmitted"/>) are stored.</summary>
    /// <param name="scope">Scope.</param>
    /// <param name="key">Key.</param>
    /// <param name="targetKey">Opaque target key. Contains a recipient address; store it as personal data.</param>
    /// <param name="outcome">The outcome.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task.</returns>
    ValueTask SaveOutcomeAsync(string scope, string key, string targetKey, DeliveryOutcome outcome, CancellationToken cancellationToken);
}

/// <summary>Process-local <see cref="IIdempotencyStore"/>. Entries expire after <see cref="Retention"/>.</summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<(string Scope, string Key), Entry> _entries = new();
    private readonly TimeProvider _time;

    /// <summary>Creates the store.</summary>
    /// <param name="timeProvider">Clock for expiry. Defaults to the system clock.</param>
    public InMemoryIdempotencyStore(TimeProvider? timeProvider = null) => _time = timeProvider ?? TimeProvider.System;

    /// <summary>How long a key is remembered. Defaults to 24 hours.</summary>
    public TimeSpan Retention { get; init; } = TimeSpan.FromHours(24);

    /// <inheritdoc />
    public ValueTask<IdempotencyClaim> ClaimAsync(string scope, string key, string fingerprint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _time.GetUtcNow();
        var entry = _entries.AddOrUpdate(
            (scope, key),
            _ => new Entry(fingerprint, now, isNew: true),
            (_, existing) => existing.CreatedAt + Retention < now ? new Entry(fingerprint, now, isNew: true) : existing.AsExisting());

        IdempotencyClaim claim = entry switch
        {
            { IsNew: true } => new(ClaimStatus.New, new Dictionary<string, DeliveryOutcome>()),
            _ when !string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal) => new(ClaimStatus.Conflict, new Dictionary<string, DeliveryOutcome>()),
            _ => new(ClaimStatus.Existing, new Dictionary<string, DeliveryOutcome>(entry.Outcomes)),
        };
        return ValueTask.FromResult(claim);
    }

    /// <inheritdoc />
    public ValueTask SaveOutcomeAsync(string scope, string key, string targetKey, DeliveryOutcome outcome, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        cancellationToken.ThrowIfCancellationRequested();
        if (outcome.Status != DeliveryStatus.NotSubmitted && _entries.TryGetValue((scope, key), out var entry))
        {
            entry.Outcomes[targetKey] = outcome;
        }

        return ValueTask.CompletedTask;
    }

    private sealed class Entry
    {
        public Entry(string fingerprint, DateTimeOffset createdAt, bool isNew, ConcurrentDictionary<string, DeliveryOutcome>? outcomes = null)
        {
            Fingerprint = fingerprint;
            CreatedAt = createdAt;
            IsNew = isNew;
            Outcomes = outcomes ?? new ConcurrentDictionary<string, DeliveryOutcome>(StringComparer.Ordinal);
        }

        public string Fingerprint { get; }

        public DateTimeOffset CreatedAt { get; }

        public bool IsNew { get; }

        public ConcurrentDictionary<string, DeliveryOutcome> Outcomes { get; }

        public Entry AsExisting() => IsNew ? new Entry(Fingerprint, CreatedAt, isNew: false, Outcomes) : this;
    }
}

/// <summary>Computes the content fingerprint used by idempotency (ADR-0005).</summary>
public static class MessageFingerprint
{
    /// <summary>
    /// SHA-256 over a canonical JSON form of everything that changes what a recipient sees: text,
    /// format, title, priority, link, and each attachment's fingerprint and type. The idempotency
    /// key itself is excluded.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns><c>sha256:</c> followed by lower-case hex.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    public static string Compute(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", 1);
            writer.WriteString("text", message.Text);
            writer.WriteNumber("format", (int)message.Format);
            writer.WriteString("title", message.Title);
            writer.WriteNumber("priority", (int)message.Priority);
            writer.WriteString("link", message.Link?.AbsoluteUri);
            writer.WriteStartArray("media");
            foreach (var media in message.Media)
            {
                writer.WriteStartObject();
                writer.WriteString("fp", media.Fingerprint);
                writer.WriteString("type", media.ContentType);
                writer.WriteString("alt", media.AltText);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }
}

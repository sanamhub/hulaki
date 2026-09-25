using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Hulaki.Credentials;

/// <summary>
/// Process-local <see cref="ICredentialStore"/>, for tests and single-process tools. Credentials
/// are lost when the process ends, so a rotated refresh token is lost with them. Thread-safe.
/// </summary>
public sealed class InMemoryCredentialStore : ICredentialStore
{
    private readonly ConcurrentDictionary<string, StoredCredential> _credentials = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<StoredCredential?> GetAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_credentials.TryGetValue(key, out var credential) ? credential : null);
    }

    /// <inheritdoc />
    public ValueTask<bool> CompareAndSetAsync(string key, long expectedRevision, StoredCredential value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        if (value.Revision <= expectedRevision)
        {
            throw new ArgumentException("The new revision must be greater than the expected revision.", nameof(value));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!_credentials.TryGetValue(key, out var current))
        {
            return ValueTask.FromResult(expectedRevision == 0 && _credentials.TryAdd(key, value));
        }

        // TryUpdate compares by reference, so a write that landed after the read makes it fail.
        return ValueTask.FromResult(current.Revision == expectedRevision && _credentials.TryUpdate(key, value, current));
    }
}

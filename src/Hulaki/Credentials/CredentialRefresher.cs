using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Hulaki.Credentials;

/// <summary>
/// Returns a credential that is valid for at least <see cref="RefreshMargin"/>, refreshing it when
/// needed without two workers rotating it at once (ADR-0010). In process, callers for one key on
/// one store queue on a semaphore; across processes, the store's compare-and-set decides and the
/// loser uses the winner's credential. Providers with refreshable credentials use it.
/// </summary>
internal sealed class CredentialRefresher
{
    /// <summary>A credential that expires sooner than this is refreshed before use.</summary>
    internal static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    // Keyed by store so two refreshers over one store share locks, and a collected store frees its locks.
    private static readonly ConditionalWeakTable<ICredentialStore, ConcurrentDictionary<string, SemaphoreSlim>> Locks = [];

    private readonly ICredentialStore _store;
    private readonly TimeProvider _time;

    public CredentialRefresher(ICredentialStore store, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Returns the stored credential, refreshed first if it is missing or expires within <see cref="RefreshMargin"/>.</summary>
    /// <param name="key">The account's key in the store.</param>
    /// <param name="refresh">
    /// Gets a new credential from the platform, given the current one (null when none is stored).
    /// The revision it returns is ignored; the refresher assigns the next one.
    /// </param>
    /// <param name="cancellationToken">Cancels the wait and the refresh.</param>
    public ValueTask<StoredCredential> GetValidAsync(
        string key,
        Func<StoredCredential?, CancellationToken, ValueTask<StoredCredential>> refresh,
        CancellationToken cancellationToken) =>
        GetAsync(key, refresh, IsFresh, cancellationToken);

    /// <summary>
    /// Refreshes after the platform refused the credential with <paramref name="rejectedRevision"/>
    /// (a 401). If another caller already replaced it, returns that credential without refreshing.
    /// </summary>
    /// <param name="key">The account's key in the store.</param>
    /// <param name="rejectedRevision">The revision the platform refused.</param>
    /// <param name="refresh">As for <see cref="GetValidAsync"/>.</param>
    /// <param name="cancellationToken">Cancels the wait and the refresh.</param>
    public ValueTask<StoredCredential> RefreshAfterRejectionAsync(
        string key,
        long rejectedRevision,
        Func<StoredCredential?, CancellationToken, ValueTask<StoredCredential>> refresh,
        CancellationToken cancellationToken) =>
        GetAsync(key, refresh, current => current is not null && current.Revision != rejectedRevision && IsFresh(current), cancellationToken);

    private async ValueTask<StoredCredential> GetAsync(
        string key,
        Func<StoredCredential?, CancellationToken, ValueTask<StoredCredential>> refresh,
        Func<StoredCredential?, bool> usable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(refresh);

        var current = await _store.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (usable(current))
        {
            return current!;
        }

        var gate = Locks.GetOrCreateValue(_store).GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another caller in this process may have refreshed while this one waited.
            current = await _store.GetAsync(key, cancellationToken).ConfigureAwait(false);
            if (usable(current))
            {
                return current!;
            }

            var expected = current?.Revision ?? 0;
            var refreshed = await refresh(current, cancellationToken).ConfigureAwait(false);
            var next = refreshed.WithRevision(expected + 1);
            if (await _store.CompareAndSetAsync(key, expected, next, cancellationToken).ConfigureAwait(false))
            {
                return next;
            }

            // Another process rotated it first. With rotating refresh tokens ours may already be
            // invalid, so use the winner's.
            return await _store.GetAsync(key, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The credential was removed while it was being refreshed.");
        }
        finally
        {
            gate.Release();
        }
    }

    private bool IsFresh(StoredCredential? credential) =>
        credential is not null && (credential.ExpiresAt is null || credential.ExpiresAt.Value - _time.GetUtcNow() >= RefreshMargin);
}

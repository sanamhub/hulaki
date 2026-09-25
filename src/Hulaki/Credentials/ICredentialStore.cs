using System.Threading;
using System.Threading.Tasks;

namespace Hulaki.Credentials;

/// <summary>
/// Keeps refreshable credentials so that several workers can rotate one without overwriting each
/// other (ADR-0010). A multi-instance app implements it over its database with an atomic
/// conditional update; <see cref="InMemoryCredentialStore"/> suits one process.
/// </summary>
public interface ICredentialStore
{
    /// <summary>Reads the credential stored under <paramref name="key"/>.</summary>
    /// <param name="key">The provider's key for the account.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The credential, or null when none is stored.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="key"/> is null.</exception>
    /// <exception cref="System.OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    ValueTask<StoredCredential?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Stores <paramref name="value"/> only if the stored revision is still
    /// <paramref name="expectedRevision"/>. Atomic. A worker that loses the race gets false and
    /// reads the winner's credential instead.
    /// </summary>
    /// <param name="key">The provider's key for the account.</param>
    /// <param name="expectedRevision">The revision read before refreshing. 0 means nothing may be stored yet.</param>
    /// <param name="value">The new credential. Its revision must be greater than <paramref name="expectedRevision"/>.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>True when the value was stored; false when another write came first.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="key"/> or <paramref name="value"/> is null.</exception>
    /// <exception cref="System.ArgumentException">The revision of <paramref name="value"/> is not greater than <paramref name="expectedRevision"/>.</exception>
    /// <exception cref="System.OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    ValueTask<bool> CompareAndSetAsync(string key, long expectedRevision, StoredCredential value, CancellationToken cancellationToken);
}

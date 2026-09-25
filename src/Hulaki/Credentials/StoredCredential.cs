using System;
using System.Collections.Generic;
using System.Globalization;

namespace Hulaki.Credentials;

/// <summary>
/// A refreshable credential as an <see cref="ICredentialStore"/> keeps it (ADR-0010). A class, not a
/// record, because a record's generated <see cref="object.ToString"/> would print the tokens.
/// Immutable.
/// </summary>
public sealed class StoredCredential
{
    private static readonly IReadOnlyDictionary<string, string> NoExtra = new Dictionary<string, string>();

    /// <summary>Creates a credential.</summary>
    /// <param name="revision">Version for compare-and-set, 1 or more. Each write increments it.</param>
    /// <param name="accessToken">The token sent with requests. A secret.</param>
    /// <param name="refreshToken">The token that gets a new access token, if the platform issues one. A secret.</param>
    /// <param name="expiresAt">When <paramref name="accessToken"/> expires. Null when it does not.</param>
    /// <param name="extra">Other fields the provider needs, such as an account id. Not secrets.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="revision"/> is below 1.</exception>
    /// <exception cref="ArgumentException"><paramref name="accessToken"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="accessToken"/> is null.</exception>
    public StoredCredential(
        long revision,
        string accessToken,
        string? refreshToken = null,
        DateTimeOffset? expiresAt = null,
        IReadOnlyDictionary<string, string>? extra = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        Revision = revision;
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        ExpiresAt = expiresAt;
        Extra = extra ?? NoExtra;
    }

    /// <summary>Version for compare-and-set. 1 or more.</summary>
    public long Revision { get; }

    /// <summary>The token sent with requests. A secret: never log it.</summary>
    public string AccessToken { get; }

    /// <summary>The token that gets a new access token, or null. A secret: never log it.</summary>
    public string? RefreshToken { get; }

    /// <summary>When <see cref="AccessToken"/> expires, or null when it does not.</summary>
    public DateTimeOffset? ExpiresAt { get; }

    /// <summary>Other provider fields, such as an account id. Never null.</summary>
    public IReadOnlyDictionary<string, string> Extra { get; }

    /// <summary>Returns <c>StoredCredential(rev=3, expires=2026-09-25T10:00:00.0000000+00:00)</c>. Never a token.</summary>
    /// <returns>A redacted description.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"StoredCredential(rev={Revision}, expires={ExpiresAt?.ToString("O", CultureInfo.InvariantCulture) ?? "none"})");

    /// <summary>Returns a copy with another revision, for the next compare-and-set write.</summary>
    internal StoredCredential WithRevision(long revision) => new(revision, AccessToken, RefreshToken, ExpiresAt, Extra);
}

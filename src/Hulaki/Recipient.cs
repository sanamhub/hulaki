using System;
using System.Collections.Generic;

namespace Hulaki;

/// <summary>
/// Who receives a message on a channel: a Telegram chat id, an email address, a phone number, a
/// Web Push endpoint, or <see cref="Self"/> for the account's own feed on social platforms.
/// </summary>
/// <remarks>
/// Recipients are personal data. Hulaki never writes <see cref="Address"/> or
/// <see cref="Properties"/> to logs or traces, and <see cref="ToString"/> redacts them (ADR-0015).
/// </remarks>
public sealed class Recipient : IEquatable<Recipient>
{
    private static readonly IReadOnlyDictionary<string, string> NoProperties = new Dictionary<string, string>();

    /// <summary>Creates a recipient.</summary>
    /// <param name="address">The channel-specific address.</param>
    /// <param name="properties">Extra fields some channels need, for example Web Push <c>p256dh</c> and <c>auth</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is null.</exception>
    public Recipient(string address, IReadOnlyDictionary<string, string>? properties = null)
    {
        ArgumentNullException.ThrowIfNull(address);
        Address = address;
        Properties = properties ?? NoProperties;
    }

    /// <summary>The account's own feed or timeline. Used by social channels.</summary>
    public static Recipient Self { get; } = new(string.Empty);

    /// <summary>The channel-specific address. Empty for <see cref="Self"/>.</summary>
    public string Address { get; }

    /// <summary>Extra channel-specific fields. Never null.</summary>
    public IReadOnlyDictionary<string, string> Properties { get; }

    /// <summary>True for <see cref="Self"/>.</summary>
    public bool IsSelf => Address.Length == 0;

    /// <inheritdoc />
    public bool Equals(Recipient? other) => other is not null && string.Equals(Address, other.Address, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Recipient);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Address);

    /// <summary>Returns <c>Recipient(self)</c> or <c>Recipient(***)</c>. Never the address.</summary>
    /// <returns>A redacted description.</returns>
    public override string ToString() => IsSelf ? "Recipient(self)" : "Recipient(***)";
}

/// <summary>One destination of a send: a configured channel and a recipient on it.</summary>
/// <param name="Channel">The channel name given at registration.</param>
/// <param name="Recipient">The recipient on that channel.</param>
public sealed record Target(string Channel, Recipient Recipient)
{
    /// <summary>Stable key for idempotency records. Contains the address, so it is never logged.</summary>
    internal string Key => Channel + "\u001f" + Recipient.Address;
}

using System;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Hulaki.WebPush;

/// <summary>
/// Message encryption for Web Push (RFC 8291) in the <c>aes128gcm</c> content coding (RFC 8188),
/// as one record. The RFC 8291 section 5 example is a test.
/// </summary>
internal static class WebPushEncryption
{
    /// <summary>The record size written in the header. One record of up to this size.</summary>
    public const int RecordSize = 4096;

    /// <summary>salt (16) + rs (4) + idlen (1) + keyid, the 65-byte server public key.</summary>
    public const int HeaderSize = 86;

    /// <summary>The GCM tag and the padding delimiter octet.</summary>
    public const int Overhead = 16 + 1;

    /// <summary>The largest plaintext that fits in one 4096-byte record: 4096 - 86 - 17 = 3993.</summary>
    public const int MaxPlaintext = RecordSize - HeaderSize - Overhead;

    private static readonly byte[] KeyInfoPrefix = Encoding.ASCII.GetBytes("WebPush: info\0");
    private static readonly byte[] CekInfo = Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0");
    private static readonly byte[] NonceInfo = Encoding.ASCII.GetBytes("Content-Encoding: nonce\0");

    /// <summary>Encrypts <paramref name="plaintext"/> for the subscription.</summary>
    /// <param name="plaintext">The payload. At most <see cref="MaxPlaintext"/> bytes.</param>
    /// <param name="userAgentPublicKey">The subscription's <c>p256dh</c>: an uncompressed P-256 point.</param>
    /// <param name="authSecret">The subscription's 16-byte <c>auth</c>.</param>
    /// <param name="serverKey">A P-256 key pair used once. RFC 8291 requires a new one per message.</param>
    /// <param name="salt">16 random bytes. New per message.</param>
    /// <returns>The request body: header, then ciphertext and tag.</returns>
    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> userAgentPublicKey, ReadOnlySpan<byte> authSecret, ECDiffieHellman serverKey, ReadOnlySpan<byte> salt)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(plaintext.Length, MaxPlaintext);
        var serverPublic = ExportPoint(serverKey);

        using var userAgent = ImportPoint(userAgentPublicKey);
        var ecdhSecret = serverKey.DeriveRawSecretAgreement(userAgent.PublicKey);

        // key_info = "WebPush: info" || 0x00 || ua_public || as_public
        var keyInfo = new byte[KeyInfoPrefix.Length + 65 + 65];
        KeyInfoPrefix.CopyTo(keyInfo, 0);
        userAgentPublicKey.CopyTo(keyInfo.AsSpan(KeyInfoPrefix.Length));
        serverPublic.CopyTo(keyInfo, KeyInfoPrefix.Length + 65);

        // HKDF-Expand with L no larger than the hash is the single HMAC the RFC writes out.
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ecdhSecret, 32, authSecret.ToArray(), keyInfo);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt.ToArray());
        var cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, CekInfo);
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, NonceInfo);

        var body = new byte[HeaderSize + plaintext.Length + Overhead];
        salt.CopyTo(body);
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(16), RecordSize);
        body[20] = 65;
        serverPublic.CopyTo(body, 21);

        // The last (only) record ends with the 0x02 delimiter and no padding.
        var padded = new byte[plaintext.Length + 1];
        plaintext.CopyTo(padded);
        padded[^1] = 2;

        using var aes = new AesGcm(cek, 16);
        aes.Encrypt(nonce, padded, body.AsSpan(HeaderSize, padded.Length), body.AsSpan(HeaderSize + padded.Length, 16));
        return body;
    }

    /// <summary>Imports an uncompressed P-256 point. Throws <see cref="CryptographicException"/> when it is not on the curve.</summary>
    public static ECDiffieHellman ImportPoint(ReadOnlySpan<byte> point)
    {
        if (point.Length != 65 || point[0] != 4)
        {
            throw new CryptographicException("Not an uncompressed P-256 point.");
        }

        try
        {
            return ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = point[1..33].ToArray(), Y = point[33..].ToArray() },
            });
        }
        catch (PlatformNotSupportedException ex)
        {
            // Windows CNG reports a point off the curve this way; OpenSSL throws CryptographicException.
            throw new CryptographicException("Not a point on P-256.", ex);
        }
    }

    /// <summary>The uncompressed form of a key's public point: 0x04 || X || Y.</summary>
    public static byte[] ExportPoint(ECAlgorithm key)
    {
        var q = key.ExportParameters(includePrivateParameters: false).Q;
        return [4, .. q.X!, .. q.Y!];
    }

    /// <summary>Decodes base64url, tolerating padding and the standard alphabet, which some libraries emit.</summary>
    public static bool TryDecode(string? text, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim().TrimEnd('=').Replace('+', '-').Replace('/', '_');
        if (!Base64Url.IsValid(normalized))
        {
            return false;
        }

        bytes = Base64Url.DecodeFromChars(normalized);
        return true;
    }
}

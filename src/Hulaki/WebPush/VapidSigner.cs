using System;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hulaki.WebPush;

/// <summary>
/// Builds the <c>Authorization: vapid t=&lt;jwt&gt;, k=&lt;key&gt;</c> value of RFC 8292: an ES256 JWT
/// whose <c>aud</c> is the push service's origin. A token is reused for its audience until it is
/// within an hour of expiring, so a batch to one push service signs once.
/// </summary>
internal sealed class VapidSigner : IDisposable
{
    /// <summary>RFC 8292 section 2 caps <c>exp</c> at 24 hours; 12 leaves room for clock skew.</summary>
    internal static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    private static readonly TimeSpan RenewBefore = TimeSpan.FromHours(1);

    private static readonly string Header = Base64Url.EncodeToString("{\"typ\":\"JWT\",\"alg\":\"ES256\"}"u8);

    private readonly ECDsa _key;
    private readonly string _publicKey;
    private readonly string _subject;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, (string Token, DateTimeOffset Expires)> _tokens = new(StringComparer.Ordinal);

    private VapidSigner(ECDsa key, string publicKey, string subject, TimeProvider time)
    {
        _key = key;
        _publicKey = publicKey;
        _subject = subject;
        _time = time;
    }

    /// <summary>Imports the key pair and checks that the private key belongs to the public one. Returns null with a reason otherwise.</summary>
    public static VapidSigner? TryCreate(string publicKey, string privateKey, string subject, TimeProvider time, out string? problem)
    {
        problem = null;
        if (!WebPushEncryption.TryDecode(publicKey, out var q) || q.Length != 65 || q[0] != 4)
        {
            problem = "VapidPublicKey must be a base64url uncompressed P-256 point (65 bytes).";
            return null;
        }

        if (!WebPushEncryption.TryDecode(privateKey, out var d) || d.Length != 32)
        {
            problem = "VapidPrivateKey must be a base64url P-256 private key (32 bytes).";
            return null;
        }

        if (!IsSubject(subject))
        {
            problem = "VapidSubject must be a mailto: or https: URI.";
            return null;
        }

        ECDsa? key = null;
        try
        {
            key = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = d,
                Q = new ECPoint { X = q[1..33], Y = q[33..] },
            });

            // Import does not always check that D and Q match. A signature the public key alone
            // verifies does.
            using var check = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = q[1..33], Y = q[33..] } });
            byte[] probe = [1, 2, 3];
            if (!check.VerifyData(probe, key.SignData(probe, HashAlgorithmName.SHA256), HashAlgorithmName.SHA256))
            {
                problem = "VapidPrivateKey does not belong to VapidPublicKey.";
                key.Dispose();
                return null;
            }

            var signer = new VapidSigner(key, Base64Url.EncodeToString(q), subject, time);
            key = null;
            return signer;
        }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException)
        {
            // Some platforms refuse a D that does not match Q at import; others accept it and fail the probe above.
            problem = "VapidPrivateKey does not belong to VapidPublicKey.";
            key?.Dispose();
            return null;
        }
    }

    public static bool IsSubject(string? subject) =>
        Uri.TryCreate(subject, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeMailto || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>The audience: the endpoint's origin, never the full endpoint (RFC 8292 section 2).</summary>
    public static string AudienceOf(Uri endpoint) => endpoint.GetLeftPart(UriPartial.Authority);

    /// <summary>The parameter of the <c>vapid</c> authorization scheme for <paramref name="endpoint"/>.</summary>
    public string AuthorizationFor(Uri endpoint)
    {
        var audience = AudienceOf(endpoint);
        var now = _time.GetUtcNow();
        if (!_tokens.TryGetValue(audience, out var cached) || cached.Expires - now < RenewBefore)
        {
            var expires = now + Lifetime;
            cached = (Sign(audience, expires), expires);
            _tokens[audience] = cached;
        }

        return $"t={cached.Token}, k={_publicKey}";
    }

    public void Dispose() => _key.Dispose();

    private string Sign(string audience, DateTimeOffset expires)
    {
        using var claims = new MemoryStream();
        using (var writer = new Utf8JsonWriter(claims))
        {
            writer.WriteStartObject();
            writer.WriteString("aud", audience);
            writer.WriteNumber("exp", expires.ToUnixTimeSeconds());
            writer.WriteString("sub", _subject);
            writer.WriteEndObject();
        }

        var signingInput = Header + "." + Base64Url.EncodeToString(claims.ToArray());

        // .NET signs in the IEEE P1363 r || s form, which is what JWS ES256 expects.
        var signature = _key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256);
        return signingInput + "." + Base64Url.EncodeToString(signature);
    }
}

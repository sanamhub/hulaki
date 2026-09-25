using System;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Hulaki.WebPush.Tests;

/// <summary>The published example keys of RFC 8291 section 5 and appendix A, and a receiver that decrypts.</summary>
internal static class Rfc8291
{
    public const string Plaintext = "When I grow up, I want to be a watermelon";
    public const string AuthSecret = "BTBZMqHH6r4Tts7J_aSIgg";
    public const string Salt = "DGv6ra1nlYgDCS1FRnbzlw";
    public const string ReceiverPrivate = "q1dXpw3UpT5VOmu_cf_v6ih07Aems3njxI-JWgLcM94";
    public const string ReceiverPublic = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";
    public const string SenderPrivate = "yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw";
    public const string SenderPublic = "BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8";

    /// <summary>The request body of section 5, without its line breaks.</summary>
    public const string Body =
        "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27ml" +
        "mlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPT" +
        "pK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN";

    public static Recipient Subscriber(string endpoint = "https://push.example.net/push/JzLQ3raZJfFBR0aqvOMsLrt54w4rJUsV") =>
        new(endpoint, new Dictionary<string, string> { ["p256dh"] = ReceiverPublic, ["auth"] = AuthSecret });

    public static ECDiffieHellman SenderKey() => Import(SenderPrivate, SenderPublic);

    /// <summary>What a browser does with a push message: RFC 8291 from the receiver's side.</summary>
    public static byte[] Decrypt(byte[] body)
    {
        var salt = body.AsSpan(0, 16).ToArray();
        var recordSize = BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(16));
        var keyIdLength = body[20];
        var senderPublic = body.AsSpan(21, keyIdLength).ToArray();
        if (recordSize != 4096 || keyIdLength != 65)
        {
            throw new CryptographicException("Unexpected header.");
        }

        using var receiver = Import(ReceiverPrivate, ReceiverPublic);
        using var sender = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = senderPublic[1..33], Y = senderPublic[33..] },
        });
        var ecdh = receiver.DeriveRawSecretAgreement(sender.PublicKey);
        byte[] keyInfo = [.. "WebPush: info\0"u8, .. Base64Url.DecodeFromChars(ReceiverPublic), .. senderPublic];
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ecdh, 32, Base64Url.DecodeFromChars(AuthSecret), keyInfo);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, "Content-Encoding: aes128gcm\0"u8.ToArray());
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, "Content-Encoding: nonce\0"u8.ToArray());

        var record = body.AsSpan(21 + keyIdLength);
        var padded = new byte[record.Length - 16];
        using var aes = new AesGcm(cek, 16);
        aes.Decrypt(nonce, record[..^16], record[^16..], padded);
        if (padded[^1] != 2)
        {
            throw new CryptographicException("Missing last-record delimiter.");
        }

        return padded[..^1];
    }

    public static string DecryptText(byte[] body) => Encoding.UTF8.GetString(Decrypt(body));

    private static ECDiffieHellman Import(string privateKey, string publicKey)
    {
        var q = Base64Url.DecodeFromChars(publicKey);
        return ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = Base64Url.DecodeFromChars(privateKey),
            Q = new ECPoint { X = q[1..33], Y = q[33..] },
        });
    }
}

/// <summary>A VAPID key pair made for the test run. Nothing here is a real key.</summary>
internal sealed class VapidKeys
{
    private VapidKeys(string publicKey, string privateKey)
    {
        PublicKey = publicKey;
        PrivateKey = privateKey;
    }

    public static VapidKeys Shared { get; } = Create();

    public string PublicKey { get; }

    public string PrivateKey { get; }

    public static VapidKeys Create()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(includePrivateParameters: true);
        return new VapidKeys(Base64Url.EncodeToString([4, .. parameters.Q.X!, .. parameters.Q.Y!]), Base64Url.EncodeToString(parameters.D));
    }
}

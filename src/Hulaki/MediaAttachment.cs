using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hulaki;

/// <summary>Where the bytes of a <see cref="MediaAttachment"/> come from.</summary>
public enum MediaSourceKind
{
    /// <summary>A public HTTPS URL. Some channels let the platform fetch it; others download it through the egress guard.</summary>
    HttpsUrl = 0,

    /// <summary>Bytes held in memory.</summary>
    Bytes = 1,

    /// <summary>A stream the channel opens, and may reopen on a retry.</summary>
    Stream = 2,
}

/// <summary>
/// An image or video to attach. Hulaki validates attachments against the channel's limits and
/// never transcodes them (ADR-0008).
/// </summary>
public sealed class MediaAttachment
{
    private readonly ReadOnlyMemory<byte> _bytes;
    private readonly Func<CancellationToken, ValueTask<Stream>>? _open;

    private MediaAttachment(MediaSourceKind kind, string contentType, string fingerprint)
    {
        Kind = kind;
        ContentType = contentType;
        Fingerprint = fingerprint;
    }

    /// <summary>Where the bytes come from.</summary>
    public MediaSourceKind Kind { get; }

    /// <summary>MIME type, for example <c>image/png</c>.</summary>
    public string ContentType { get; }

    /// <summary>The source URL when <see cref="Kind"/> is <see cref="MediaSourceKind.HttpsUrl"/>.</summary>
    public Uri? Url { get; private init; }

    /// <summary>File name offered to the platform. Optional.</summary>
    public string? FileName { get; private init; }

    /// <summary>Length in bytes when known.</summary>
    public long? Length { get; private init; }

    /// <summary>Alternative text for screen readers. Channels that support it send it.</summary>
    public string? AltText { get; init; }

    /// <summary>Stable identity of the content, used in the idempotency fingerprint (ADR-0005).</summary>
    public string Fingerprint { get; }

    /// <summary>Creates an attachment from a public HTTPS URL.</summary>
    /// <param name="url">Absolute <c>https</c> URL.</param>
    /// <param name="contentType">MIME type.</param>
    /// <returns>The attachment. Its fingerprint is the URL.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="url"/> is not an absolute HTTPS URL, or <paramref name="contentType"/> is empty.</exception>
    public static MediaAttachment FromUrl(Uri url, string contentType)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        if (!url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Media URLs must be absolute https URLs.", nameof(url));
        }

        return new MediaAttachment(MediaSourceKind.HttpsUrl, contentType, url.AbsoluteUri) { Url = url };
    }

    /// <summary>Creates an attachment from bytes in memory.</summary>
    /// <param name="bytes">The content. Not copied; do not change it until the send completes.</param>
    /// <param name="contentType">MIME type.</param>
    /// <param name="fileName">Optional file name.</param>
    /// <returns>The attachment. Its fingerprint is the SHA-256 of the bytes.</returns>
    /// <exception cref="ArgumentException"><paramref name="bytes"/> is empty, or <paramref name="contentType"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="contentType"/> is null.</exception>
    public static MediaAttachment FromBytes(ReadOnlyMemory<byte> bytes, string contentType, string? fileName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        if (bytes.IsEmpty)
        {
            throw new ArgumentException("Media content must not be empty.", nameof(bytes));
        }

        var fingerprint = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes.Span));
        return new MediaAttachment(MediaSourceKind.Bytes, contentType, fingerprint)
        {
            FileName = fileName,
            Length = bytes.Length,
            BytesValue = bytes,
        };
    }

    /// <summary>Creates an attachment from a stream factory.</summary>
    /// <param name="open">Opens a new stream positioned at the start. Called once per attempt.</param>
    /// <param name="contentType">MIME type.</param>
    /// <param name="fingerprint">Caller-supplied stable identity, for example a content hash or a storage version id.</param>
    /// <param name="length">Length in bytes if known. Some platforms need it before upload.</param>
    /// <param name="fileName">Optional file name.</param>
    /// <returns>The attachment.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="contentType"/> or <paramref name="fingerprint"/> is empty.</exception>
    public static MediaAttachment FromStream(
        Func<CancellationToken, ValueTask<Stream>> open,
        string contentType,
        string fingerprint,
        long? length = null,
        string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        return new MediaAttachment(MediaSourceKind.Stream, contentType, fingerprint, open)
        {
            FileName = fileName,
            Length = length,
        };
    }

    private MediaAttachment(MediaSourceKind kind, string contentType, string fingerprint, Func<CancellationToken, ValueTask<Stream>> open)
        : this(kind, contentType, fingerprint) => _open = open;

    private ReadOnlyMemory<byte> BytesValue { init => _bytes = value; }

    /// <summary>Opens the content as a new stream. Channels call this; callers rarely need it.</summary>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>A stream positioned at the start. The caller disposes it.</returns>
    /// <exception cref="InvalidOperationException"><see cref="Kind"/> is <see cref="MediaSourceKind.HttpsUrl"/>; download URLs through the egress guard instead.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public ValueTask<Stream> OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Kind switch
        {
            MediaSourceKind.Bytes => new ValueTask<Stream>(new MemoryStream(_bytes.ToArray(), writable: false)),
            MediaSourceKind.Stream => _open!(cancellationToken),
            _ => throw new InvalidOperationException("URL media has no local stream."),
        };
    }
}

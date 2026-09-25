using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Hulaki.Bluesky.Wire;
using Hulaki.Channels;
using Hulaki.Credentials;
using Hulaki.Text;

namespace Hulaki.Bluesky;

/// <summary>
/// Posts to the account's Bluesky feed with an app password: <c>createSession</c> once, then
/// <c>createRecord</c> of an <c>app.bsky.feed.post</c> per post, refreshing the session when it
/// expires (ADR-0010). The recipient is <see cref="Recipient.Self"/>. Links, from markup or bare
/// in the text, become link facets.
/// </summary>
public sealed partial class BlueskyChannel : ChannelBase
{
    /// <summary>Bluesky allows 300 graphemes per post, counted with the extended grapheme rules (GB9c included).</summary>
    public static CapabilityManifest Manifest { get; } = new(
        "bluesky",
        new TextLimit(300, TextCounter.Graphemes),
        [
            new(Capability.Text, Availability.Available, "Longer text can become a thread (ThreadLongPosts)"),
            new(Capability.Markup, Availability.Available, "Links become facets; bold, italic and code are sent as plain text"),
            new(Capability.Title, Availability.UnsupportedByPlatform, "Sent as the first line"),
            new(Capability.Priority, Availability.UnsupportedByPlatform),
            new(Capability.ClickAction, Availability.UnsupportedByPlatform, "The link is appended as a facet"),
            new(Capability.Images, Availability.NotImplemented),
            new(Capability.IdempotentSend, Availability.UnsupportedByPlatform),
            new(Capability.Delete, Availability.NotImplemented),
        ]);

    private readonly HttpClient _http;
    private readonly BlueskyChannelOptions _options;
    private readonly CredentialRefresher _sessions;
    private readonly string _sessionKey;

    /// <summary>Creates the channel.</summary>
    /// <param name="name">Registration name.</param>
    /// <param name="http">Client for the account's server. The channel does not dispose it.</param>
    /// <param name="options">Options with the identifier and app password.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/>, the identifier or the app password is empty, or the service URL is not absolute.</exception>
    public BlueskyChannel(string name, HttpClient http, BlueskyChannelOptions options)
        : base(name, Manifest, options, DefaultLimiter)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Identifier, "options.Identifier");
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AppPassword, "options.AppPassword");
        if (options.ServiceUrl is not { IsAbsoluteUri: true })
        {
            throw new ArgumentException("ServiceUrl must be an absolute URL.", nameof(options));
        }

        _http = http;
        _options = options;
        _sessions = new CredentialRefresher(options.CredentialStore ?? new InMemoryCredentialStore(), options.TimeProvider);
        _sessionKey = $"bluesky:{options.ServiceUrl.Host}:{options.Identifier.Trim().ToUpperInvariant()}";
    }

    /// <inheritdoc />
    protected override void PrepareCore(Message message, Recipient recipient, ICollection<PreparationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(issues);
        if (!recipient.IsSelf)
        {
            issues.Add(new("recipient-must-be-self", "Bluesky posts to the account's own feed; use Recipient.Self."));
        }
    }

    /// <inheritdoc />
    /// <remarks>With <see cref="BlueskyChannelOptions.ThreadLongPosts"/>, each post of the thread is counted on its own, so the longest one is measured.</remarks>
    protected override string CountedText(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var text = RichText.From(message).Text;
        return _options.ThreadLongPosts ? TextFit.Split(text, Manifest.TextLimit).MaxBy(p => Manifest.TextLimit.Counter.Count(p)) ?? text : text;
    }

    /// <inheritdoc />
    protected override async Task<DeliveryOutcome> SendOnceAsync(Message message, Recipient recipient, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var parts = Parts(RichText.From(message));

        StoredCredential session;
        try
        {
            session = await _sessions.GetValidAsync(_sessionKey, RefreshAsync, cancellationToken).ConfigureAwait(false);
        }
        catch (HulakiDeliveryException ex)
        {
            return ex.Outcome;
        }

        StrongRef? root = null;
        StrongRef? parent = null;
        for (var i = 0; i < parts.Count; i++)
        {
            var reply = root is null ? null : new ReplyRef { Root = root, Parent = parent! };
            PostResult result;
            if (i == 0)
            {
                // Nothing is posted yet: connection failures and timeouts go to ChannelBase, which
                // knows which of them are safe to retry (ADR-0005).
                result = await PostAsync(parts[i], reply, session, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                try
                {
                    result = await PostAsync(parts[i], reply, session, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
                {
                    // Earlier parts are live. A retry by ChannelBase would post them again, so this
                    // one case is caught here and reported as a partial thread.
                    return Partial(i, parts.Count);
                }
            }

            if (result.Failure is { } failure)
            {
                return i == 0 ? failure : Partial(i, parts.Count);
            }

            session = result.Session;
            root ??= result.Posted;
            parent = result.Posted;
        }

        return DeliveryOutcome.Delivered(root!.Uri, WebUrl(root.Uri!));
    }

    internal static Uri? WebUrl(string atUri)
    {
        // at://did:plc:abc/app.bsky.feed.post/3kxyz -> https://bsky.app/profile/did:plc:abc/post/3kxyz
        var match = PostUri().Match(atUri);
        return match.Success
            ? new Uri($"https://bsky.app/profile/{match.Groups["repo"].Value}/post/{match.Groups["rkey"].Value}")
            : null;
    }

    /// <summary>
    /// The access token's expiry from its <c>exp</c> claim. The token is not verified: the server
    /// does that, and a wrong guess only moves the refresh earlier or later.
    /// </summary>
    internal static DateTimeOffset? ExpiryOf(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3 || !System.Buffers.Text.Base64Url.IsValid(parts[1]))
        {
            return null;
        }

        try
        {
            using var claims = JsonDocument.Parse(System.Buffers.Text.Base64Url.DecodeFromChars(parts[1]));
            return claims.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private List<RichText> Parts(RichText whole)
    {
        if (!_options.ThreadLongPosts || Manifest.TextLimit.Fits(whole.Text))
        {
            return [whole];
        }

        // TextFit.Split returns substrings of the text in order, so each part is found after the last.
        var parts = new List<RichText>();
        var cursor = 0;
        foreach (var part in TextFit.Split(whole.Text, Manifest.TextLimit))
        {
            var start = whole.Text.IndexOf(part, cursor, StringComparison.Ordinal);
            parts.Add(whole.Slice(start, part.Length));
            cursor = start + part.Length;
        }

        return parts;
    }

    private async Task<PostResult> PostAsync(RichText part, ReplyRef? reply, StoredCredential session, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var body = new CreateRecordRequest
            {
                Repo = session.Extra["did"],
                Record = new PostRecord
                {
                    Text = part.Text,
                    CreatedAt = Options.TimeProvider.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
                    Langs = _options.Languages.Count == 0 ? null : [.. _options.Languages],
                    Facets = part.Facets(),
                    Reply = reply,
                },
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, Xrpc("com.atproto.repo.createRecord"))
            {
                Content = JsonContent.Create(body, BlueskyJsonContext.Default.CreateRecordRequest),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var posted = await ReadAsync(response, BlueskyJsonContext.Default.StrongRef, cancellationToken).ConfigureAwait(false);
                return posted is { Uri.Length: > 0, Cid.Length: > 0 }
                    ? new PostResult(null, posted, session)
                    : new PostResult(DeliveryOutcome.Unknown(new HulakiError(HulakiErrorCode.UpstreamFailure, RetryDisposition.ReconcileFirst, "Bluesky answered 2xx without the post's uri and cid.") { HttpStatus = (int)response.StatusCode }), null, session);
            }

            var error = await ErrorNameAsync(response, cancellationToken).ConfigureAwait(false);
            if (!IsExpiredSession(response, error))
            {
                return new PostResult(HttpHelpers.OutcomeFor(MapError(response, error)), null, session);
            }

            if (attempt > 0)
            {
                return new PostResult(DeliveryOutcome.Failed(new HulakiError(HulakiErrorCode.ReconnectRequired, RetryDisposition.AfterReconnect, "Bluesky refused a freshly refreshed session.") { HttpStatus = (int)response.StatusCode, PlatformCode = error }), null, session);
            }

            try
            {
                session = await _sessions.RefreshAfterRejectionAsync(_sessionKey, session.Revision, RefreshAsync, cancellationToken).ConfigureAwait(false);
            }
            catch (HulakiDeliveryException ex)
            {
                return new PostResult(ex.Outcome, null, session);
            }
        }
    }

    /// <summary>Refreshes with the refresh token when there is one, else creates a session with the app password.</summary>
    private async ValueTask<StoredCredential> RefreshAsync(StoredCredential? current, CancellationToken cancellationToken)
    {
        if (current?.RefreshToken is { } refreshToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Xrpc("com.atproto.server.refreshSession"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshToken);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return await SessionFromAsync(response, cancellationToken).ConfigureAwait(false);
            }

            var error = await ErrorNameAsync(response, cancellationToken).ConfigureAwait(false);
            if (!IsExpiredSession(response, error))
            {
                throw new HulakiDeliveryException(HttpHelpers.OutcomeFor(MapError(response, error)));
            }

            // The refresh token expired or was revoked: start over with the app password.
        }

        var login = new CreateSessionRequest { Identifier = _options.Identifier, Password = _options.AppPassword };
        using var created = await _http.PostAsJsonAsync(Xrpc("com.atproto.server.createSession"), login, BlueskyJsonContext.Default.CreateSessionRequest, cancellationToken).ConfigureAwait(false);
        if (created.IsSuccessStatusCode)
        {
            return await SessionFromAsync(created, cancellationToken).ConfigureAwait(false);
        }

        var loginError = await ErrorNameAsync(created, cancellationToken).ConfigureAwait(false);
        var failure = (int)created.StatusCode == 401
            ? new HulakiError(HulakiErrorCode.ReconnectRequired, RetryDisposition.AfterReconnect, "Bluesky refused the identifier or app password.") { HttpStatus = 401, PlatformCode = loginError }
            : MapError(created, loginError);
        throw new HulakiDeliveryException(HttpHelpers.OutcomeFor(failure));
    }

    private async Task<StoredCredential> SessionFromAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var session = await ReadAsync(response, BlueskyJsonContext.Default.SessionResponse, cancellationToken).ConfigureAwait(false);
        if (session is not { AccessJwt.Length: > 0, Did.Length: > 0 })
        {
            throw new HulakiDeliveryException(DeliveryOutcome.Failed(new HulakiError(HulakiErrorCode.UpstreamFailure, RetryDisposition.AfterDelay, "Bluesky answered 2xx without a session.")));
        }

        // Without an exp claim, treat the token as expiring in 10 minutes: refreshed soon, never trusted for hours.
        var expires = ExpiryOf(session.AccessJwt) ?? Options.TimeProvider.GetUtcNow().AddMinutes(10);
        return new StoredCredential(1, session.AccessJwt, session.RefreshJwt, expires, new Dictionary<string, string> { ["did"] = session.Did });
    }

    private Uri Xrpc(string method) => new(_options.ServiceUrl, "xrpc/" + method);

    private static bool IsExpiredSession(HttpResponseMessage response, string? error) =>
        error is "ExpiredToken" or "InvalidToken" || (response.StatusCode == HttpStatusCode.Unauthorized && error is null or "AuthenticationRequired");

    private static DeliveryOutcome Partial(int posted, int total) =>
        DeliveryOutcome.Failed(new HulakiError(
            HulakiErrorCode.UpstreamFailure,
            RetryDisposition.ReconcileFirst,
            $"Posted {posted} of {total} thread parts, then the next one failed. Resending would repeat the posted parts."));

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync(type, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null; // a proxy's HTML error page or an empty body; the status code decides
        }
    }

    // XRPC error names are identifiers such as "ExpiredToken"; "message" is prose and is dropped.
    private static async Task<string?> ErrorNameAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await ReadAsync(response, BlueskyJsonContext.Default.XrpcError, cancellationToken).ConfigureAwait(false))?.Error is { } name && ErrorName().IsMatch(name) ? name : null;

    private HulakiError MapError(HttpResponseMessage response, string? error)
    {
        var status = (int)response.StatusCode;
        var retryAfter = RateLimitReset(response) ?? HttpHelpers.RetryAfter(response, Options.TimeProvider);

        HulakiError Error(HulakiErrorCode code, RetryDisposition retry, string text) =>
            new(code, retry, text) { HttpStatus = status, PlatformCode = error, RetryAfter = retryAfter };

        return status switch
        {
            400 => Error(HulakiErrorCode.InvalidInput, RetryDisposition.Never, "Bluesky refused the post."),
            403 => Error(HulakiErrorCode.PermissionDenied, RetryDisposition.Never, "Bluesky does not allow this account to post."),
            429 => Error(HulakiErrorCode.RateLimited, RetryDisposition.AfterDelay, "Bluesky rate limited the account."),
            _ => HttpHelpers.ErrorFor(response.StatusCode, retryAfter, $"Bluesky returned {status}.") with { PlatformCode = error },
        };
    }

    /// <summary><c>ratelimit-reset</c> is the Unix time, in seconds, when the window reopens (docs.bsky.app, rate limits).</summary>
    private TimeSpan? RateLimitReset(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("ratelimit-reset", out var values)
            || !long.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var reset))
        {
            return null;
        }

        var wait = DateTimeOffset.FromUnixTimeSeconds(reset) - Options.TimeProvider.GetUtcNow();
        return wait > TimeSpan.Zero ? wait : null;
    }

    // 5,000 points per hour per account, a post costs 3 (docs.bsky.app, rate limits): about 1,666
    // posts an hour. 27 a minute stays under that. A thread takes one token for all its parts.
    private static RateLimiter DefaultLimiter() => new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 27,
        TokensPerPeriod = 27,
        ReplenishmentPeriod = TimeSpan.FromMinutes(1),
        QueueLimit = 100,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    });

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorName();

    [GeneratedRegex("^at://(?<repo>[^/]+)/app\\.bsky\\.feed\\.post/(?<rkey>[A-Za-z0-9._:~-]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex PostUri();

    private readonly record struct PostResult(DeliveryOutcome? Failure, StrongRef? Posted, StoredCredential Session);
}

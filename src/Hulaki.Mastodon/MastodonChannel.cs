using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Hulaki.Channels;
using Hulaki.Idempotency;
using Hulaki.Markup;
using Hulaki.Mastodon.Wire;
using Hulaki.Text;

namespace Hulaki.Mastodon;

/// <summary>
/// Posts a status to the account's Mastodon timeline with <c>POST /api/v1/statuses</c>. The
/// recipient is <see cref="Recipient.Self"/>. Every request carries an <c>Idempotency-Key</c>, the
/// message's key or else its fingerprint, and Mastodon returns the first status for a repeated key,
/// so a retry after a lost answer cannot publish twice.
/// </summary>
public sealed class MastodonChannel : ChannelBase
{
    private const int DefaultMaxCharacters = 500;

    private static readonly CapabilityDeclaration[] Declarations =
    [
        new(Capability.Text, Availability.UnknownUntilRequest, "500 characters unless the instance allows more (MaxCharacters); URLs count as 23"),
        new(Capability.Markup, Availability.UnsupportedByPlatform, "Sent as plain text; the instance links URLs"),
        new(Capability.Title, Availability.UnsupportedByPlatform, "Sent as the first line"),
        new(Capability.Priority, Availability.UnsupportedByPlatform),
        new(Capability.ClickAction, Availability.UnsupportedByPlatform, "The link is appended"),
        new(Capability.Images, Availability.NotImplemented),
        new(Capability.IdempotentSend, Availability.Available, "Idempotency-Key header"),
        new(Capability.Delete, Availability.NotImplemented),
    ];

    private readonly HttpClient _http;
    private readonly MastodonChannelOptions _options;
    private readonly Uri _instance;
    private int? _instanceLimit;

    /// <summary>The default: 500 characters, the limit of a Mastodon instance that has not changed it.</summary>
    public static CapabilityManifest Manifest { get; } = ManifestFor(DefaultMaxCharacters);

    /// <summary>Creates the channel.</summary>
    /// <param name="name">Registration name.</param>
    /// <param name="http">Client for the instance. The channel does not dispose it.</param>
    /// <param name="options">Options with the instance URL and an access token.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> or the access token is empty, the instance URL is missing or not absolute, or <see cref="MastodonChannelOptions.MaxCharacters"/> is below 1.</exception>
    public MastodonChannel(string name, HttpClient http, MastodonChannelOptions options)
        : base(name, ManifestFor(options), options, DefaultLimiter)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AccessToken, "options.AccessToken");
        if (options.InstanceUrl is not { IsAbsoluteUri: true } instance)
        {
            throw new ArgumentException("InstanceUrl must be an absolute URL.", nameof(options));
        }

        _http = http;
        _options = options;
        _instance = instance;
        // A configured limit is trusted; only the default is checked against the instance.
        _instanceLimit = options.MaxCharacters;
    }

    /// <inheritdoc />
    protected override void PrepareCore(Message message, Recipient recipient, ICollection<PreparationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(issues);
        if (!recipient.IsSelf)
        {
            issues.Add(new("recipient-must-be-self", "Mastodon posts to the account's own timeline; use Recipient.Self."));
        }
    }

    /// <inheritdoc />
    protected override async Task<DeliveryOutcome> SendOnceAsync(Message message, Recipient recipient, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var text = Render(message);
        var limit = _instanceLimit ?? await InstanceLimitAsync(cancellationToken).ConfigureAwait(false);
        if (MastodonTextCounter.Instance.Count(text) > limit)
        {
            return DeliveryOutcome.Failed(new HulakiError(HulakiErrorCode.InvalidInput, RetryDisposition.Never, $"Text is over this instance's {limit} characters."));
        }

        var body = new StatusRequest { Status = text, Visibility = _options.Visibility, Language = _options.Language };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_instance, "api/v1/statuses"))
        {
            Content = JsonContent.Create(body, MastodonJsonContext.Default.StatusRequest),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", IdempotencyKeyFor(message));

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            var status = await ReadAsync(response, MastodonJsonContext.Default.StatusResponse, cancellationToken).ConfigureAwait(false);
            if (status?.Id is { Length: > 0 } id)
            {
                return DeliveryOutcome.Delivered(id, Uri.TryCreate(status.Url, UriKind.Absolute, out var url) ? url : null);
            }

            // The key makes a resend safe, so this is a retryable failure rather than Unknown.
            return DeliveryOutcome.Failed(new HulakiError(HulakiErrorCode.UpstreamFailure, RetryDisposition.AfterDelay, "Mastodon answered 2xx without a status.") { HttpStatus = (int)response.StatusCode });
        }

        return HttpHelpers.OutcomeFor(MapError(response));
    }

    /// <inheritdoc />
    protected override string CountedText(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Render(message);
    }

    /// <summary>The caller's key, or the fingerprint, so an unkeyed message still deduplicates across retries.</summary>
    internal static string IdempotencyKeyFor(Message message) => message.IdempotencyKey ?? MessageFingerprint.Compute(message);

    internal static string Render(Message message)
    {
        var text = message.Format == TextFormat.Markup ? MarkupDocument.Parse(message.Text).ToPlainText() : message.Text;
        var title = message.Title is null ? string.Empty : message.Title + "\n";
        var link = message.Link is null ? string.Empty : "\n" + message.Link.AbsoluteUri;
        return title + text + link;
    }

    /// <summary>
    /// Reads <c>configuration.statuses.max_characters</c>. An error answer or a missing field keeps
    /// the default for this send and asks again next time; a network failure goes to ChannelBase.
    /// </summary>
    private async Task<int> InstanceLimitAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(new Uri(_instance, "api/v2/instance"), cancellationToken).ConfigureAwait(false);
        var instance = response.IsSuccessStatusCode
            ? await ReadAsync(response, MastodonJsonContext.Default.InstanceResponse, cancellationToken).ConfigureAwait(false)
            : null;
        if (instance?.Configuration?.Statuses?.MaxCharacters is { } max and > 0)
        {
            _instanceLimit = max;
            return max;
        }

        return DefaultMaxCharacters;
    }

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

    private HulakiError MapError(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        var retryAfter = RateLimitReset(response) ?? HttpHelpers.RetryAfter(response, Options.TimeProvider);

        HulakiError Error(HulakiErrorCode code, RetryDisposition retry, string text) =>
            new(code, retry, text) { HttpStatus = status, RetryAfter = retryAfter };

        // Mastodon's "error" is prose that can quote the status; none of it is kept.
        return status switch
        {
            401 => Error(HulakiErrorCode.ReconnectRequired, RetryDisposition.AfterReconnect, "The access token is invalid or was revoked."),
            403 => Error(HulakiErrorCode.PermissionDenied, RetryDisposition.Never, "The token lacks write:statuses, or the account may not post."),
            404 => Error(HulakiErrorCode.InvalidConfiguration, RetryDisposition.Never, "The instance has no statuses API at this URL."),
            422 => Error(HulakiErrorCode.InvalidInput, RetryDisposition.Never, "Mastodon refused the status."),
            429 => Error(HulakiErrorCode.RateLimited, RetryDisposition.AfterDelay, "Mastodon rate limited the account."),
            _ => HttpHelpers.ErrorFor(response.StatusCode, retryAfter, $"Mastodon returned {status}."),
        };
    }

    /// <summary><c>X-RateLimit-Reset</c> is an ISO 8601 timestamp (docs.joinmastodon.org/api/rate-limits).</summary>
    private TimeSpan? RateLimitReset(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-RateLimit-Reset", out var values)
            || !DateTimeOffset.TryParse(values.FirstOrDefault(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var reset))
        {
            return null;
        }

        var wait = reset - Options.TimeProvider.GetUtcNow();
        return wait > TimeSpan.Zero ? wait : null;
    }

    private static CapabilityManifest ManifestFor(MastodonChannelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxCharacters is < 1)
        {
            throw new ArgumentException("MaxCharacters must be at least 1.", nameof(options));
        }

        return options.MaxCharacters is { } max && max != DefaultMaxCharacters ? ManifestFor(max) : Manifest;
    }

    private static CapabilityManifest ManifestFor(int maxCharacters) =>
        new("mastodon", new TextLimit(maxCharacters, MastodonTextCounter.Instance), Declarations);

    // Mastodon allows 300 status posts per account per 3 hours (docs.joinmastodon.org/api/rate-limits).
    private static RateLimiter DefaultLimiter() => new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 300,
        TokensPerPeriod = 100,
        ReplenishmentPeriod = TimeSpan.FromHours(1),
        QueueLimit = 100,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    });
}

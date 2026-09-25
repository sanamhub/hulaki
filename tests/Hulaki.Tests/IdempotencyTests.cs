using System;
using System.Linq;
using System.Threading.Tasks;
using Hulaki.Idempotency;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Hulaki.Tests;

public sealed class MessageFingerprintTests
{
    private static readonly Message Base = new("Orange rain warning") { IdempotencyKey = "k1" };

    [Fact]
    public void Equal_content_gives_an_equal_fingerprint()
    {
        Assert.Equal(MessageFingerprint.Compute(Base), MessageFingerprint.Compute(new Message("Orange rain warning")));
    }

    [Fact]
    public void The_idempotency_key_is_ignored()
    {
        Assert.Equal(MessageFingerprint.Compute(Base), MessageFingerprint.Compute(Base with { IdempotencyKey = "k2" }));
    }

    public static TheoryData<string> Changes() =>
        ["text", "format", "title", "priority", "link", "media fingerprint", "media alt text"];

    [Theory]
    [MemberData(nameof(Changes))]
    public void Each_visible_field_changes_the_fingerprint(string field)
    {
        var withMedia = Base with { Media = [Png(1)] };
        var (before, after) = field switch
        {
            "text" => (Base, Base with { Text = "Red rain warning" }),
            "format" => (Base, Base with { Format = TextFormat.Markup }),
            "title" => (Base, Base with { Title = "DHM" }),
            "priority" => (Base, Base with { Priority = MessagePriority.Urgent }),
            "link" => (Base, Base with { Link = new Uri("https://example.com/warning") }),
            "media fingerprint" => (withMedia, Base with { Media = [Png(2)] }),
            "media alt text" => (withMedia, Base with { Media = [WithAltText(Png(1), "Rain map")] }),
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        Assert.NotEqual(MessageFingerprint.Compute(before), MessageFingerprint.Compute(after));
    }

    private static MediaAttachment Png(byte seed) => MediaAttachment.FromBytes(new byte[] { seed, 2, 3 }, "image/png");

    // AltText is an init accessor on a class built only by factories, so no caller can set it yet.
    // Reflection reaches the setter; the gap itself is reported to the maintainer.
    private static MediaAttachment WithAltText(MediaAttachment media, string altText)
    {
        typeof(MediaAttachment).GetProperty(nameof(MediaAttachment.AltText))!.SetValue(media, altText);
        return media;
    }
}

public sealed class InMemoryIdempotencyStoreTests
{
    private const string Scope = "tenant-a";

    [Fact]
    public async Task Claims_are_new_then_existing_with_saved_outcomes_then_conflict()
    {
        var store = new InMemoryIdempotencyStore(new FakeTimeProvider());
        var ct = TestContext.Current.CancellationToken;

        var first = await store.ClaimAsync(Scope, "k", "fp1", ct);
        await store.SaveOutcomeAsync(Scope, "k", "target-1", DeliveryOutcome.Delivered("42"), ct);
        var second = await store.ClaimAsync(Scope, "k", "fp1", ct);
        var third = await store.ClaimAsync(Scope, "k", "fp2", ct);

        Assert.Equal(ClaimStatus.New, first.Status);
        Assert.Empty(first.Outcomes);
        Assert.Equal(ClaimStatus.Existing, second.Status);
        Assert.Equal("42", second.Outcomes["target-1"].PlatformMessageId);
        Assert.Equal(ClaimStatus.Conflict, third.Status);
        Assert.Empty(third.Outcomes);
    }

    [Fact]
    public async Task Scopes_do_not_share_keys()
    {
        var store = new InMemoryIdempotencyStore(new FakeTimeProvider());
        var ct = TestContext.Current.CancellationToken;

        await store.ClaimAsync("tenant-a", "k", "fp1", ct);
        var other = await store.ClaimAsync("tenant-b", "k", "fp2", ct);

        Assert.Equal(ClaimStatus.New, other.Status);
    }

    [Fact]
    public async Task Entries_expire_after_the_retention()
    {
        var time = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(time) { Retention = TimeSpan.FromHours(1) };
        var ct = TestContext.Current.CancellationToken;

        await store.ClaimAsync(Scope, "k", "fp1", ct);
        time.Advance(TimeSpan.FromMinutes(59));
        var within = await store.ClaimAsync(Scope, "k", "fp1", ct);
        time.Advance(TimeSpan.FromMinutes(2));
        var after = await store.ClaimAsync(Scope, "k", "fp2", ct);

        Assert.Equal(ClaimStatus.Existing, within.Status);
        Assert.Equal(ClaimStatus.New, after.Status);
    }

    [Fact]
    public async Task Not_submitted_outcomes_are_not_stored()
    {
        var store = new InMemoryIdempotencyStore(new FakeTimeProvider());
        var ct = TestContext.Current.CancellationToken;

        await store.ClaimAsync(Scope, "k", "fp1", ct);
        await store.SaveOutcomeAsync(Scope, "k", "target-1", DeliveryOutcome.NotSubmitted([new PreparationIssue("text-too-long", "Too long.")]), ct);
        var again = await store.ClaimAsync(Scope, "k", "fp1", ct);

        Assert.Equal(ClaimStatus.Existing, again.Status);
        Assert.Empty(again.Outcomes);
    }

    [Fact]
    public async Task Fifty_concurrent_claims_yield_exactly_one_new()
    {
        var store = new InMemoryIdempotencyStore(new FakeTimeProvider());
        var ct = TestContext.Current.CancellationToken;

        var claims = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(_ => Task.Run(async () => await store.ClaimAsync(Scope, "k", "fp1", ct), ct)));

        Assert.Equal(1, claims.Count(c => c.Status == ClaimStatus.New));
        Assert.Equal(49, claims.Count(c => c.Status == ClaimStatus.Existing));
    }

    [Fact]
    public async Task Stored_outcomes_are_a_snapshot()
    {
        var store = new InMemoryIdempotencyStore(new FakeTimeProvider());
        var ct = TestContext.Current.CancellationToken;

        await store.ClaimAsync(Scope, "k", "fp1", ct);
        var existing = await store.ClaimAsync(Scope, "k", "fp1", ct);
        await store.SaveOutcomeAsync(Scope, "k", "target-1", DeliveryOutcome.Delivered(), ct);

        Assert.Empty(existing.Outcomes);
    }
}

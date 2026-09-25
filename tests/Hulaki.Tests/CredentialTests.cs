using System;
using System.Threading;
using System.Threading.Tasks;
using Hulaki.Credentials;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Hulaki.Tests;

public sealed class StoredCredentialTests
{
    private const string Access = "TEST-access_0000000000000000000";
    private const string Refresh = "TEST-refresh_0000000000000000000";

    [Fact]
    public void To_string_never_contains_either_token()
    {
        var credential = new StoredCredential(3, Access, Refresh, new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));

        var text = credential.ToString();

        Assert.DoesNotContain(Access, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Refresh, text, StringComparison.Ordinal);
        Assert.Equal("StoredCredential(rev=3, expires=2026-09-25T10:00:00.0000000+00:00)", text);
        Assert.Equal("StoredCredential(rev=1, expires=none)", new StoredCredential(1, Access).ToString());
    }

    [Fact]
    public void Revision_below_one_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StoredCredential(0, Access));
    }
}

public sealed class InMemoryCredentialStoreTests
{
    [Fact]
    public async Task Compare_and_set_needs_the_expected_revision()
    {
        var store = new InMemoryCredentialStore();
        var ct = TestContext.Current.CancellationToken;

        Assert.False(await store.CompareAndSetAsync("k", 1, new StoredCredential(2, "TEST-a"), ct));
        Assert.True(await store.CompareAndSetAsync("k", 0, new StoredCredential(1, "TEST-a"), ct));
        Assert.False(await store.CompareAndSetAsync("k", 0, new StoredCredential(1, "TEST-b"), ct));
        Assert.True(await store.CompareAndSetAsync("k", 1, new StoredCredential(2, "TEST-c"), ct));
        Assert.Equal("TEST-c", (await store.GetAsync("k", ct))!.AccessToken);
    }

    [Fact]
    public async Task New_revision_must_be_greater_than_the_expected_one()
    {
        var store = new InMemoryCredentialStore();

        await Assert.ThrowsAsync<ArgumentException>(async () => await store.CompareAndSetAsync("k", 1, new StoredCredential(1, "TEST-a"), TestContext.Current.CancellationToken));
    }
}

public sealed class CredentialRefresherTests
{
    [Fact]
    public async Task Concurrent_callers_refresh_once_in_total()
    {
        var store = new InMemoryCredentialStore();
        var time = new FakeTimeProvider();
        var calls = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async ValueTask<StoredCredential> Refresh(StoredCredential? current, CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            await gate.Task.WaitAsync(ct);
            return new StoredCredential(1, "TEST-new", expiresAt: time.GetUtcNow().AddHours(1));
        }

        // Two refreshers over one store stand for two channels in one process.
        var ct = TestContext.Current.CancellationToken;
        var first = new CredentialRefresher(store, time).GetValidAsync("k", Refresh, ct).AsTask();
        var second = new CredentialRefresher(store, time).GetValidAsync("k", Refresh, ct).AsTask();
        gate.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, calls);
        Assert.All(results, r => Assert.Equal("TEST-new", r.AccessToken));
        Assert.All(results, r => Assert.Equal(1, r.Revision));
    }

    [Fact]
    public async Task A_lost_compare_and_set_returns_the_winners_token()
    {
        var inner = new InMemoryCredentialStore();
        var ct = TestContext.Current.CancellationToken;
        await inner.CompareAndSetAsync("k", 0, new StoredCredential(1, "TEST-old", "TEST-refresh-old", DateTimeOffset.UnixEpoch), ct);
        var store = new RacingStore(inner, new StoredCredential(2, "TEST-winner"));
        var refresher = new CredentialRefresher(store, new FakeTimeProvider());

        var result = await refresher.GetValidAsync("k", (_, _) => ValueTask.FromResult(new StoredCredential(1, "TEST-loser")), ct);

        Assert.Equal("TEST-winner", result.AccessToken);
        Assert.Equal("TEST-winner", (await inner.GetAsync("k", ct))!.AccessToken);
    }

    [Fact]
    public async Task Expiry_inside_the_margin_triggers_a_refresh()
    {
        var store = new InMemoryCredentialStore();
        var time = new FakeTimeProvider();
        var ct = TestContext.Current.CancellationToken;
        await store.CompareAndSetAsync("k", 0, new StoredCredential(1, "TEST-first", expiresAt: time.GetUtcNow().AddMinutes(10)), ct);
        var refresher = new CredentialRefresher(store, time);
        var calls = 0;
        ValueTask<StoredCredential> Refresh(StoredCredential? current, CancellationToken _)
        {
            calls++;
            return ValueTask.FromResult(new StoredCredential(1, "TEST-second", expiresAt: time.GetUtcNow().AddHours(1)));
        }

        var early = await refresher.GetValidAsync("k", Refresh, ct);
        time.Advance(TimeSpan.FromMinutes(6));
        var late = await refresher.GetValidAsync("k", Refresh, ct);

        Assert.Equal("TEST-first", early.AccessToken);
        Assert.Equal("TEST-second", late.AccessToken);
        Assert.Equal(2, late.Revision);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Missing_credential_is_created_by_the_refresh()
    {
        var store = new InMemoryCredentialStore();
        var ct = TestContext.Current.CancellationToken;

        var result = await new CredentialRefresher(store, new FakeTimeProvider())
            .GetValidAsync("k", (current, _) => ValueTask.FromResult(new StoredCredential(1, current is null ? "TEST-created" : "TEST-wrong")), ct);

        Assert.Equal("TEST-created", result.AccessToken);
        Assert.Equal(1, result.Revision);
    }

    [Fact]
    public async Task Rejection_refreshes_once_and_a_later_rejection_of_the_same_revision_does_not()
    {
        var store = new InMemoryCredentialStore();
        var ct = TestContext.Current.CancellationToken;
        await store.CompareAndSetAsync("k", 0, new StoredCredential(1, "TEST-first"), ct);
        var refresher = new CredentialRefresher(store, new FakeTimeProvider());
        var calls = 0;
        ValueTask<StoredCredential> Refresh(StoredCredential? current, CancellationToken _)
        {
            calls++;
            return ValueTask.FromResult(new StoredCredential(1, "TEST-second"));
        }

        var afterFirst = await refresher.RefreshAfterRejectionAsync("k", 1, Refresh, ct);
        var afterSecond = await refresher.RefreshAfterRejectionAsync("k", 1, Refresh, ct);

        Assert.Equal("TEST-second", afterFirst.AccessToken);
        Assert.Same(afterFirst, afterSecond);
        Assert.Equal(1, calls);
    }

    /// <summary>Lets another writer win the race just before this process's compare-and-set.</summary>
    private sealed class RacingStore(InMemoryCredentialStore inner, StoredCredential winner) : ICredentialStore
    {
        public ValueTask<StoredCredential?> GetAsync(string key, CancellationToken cancellationToken) => inner.GetAsync(key, cancellationToken);

        public async ValueTask<bool> CompareAndSetAsync(string key, long expectedRevision, StoredCredential value, CancellationToken cancellationToken)
        {
            Assert.True(await inner.CompareAndSetAsync(key, expectedRevision, winner, cancellationToken));
            return await inner.CompareAndSetAsync(key, expectedRevision, value, cancellationToken);
        }
    }
}

using AskLucy.Domain.Notifications;
using AskLucy.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Tests.Notifications;

/// <summary>
/// specs/067 research R4 against a real database: the per-row conditional update is what keeps two
/// dispatchers off the same event. Other tests in the collection may leave outbox rows behind, so
/// every assertion is scoped to the events this test seeded.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class NotificationOutboxClaimTests(PersistenceTestFixture fixture)
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ClaimBatchAsync_ConcurrentClaimers_NeverClaimTheSameEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new SettableTimeProvider(DateTime.UtcNow);
        var seeded = await SeedAsync(20, clock.Now, ct);

        await using var first = fixture.CreateDbContext();
        await using var second = fixture.CreateDbContext();
        var leaseExpires = clock.Now + Lease;

        var claims = await Task.WhenAll(
            new NotificationOutboxStore(first, clock).ClaimBatchAsync("worker-a", leaseExpires, 1000, ct),
            new NotificationOutboxStore(second, clock).ClaimBatchAsync("worker-b", leaseExpires, 1000, ct));

        var mineA = claims[0].Intersect(seeded).ToList();
        var mineB = claims[1].Intersect(seeded).ToList();
        mineA.Intersect(mineB).Should().BeEmpty();
        mineA.Concat(mineB).Should().BeEquivalentTo(seeded);

        await using var verify = fixture.CreateDbContext();
        var rows = await verify.NotificationOutboxEvents.AsNoTracking().Where(e => seeded.Contains(e.Id)).ToListAsync(ct);
        rows.Should().OnlyContain(e => e.Status == OutboxEventStatus.Processing && e.Attempts == 1);
        rows.Where(e => mineA.Contains(e.Id)).Should().OnlyContain(e => e.LeaseOwner == "worker-a");
        rows.Where(e => mineB.Contains(e.Id)).Should().OnlyContain(e => e.LeaseOwner == "worker-b");
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ClaimBatchAsync_LeaseStillHeld_IsNotReclaimed_ButAnExpiredLeaseIs()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new SettableTimeProvider(DateTime.UtcNow);
        var seeded = await SeedAsync(1, clock.Now, ct);

        await using (var context = fixture.CreateDbContext())
        {
            (await new NotificationOutboxStore(context, clock).ClaimBatchAsync("worker-a", clock.Now + Lease, 1000, ct))
                .Should().Contain(seeded);
        }

        await using (var context = fixture.CreateDbContext())
        {
            (await new NotificationOutboxStore(context, clock).ClaimBatchAsync("worker-b", clock.Now + Lease, 1000, ct))
                .Should().NotContain(seeded, "worker-a still holds the lease");
        }

        clock.Now += Lease + TimeSpan.FromSeconds(1);

        await using (var context = fixture.CreateDbContext())
        {
            (await new NotificationOutboxStore(context, clock).ClaimBatchAsync("worker-b", clock.Now + Lease, 1000, ct))
                .Should().Contain(seeded, "a crashed worker's expired lease is claimable again");
        }

        await using var verify = fixture.CreateDbContext();
        var row = await verify.NotificationOutboxEvents.AsNoTracking().SingleAsync(e => e.Id == seeded[0], ct);
        row.LeaseOwner.Should().Be("worker-b");
        row.Attempts.Should().Be(2);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task GetClaimedAsync_RequiresTheClaimingWorkersUnexpiredLease()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new SettableTimeProvider(DateTime.UtcNow);
        var id = (await SeedAsync(1, clock.Now, ct))[0];

        await using (var context = fixture.CreateDbContext())
        {
            await new NotificationOutboxStore(context, clock).ClaimBatchAsync("worker-a", clock.Now + Lease, 1000, ct);
        }

        await using (var context = fixture.CreateDbContext())
        {
            var store = new NotificationOutboxStore(context, clock);
            (await store.GetClaimedAsync(id, "worker-b", ct)).Should().BeNull("only the lease holder may complete the event");

            var claimed = await store.GetClaimedAsync(id, "worker-a", ct);
            claimed.Should().NotBeNull();
            claimed!.Complete(OutboxEventOutcome.Materialized, clock.Now);
            await context.SaveChangesAsync(ct);
        }

        await using (var verify = fixture.CreateDbContext())
        {
            var row = await verify.NotificationOutboxEvents.AsNoTracking().SingleAsync(e => e.Id == id, ct);
            row.Status.Should().Be(OutboxEventStatus.Completed);
            row.LeaseOwner.Should().BeNull();
        }

        var expired = (await SeedAsync(1, clock.Now, ct))[0];
        await using (var context = fixture.CreateDbContext())
        {
            await new NotificationOutboxStore(context, clock).ClaimBatchAsync("worker-a", clock.Now + Lease, 1000, ct);
        }

        clock.Now += Lease + TimeSpan.FromSeconds(1);
        await using (var context = fixture.CreateDbContext())
        {
            (await new NotificationOutboxStore(context, clock).GetClaimedAsync(expired, "worker-a", ct))
                .Should().BeNull("an expired lease no longer entitles the worker to finish the event");
        }
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ReleaseClaimsAsync_HandsTheWorkersRowsBack_WithoutCountingTheAttempt()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new SettableTimeProvider(DateTime.UtcNow);
        var seeded = await SeedAsync(2, clock.Now, ct);

        await using (var context = fixture.CreateDbContext())
        {
            var store = new NotificationOutboxStore(context, clock);
            await store.ClaimBatchAsync("worker-a", clock.Now + Lease, 1000, ct);
            (await store.ReleaseClaimsAsync("worker-a", ct)).Should().BeGreaterThanOrEqualTo(2);
        }

        await using var verify = fixture.CreateDbContext();
        var rows = await verify.NotificationOutboxEvents.AsNoTracking().Where(e => seeded.Contains(e.Id)).ToListAsync(ct);
        rows.Should().OnlyContain(e =>
            e.Status == OutboxEventStatus.Pending && e.Attempts == 0 && e.LeaseOwner == null && e.LeaseExpiresAtUtc == null);
    }

    private async Task<List<Guid>> SeedAsync(int count, DateTime occurredAtUtc, CancellationToken ct)
    {
        await using var context = fixture.CreateDbContext();
        var events = Enumerable.Range(0, count)
            .Select(i => NotificationOutboxEvent.Create(
                NotificationTypeKeys.DocumentIndexingCompleted,
                """{"kind":"User","userId":"claim-test"}""",
                "{}",
                $"claim-test-{Guid.NewGuid():N}",
                occurredAtUtc,
                eventKey: $"claim-test-{Guid.NewGuid():N}"))
            .ToList();

        context.NotificationOutboxEvents.AddRange(events);
        await context.SaveChangesAsync(ct);
        return [.. events.Select(e => e.Id)];
    }

    private sealed class SettableTimeProvider(DateTime utcNow) : TimeProvider
    {
        public DateTime Now { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }
}

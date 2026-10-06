using AskLucy.Domain.Notifications;
using AskLucy.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Tests.Notifications;

/// <summary>
/// T107 — specs/067 research R4 and R5 against a real database: the per-row conditional update keeps two
/// workers off the same delivery, the sweeper turns an expired <c>Sending</c> lease into
/// <c>Failed(AmbiguousOutcome)</c> without requeueing it, and a claimed-but-unsent delivery is released.
/// Other tests in the collection may leave deliveries behind, so every assertion is scoped to the ids this
/// test seeded, and a claim passes <c>batchSize</c> large enough to take them all.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class DeliveryClaimRaceTests(PersistenceTestFixture fixture)
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    private static readonly NotificationChannel[] Email = [NotificationChannel.Email];

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ClaimDueDeliveriesAsync_ConcurrentClaimers_NeverClaimTheSameDelivery()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var seeded = await SeedAsync(20, now, NotificationPriority.Normal, ct);

        await using var first = fixture.CreateDbContext();
        await using var second = fixture.CreateDbContext();
        var leaseExpires = now + Lease;

        var claims = await Task.WhenAll(
            new NotificationRepository(first).ClaimDueDeliveriesAsync("worker-a", Email, leaseExpires, 1000, now, ct),
            new NotificationRepository(second).ClaimDueDeliveriesAsync("worker-b", Email, leaseExpires, 1000, now, ct));

        var mineA = claims[0].Intersect(seeded).ToList();
        var mineB = claims[1].Intersect(seeded).ToList();
        mineA.Intersect(mineB).Should().BeEmpty("a delivery claimed twice would be sent twice");
        mineA.Concat(mineB).Should().BeEquivalentTo(seeded);

        var rows = await DeliveriesAsync(seeded, ct);
        rows.Should().OnlyContain(d => d.Status == DeliveryStatus.Sending && d.AttemptCount == 1 && d.LastAttemptAtUtc != null);
        rows.Where(d => mineA.Contains(d.Id)).Should().OnlyContain(d => d.LeaseOwner == "worker-a");
        rows.Where(d => mineB.Contains(d.Id)).Should().OnlyContain(d => d.LeaseOwner == "worker-b");
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ClaimDueDeliveriesAsync_FourWorkersRacing_EachDeliveryGoesToExactlyOne()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var seeded = await SeedAsync(40, now, NotificationPriority.High, ct);

        var claims = await Task.WhenAll(Enumerable.Range(0, 4).Select(async i =>
        {
            await using var context = fixture.CreateDbContext();
            return await new NotificationRepository(context).ClaimDueDeliveriesAsync($"worker-{i}", Email, now + Lease, 1000, now, ct);
        }));

        var claimed = claims.SelectMany(c => c).Where(seeded.Contains).ToList();
        claimed.Should().OnlyHaveUniqueItems();
        claimed.Should().BeEquivalentTo(seeded);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ClaimDueDeliveriesAsync_TakesTheHighestPriorityFirst_WhateverTheBacklogDepth()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var normal = await SeedAsync(5, now.AddHours(-1), NotificationPriority.Normal, ct);
        var critical = await SeedAsync(1, now, NotificationPriority.Critical, ct);

        await using var context = fixture.CreateDbContext();
        var claimed = await new NotificationRepository(context).ClaimDueDeliveriesAsync("worker-a", Email, now + Lease, 1, now, ct);

        claimed.Should().ContainSingle().Which.Should().Be(critical[0], "a password reset is never behind older announcements (SC-014)");
        (await DeliveriesAsync(normal, ct)).Should().OnlyContain(d => d.Status == DeliveryStatus.Pending);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ClaimDueDeliveriesAsync_SkipsNotYetDueRetries_AndChannelsWithoutASender()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var notYetDue = await SeedAsync(1, now.AddMinutes(10), NotificationPriority.Normal, ct);
        var due = await SeedAsync(1, now.AddSeconds(-1), NotificationPriority.Normal, ct);

        await using (var context = fixture.CreateDbContext())
        {
            var claimed = await new NotificationRepository(context).ClaimDueDeliveriesAsync("worker-a", Email, now + Lease, 1000, now, ct);
            claimed.Should().Contain(due).And.NotContain(notYetDue);
        }

        await using (var context = fixture.CreateDbContext())
        {
            var none = await new NotificationRepository(context).ClaimDueDeliveriesAsync("worker-a", [], now + Lease, 1000, now, ct);
            none.Should().BeEmpty();
        }
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task GetClaimedDeliveryAsync_ReturnsTheNotificationOnlyToTheClaimingWorker()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var id = (await SeedAsync(1, now, NotificationPriority.Normal, ct))[0];
        await using (var context = fixture.CreateDbContext())
        {
            await new NotificationRepository(context).ClaimDueDeliveriesAsync("worker-a", Email, now + Lease, 1000, now, ct);
        }

        await using var read = fixture.CreateDbContext();
        var repository = new NotificationRepository(read);
        (await repository.GetClaimedDeliveryAsync(id, "worker-b", ct)).Should().BeNull();

        var notification = await repository.GetClaimedDeliveryAsync(id, "worker-a", ct);
        notification.Should().NotBeNull();
        notification!.Deliveries.Should().Contain(d => d.Id == id && d.Status == DeliveryStatus.Sending);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task GetClaimedDeliveryAsync_StillFindsANotificationItsOwnerDeleted_SoTheSendCanBeCancelled()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var id = (await SeedAsync(1, now, NotificationPriority.Normal, ct))[0];
        await using (var context = fixture.CreateDbContext())
        {
            var notification = await context.Notifications.Include(n => n.Deliveries).SingleAsync(n => n.Deliveries.Any(d => d.Id == id), ct);
            notification.DeleteByOwner(notification.RecipientUserId!, now);
            await context.SaveChangesAsync(ct);
        }

        await using (var context = fixture.CreateDbContext())
        {
            await new NotificationRepository(context).ClaimDueDeliveriesAsync("worker-a", Email, now + Lease, 1000, now, ct);
        }

        await using var read = fixture.CreateDbContext();
        var found = await new NotificationRepository(read).GetClaimedDeliveryAsync(id, "worker-a", ct);

        found.Should().NotBeNull();
        found!.DeletedAtUtc.Should().NotBeNull();
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task SweepExpiredLeasesAsync_TurnsAnExpiredSendingLeaseIntoAmbiguousFailure_WithNoRequeue()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var seeded = await SeedAsync(2, now, NotificationPriority.Normal, ct);
        await using (var context = fixture.CreateDbContext())
        {
            await new NotificationRepository(context).ClaimDueDeliveriesAsync("worker-a", Email, now + Lease, 1000, now, ct);
        }

        // Before the lease runs out nothing is swept: the worker may still be mid-send.
        await using (var context = fixture.CreateDbContext())
        {
            (await new NotificationRepository(context).SweepExpiredLeasesAsync(now + TimeSpan.FromSeconds(30), ct)).Should().Be(0);
        }

        await using (var context = fixture.CreateDbContext())
        {
            var swept = await new NotificationRepository(context).SweepExpiredLeasesAsync(now + Lease + TimeSpan.FromSeconds(1), ct);
            swept.Should().BeGreaterThanOrEqualTo(2);
        }

        var rows = await DeliveriesAsync(seeded, ct);
        rows.Should().OnlyContain(d =>
            d.Status == DeliveryStatus.Failed
            && d.FailureKind == DeliveryFailureKind.AmbiguousOutcome
            && d.NextAttemptAtUtc == null
            && d.LeaseOwner == null
            && d.LeaseExpiresAtUtc == null);

        // And it is never claimed again, however long it waits.
        await using var again = fixture.CreateDbContext();
        var reclaimed = await new NotificationRepository(again).ClaimDueDeliveriesAsync("worker-b", Email, now + Lease, 1000, now + TimeSpan.FromDays(1), ct);
        reclaimed.Should().NotContain(seeded);

        await using var verify = fixture.CreateDbContext();
        var notificationStatuses = await verify.Notifications.AsNoTracking()
            .Where(n => n.Deliveries.Any(d => seeded.Contains(d.Id))).Select(n => n.Status).ToListAsync(ct);
        notificationStatuses.Should().OnlyContain(s => s == NotificationStatus.Failed);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task SweepExpiredLeasesAsync_LeavesALiveLease_AndANotificationThatIsAlreadyDelivered_Alone()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var live = (await SeedAsync(1, now, NotificationPriority.Normal, ct))[0];
        var inCenter = (await SeedAsync(1, now, NotificationPriority.Normal, ct, withDeliveredInApp: true))[0];
        await using (var context = fixture.CreateDbContext())
        {
            await new NotificationRepository(context).ClaimDueDeliveriesAsync("worker-a", Email, now + Lease, 1000, now, ct);
        }

        await using (var context = fixture.CreateDbContext())
        {
            // Both leases are claimed at `now`; sweeping at now+1s finds neither expired.
            (await new NotificationRepository(context).SweepExpiredLeasesAsync(now + TimeSpan.FromSeconds(1), ct)).Should().Be(0);
        }

        await using (var context = fixture.CreateDbContext())
        {
            await new NotificationRepository(context).SweepExpiredLeasesAsync(now + Lease + TimeSpan.FromSeconds(1), ct);
        }

        await using var verify = fixture.CreateDbContext();
        var delivered = await verify.Notifications.AsNoTracking().SingleAsync(n => n.Deliveries.Any(d => d.Id == inCenter), ct);
        delivered.Status.Should().Be(NotificationStatus.Delivered, "the in-app copy already reached the user, so the notification stays delivered");
        (await DeliveriesAsync([live], ct)).Single().Status.Should().Be(DeliveryStatus.Failed);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ReleaseClaimsAsync_HandsAClaimedButUnsentDeliveryBack_WithoutCountingTheAttempt()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var seeded = await SeedAsync(3, now, NotificationPriority.Normal, ct);
        await using (var context = fixture.CreateDbContext())
        {
            await new NotificationRepository(context).ClaimDueDeliveriesAsync("worker-a", Email, now + Lease, 1000, now, ct);
        }

        await using (var context = fixture.CreateDbContext())
        {
            // Two are handed back; the third is mid-send and stays with its lease.
            var released = await new NotificationRepository(context).ReleaseClaimsAsync("worker-a", [seeded[0], seeded[1]], now, ct);
            released.Should().Be(2);
        }

        var rows = (await DeliveriesAsync(seeded, ct)).ToDictionary(d => d.Id);
        rows[seeded[0]].Should().Match<NotificationDelivery>(d =>
            d.Status == DeliveryStatus.Pending && d.AttemptCount == 0 && d.LeaseOwner == null && d.LeaseExpiresAtUtc == null);
        rows[seeded[1]].Status.Should().Be(DeliveryStatus.Pending);
        rows[seeded[2]].Status.Should().Be(DeliveryStatus.Sending);
        rows[seeded[2]].LeaseOwner.Should().Be("worker-a");

        await using var again = fixture.CreateDbContext();
        var reclaimed = await new NotificationRepository(again).ClaimDueDeliveriesAsync("worker-b", Email, now + Lease, 1000, now, ct);
        reclaimed.Should().Contain([seeded[0], seeded[1]]).And.NotContain(seeded[2]);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ReleaseClaimsAsync_ADeliveryThatHadAlreadyFailedOnce_GoesBackToRetrying()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var id = (await SeedAsync(1, now, NotificationPriority.Normal, ct))[0];
        await using (var context = fixture.CreateDbContext())
        {
            var notification = await context.Notifications.Include(n => n.Deliveries).SingleAsync(n => n.Deliveries.Any(d => d.Id == id), ct);
            var delivery = notification.Deliveries.Single();
            delivery.MarkSending("worker-a", now + Lease, now);
            delivery.ScheduleRetry(now, DeliveryFailureKind.Transient, "temporary", null, now);
            notification.RecomputeStatus();
            await context.SaveChangesAsync(ct);
        }

        await using (var context = fixture.CreateDbContext())
        {
            await new NotificationRepository(context).ClaimDueDeliveriesAsync("worker-b", Email, now + Lease, 1000, now, ct);
        }

        await using (var context = fixture.CreateDbContext())
        {
            await new NotificationRepository(context).ReleaseClaimsAsync("worker-b", [id], now, ct);
        }

        var row = (await DeliveriesAsync([id], ct)).Single();
        row.Status.Should().Be(DeliveryStatus.Retrying);
        row.AttemptCount.Should().Be(1, "the first attempt really failed; only the claim's own attempt is returned");
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ReleaseClaimsAsync_NeverReleasesADeliveryAnotherWorkerHolds()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var id = (await SeedAsync(1, now, NotificationPriority.Normal, ct))[0];
        await using (var context = fixture.CreateDbContext())
        {
            await new NotificationRepository(context).ClaimDueDeliveriesAsync("worker-a", Email, now + Lease, 1000, now, ct);
        }

        await using (var context = fixture.CreateDbContext())
        {
            (await new NotificationRepository(context).ReleaseClaimsAsync("worker-b", [id], now, ct)).Should().Be(0);
        }

        (await DeliveriesAsync([id], ct)).Single().LeaseOwner.Should().Be("worker-a");
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task GetDueBacklogAsync_CountsOnlyDuePendingAndRetryingDeliveries()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var before = await BacklogAsync(now, ct);
        await SeedAsync(2, now.AddMinutes(-7), NotificationPriority.Normal, ct);
        await SeedAsync(1, now.AddMinutes(30), NotificationPriority.Normal, ct);

        var after = await BacklogAsync(now, ct);

        (after.DueCount - before.DueCount).Should().Be(2, "the delivery due in 30 minutes is not backlog yet");
        after.OldestDueAtUtc.Should().BeOnOrBefore(now.AddMinutes(-7));
    }

    private async Task<AskLucy.Application.Notifications.Abstractions.DeliveryBacklog> BacklogAsync(DateTime now, CancellationToken ct)
    {
        await using var context = fixture.CreateDbContext();
        return await new NotificationRepository(context).GetDueBacklogAsync(now, ct);
    }

    private async Task<List<NotificationDelivery>> DeliveriesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        await using var context = fixture.CreateDbContext();
        return await context.Set<NotificationDelivery>().AsNoTracking().Where(d => ids.Contains(d.Id)).ToListAsync(ct);
    }

    /// <summary>One notification per delivery, each with a pending email delivery due at <paramref name="dueAtUtc"/>. Returns the delivery ids.</summary>
    private async Task<List<Guid>> SeedAsync(
        int count, DateTime dueAtUtc, NotificationPriority priority, CancellationToken ct, bool withDeliveredInApp = false)
    {
        var userId = $"user-{Guid.NewGuid():N}";
        await using var context = fixture.CreateDbContext();
        context.Users.Add(PersistenceTestFixture.CreateTestUser(userId));

        var definition = NotificationTypeCatalog.Get(NotificationTypeKeys.WorkflowExecutionFailed);
        var ids = new List<Guid>(count);
        for (var i = 0; i < count; i++)
        {
            var correlation = $"claim-test-{Guid.NewGuid():N}";
            var notification = Notification.Create(
                userId, definition, priority, withDeliveredInApp ? "Title" : string.Empty, withDeliveredInApp ? "Message" : string.Empty, "en", correlation, dueAtUtc,
                showInCenter: withDeliveredInApp, eventKey: correlation);
            if (withDeliveredInApp)
            {
                notification.AddDelivery(NotificationDelivery.CreateDelivered(NotificationChannel.InApp, priority, "en", null, correlation, dueAtUtc));
            }

            var email = NotificationDelivery.CreatePending(
                NotificationChannel.Email, priority, RecipientKind.User, null, maxAttempts: 5, expiresAtUtc: null, correlation, dueAtUtc);
            notification.AddDelivery(email);
            context.Notifications.Add(notification);
            ids.Add(email.Id);
        }

        await context.SaveChangesAsync(ct);
        return ids;
    }
}

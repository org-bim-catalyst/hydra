using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Processing;
using AskLucy.Application.Options;
using AskLucy.Domain.Notifications;
using AskLucy.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AskLucy.Persistence.Tests.Notifications;

/// <summary>
/// T155 — specs/067 FR-059, research R20 against a real database: each class of data goes after its own window, in batches, a
/// notification with work still in flight is never orphaned, and audit rows are never deleted. Rows other tests left behind are
/// swept too (retention is global), so every assertion is about the ids this test seeded.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class NotificationRetentionTests(PersistenceTestFixture fixture)
{
    private static readonly NotificationTypeDefinition Failed = NotificationTypeCatalog.Get(NotificationTypeKeys.WorkflowExecutionFailed);

    private sealed class NowProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed class Monitor(NotificationsOptions value) : IOptionsMonitor<NotificationsOptions>
    {
        public NotificationsOptions CurrentValue => value;

        public NotificationsOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<NotificationsOptions, string?> listener) => null;
    }

    private RetentionService Service(DateTime now, int batchSize = 1000)
    {
        var services = new ServiceCollection()
            .AddScoped<INotificationRetentionRepository>(_ => new NotificationRetentionRepository(fixture.CreateDbContext()))
            .BuildServiceProvider();
        var options = new NotificationsOptions { Retention = new NotificationRetentionOptions { BatchSize = batchSize } };
        return new RetentionService(services.GetRequiredService<IServiceScopeFactory>(), new Monitor(options), new NowProvider(now), NullLogger<RetentionService>.Instance);
    }

    private async Task<string> SeedUserAsync(CancellationToken ct)
    {
        var userId = $"retention-{Guid.NewGuid():N}";
        await using var context = fixture.CreateDbContext();
        context.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
        await context.SaveChangesAsync(ct);
        return userId;
    }

    /// <summary>A notification created <paramref name="ageDays"/> ago with a delivered in-app delivery, optionally read or deleted <paramref name="eventAgeDays"/> ago.</summary>
    private async Task<(Guid NotificationId, Guid InAppDeliveryId)> SeedNotificationAsync(
        string userId, DateTime now, int ageDays, CancellationToken ct, int? readAgeDays = null, int? deletedAgeDays = null, bool withActiveEmail = false)
    {
        var created = now.AddDays(-ageDays);
        var correlation = $"retention-{Guid.NewGuid():N}";
        var notification = Notification.Create(userId, Failed, NotificationPriority.Normal, "Title", "Message", "en", correlation, created, showInCenter: true, eventKey: correlation);
        var inApp = NotificationDelivery.CreateDelivered(NotificationChannel.InApp, NotificationPriority.Normal, "en", null, correlation, created);
        notification.AddDelivery(inApp);
        if (withActiveEmail)
        {
            notification.AddDelivery(NotificationDelivery.CreatePending(NotificationChannel.Email, NotificationPriority.Normal, RecipientKind.User, null, 5, null, correlation, created));
        }

        if (readAgeDays is { } read)
        {
            notification.MarkRead(now.AddDays(-read));
        }

        if (deletedAgeDays is { } deleted)
        {
            notification.DeleteByOwner(userId, now.AddDays(-deleted));
        }

        await using var context = fixture.CreateDbContext();
        context.Notifications.Add(notification);
        await context.SaveChangesAsync(ct);
        return (notification.Id, inApp.Id);
    }

    private async Task<bool> NotificationExistsAsync(Guid id, CancellationToken ct)
    {
        await using var context = fixture.CreateDbContext();
        return await context.Notifications.IgnoreQueryFilters().AnyAsync(n => n.Id == id, ct);
    }

    private async Task<bool> DeliveryExistsAsync(Guid id, CancellationToken ct)
    {
        await using var context = fixture.CreateDbContext();
        return await context.Set<NotificationDelivery>().AnyAsync(d => d.Id == id, ct);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ReadNotifications_AreDeletedAfterNinetyDays_WithTheirDeliveries_AndNotBefore()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var user = await SeedUserAsync(ct);
        var old = await SeedNotificationAsync(user, now, ageDays: 120, ct, readAgeDays: 91);
        var recent = await SeedNotificationAsync(user, now, ageDays: 120, ct, readAgeDays: 89);
        var unread = await SeedNotificationAsync(user, now, ageDays: 400, ct);

        var result = await Service(now).RunAsync(ct);

        result.ReadNotifications.Should().BeGreaterThanOrEqualTo(1);
        (await NotificationExistsAsync(old.NotificationId, ct)).Should().BeFalse();
        (await DeliveryExistsAsync(old.InAppDeliveryId, ct)).Should().BeFalse("deleting a notification takes its deliveries with it");
        (await NotificationExistsAsync(recent.NotificationId, ct)).Should().BeTrue();
        (await NotificationExistsAsync(unread.NotificationId, ct)).Should().BeTrue("an unread notification is never retained-out, however old");
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task OwnerDeletedNotifications_AreDeletedAfterThirtyDays()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var user = await SeedUserAsync(ct);
        var old = await SeedNotificationAsync(user, now, ageDays: 40, ct, deletedAgeDays: 31);
        var recent = await SeedNotificationAsync(user, now, ageDays: 40, ct, deletedAgeDays: 29);

        await Service(now).RunAsync(ct);

        (await NotificationExistsAsync(old.NotificationId, ct)).Should().BeFalse();
        (await NotificationExistsAsync(recent.NotificationId, ct)).Should().BeTrue();
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ANotificationWithAnActiveDelivery_IsNeverDeleted_SoNothingInFlightIsOrphaned()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var user = await SeedUserAsync(ct);
        var busy = await SeedNotificationAsync(user, now, ageDays: 200, ct, readAgeDays: 150, withActiveEmail: true);

        await Service(now).RunAsync(ct);

        (await NotificationExistsAsync(busy.NotificationId, ct)).Should().BeTrue();
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task FailedAndDeadLetteredDeliveries_AreDeletedThirtyDaysAfterTheirLastAttempt_AndTheNotificationStays()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var user = await SeedUserAsync(ct);
        var (oldFailed, oldDead, recentFailed, notificationId) = await SeedFailedDeliveriesAsync(user, now, ct);

        var result = await Service(now).RunAsync(ct);

        result.FailedDeliveries.Should().BeGreaterThanOrEqualTo(2);
        (await DeliveryExistsAsync(oldFailed, ct)).Should().BeFalse();
        (await DeliveryExistsAsync(oldDead, ct)).Should().BeFalse();
        (await DeliveryExistsAsync(recentFailed, ct)).Should().BeTrue();
        (await NotificationExistsAsync(notificationId, ct)).Should().BeTrue("only the delivery rows go, not the notification the user may still see");
    }

    private async Task<(Guid OldFailed, Guid OldDead, Guid RecentFailed, Guid NotificationId)> SeedFailedDeliveriesAsync(string userId, DateTime now, CancellationToken ct)
    {
        var correlation = $"retention-{Guid.NewGuid():N}";
        var created = now.AddDays(-50);
        var notification = Notification.Create(userId, Failed, NotificationPriority.Normal, "Title", "Message", "en", correlation, created, showInCenter: true, eventKey: correlation);
        notification.AddDelivery(NotificationDelivery.CreateDelivered(NotificationChannel.InApp, NotificationPriority.Normal, "en", null, correlation, created));

        // One notification can only have one delivery per channel, so the three failures sit on three notifications.
        var oldFailed = FailedEmail(correlation + "-a", created, now.AddDays(-31), dead: false);
        var oldDead = FailedEmail(correlation + "-b", created, now.AddDays(-45), dead: true);
        var recentFailed = FailedEmail(correlation + "-c", created, now.AddDays(-29), dead: false);
        notification.AddDelivery(oldFailed);

        var second = Notification.Create(userId, Failed, NotificationPriority.Normal, string.Empty, string.Empty, "en", correlation + "-b", created, showInCenter: false, eventKey: correlation + "-b");
        second.AddDelivery(oldDead);
        var third = Notification.Create(userId, Failed, NotificationPriority.Normal, string.Empty, string.Empty, "en", correlation + "-c", created, showInCenter: false, eventKey: correlation + "-c");
        third.AddDelivery(recentFailed);

        await using var context = fixture.CreateDbContext();
        context.Notifications.AddRange(notification, second, third);
        await context.SaveChangesAsync(ct);
        return (oldFailed.Id, oldDead.Id, recentFailed.Id, notification.Id);
    }

    private static NotificationDelivery FailedEmail(string correlation, DateTime created, DateTime lastAttempt, bool dead)
    {
        var delivery = NotificationDelivery.CreatePending(NotificationChannel.Email, NotificationPriority.Normal, RecipientKind.User, null, 5, null, correlation, created);
        delivery.MarkSending("w", lastAttempt.AddMinutes(2), lastAttempt);
        if (dead)
        {
            delivery.DeadLetter("Retry limit reached.");
        }
        else
        {
            delivery.Fail(DeliveryFailureKind.Permanent, "Rejected.", "550", lastAttempt);
        }

        return delivery;
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task FinishedEmailDeliveries_AreDeletedAfterNinetyDays_ButAnInAppDeliveryLivesWithItsNotification()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var user = await SeedUserAsync(ct);
        var correlation = $"retention-{Guid.NewGuid():N}";
        var created = now.AddDays(-100);
        var notification = Notification.Create(user, Failed, NotificationPriority.Normal, "Title", "Message", "en", correlation, created, showInCenter: true, eventKey: correlation);
        var inApp = NotificationDelivery.CreateDelivered(NotificationChannel.InApp, NotificationPriority.Normal, "en", null, correlation, created);
        var email = NotificationDelivery.CreatePending(NotificationChannel.Email, NotificationPriority.Normal, RecipientKind.User, null, 5, null, correlation, created);
        email.MarkSending("w", created.AddMinutes(2), created);
        email.MarkSent("en", null, "250 OK", created.AddSeconds(3));
        notification.AddDelivery(inApp);
        notification.AddDelivery(email);
        await using (var context = fixture.CreateDbContext())
        {
            context.Notifications.Add(notification);
            await context.SaveChangesAsync(ct);
        }

        var result = await Service(now).RunAsync(ct);

        result.FinishedDeliveries.Should().BeGreaterThanOrEqualTo(1);
        (await DeliveryExistsAsync(email.Id, ct)).Should().BeFalse();
        (await DeliveryExistsAsync(inApp.Id, ct)).Should().BeTrue("the in-app delivery is what makes the notification appear in the center");
        (await NotificationExistsAsync(notification.Id, ct)).Should().BeTrue();
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task CompletedOutboxEvents_AreDeletedAfterSevenDays_AndPendingOnesNever()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        NotificationOutboxEvent Event(DateTime occurred, DateTime? completedAt)
        {
            var e = NotificationOutboxEvent.Create(NotificationTypeKeys.WorkflowExecutionFailed, """{"kind":"User","userId":"x"}""", "{}", $"retention-{Guid.NewGuid():N}", occurred);
            if (completedAt is { } done)
            {
                e.Claim("w", done.AddMinutes(1), done);
                e.Complete(OutboxEventOutcome.Materialized, done);
            }

            return e;
        }

        var old = Event(now.AddDays(-9), now.AddDays(-8));
        var recent = Event(now.AddDays(-7), now.AddDays(-6));
        var pending = Event(now.AddDays(-30), null);
        await using (var context = fixture.CreateDbContext())
        {
            context.NotificationOutboxEvents.AddRange(old, recent, pending);
            await context.SaveChangesAsync(ct);
        }

        await Service(now).RunAsync(ct);

        await using var verify = fixture.CreateDbContext();
        var remaining = await verify.NotificationOutboxEvents.Where(e => new[] { old.Id, recent.Id, pending.Id }.Contains(e.Id)).Select(e => e.Id).ToListAsync(ct);
        remaining.Should().BeEquivalentTo([recent.Id, pending.Id]);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task AuditRows_AreNeverDeleted_HoweverOld()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var entry = NotificationAuditLog.Record(
            NotificationAuditAction.AnnouncementPublished, "admin-1", nameof(SystemAnnouncement), Guid.NewGuid().ToString(), NotificationAuditOutcome.Succeeded, now.AddYears(-5));
        await using (var context = fixture.CreateDbContext())
        {
            context.NotificationAuditLogs.Add(entry);
            await context.SaveChangesAsync(ct);
        }

        await Service(now).RunAsync(ct);

        await using var verify = fixture.CreateDbContext();
        (await verify.NotificationAuditLogs.IgnoreQueryFilters().AnyAsync(a => a.Id == entry.Id, ct)).Should().BeTrue();
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ADeleteThatIsLargerThanOneBatch_RunsBatchAfterBatchUntilNothingIsLeft()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var user = await SeedUserAsync(ct);
        var ids = new List<Guid>();
        for (var i = 0; i < 7; i++)
        {
            ids.Add((await SeedNotificationAsync(user, now, ageDays: 120, ct, readAgeDays: 100)).NotificationId);
        }

        var result = await Service(now, batchSize: 2).RunAsync(ct);

        result.ReadNotifications.Should().BeGreaterThanOrEqualTo(7, "seven batches' worth of rows, two at a time");
        await using var verify = fixture.CreateDbContext();
        (await verify.Notifications.IgnoreQueryFilters().CountAsync(n => ids.Contains(n.Id), ct)).Should().Be(0);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task RunningItTwice_IsHarmless()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var user = await SeedUserAsync(ct);
        var keep = await SeedNotificationAsync(user, now, ageDays: 5, ct);

        await Service(now).RunAsync(ct);
        var second = await Service(now).RunAsync(ct);

        second.Total.Should().Be(0);
        (await NotificationExistsAsync(keep.NotificationId, ct)).Should().BeTrue();
    }
}

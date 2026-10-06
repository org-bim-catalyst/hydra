using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Admin;
using AskLucy.Domain.Notifications;
using AskLucy.Persistence.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Tests.Notifications;

/// <summary>
/// specs/067 US6 — the administrators' list queries (deliveries, audit, announcements) and the recipient directory's audience paging,
/// against a real database. Every test works in a time window or id range of its own.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class NotificationAdminQueryTests(PersistenceTestFixture fixture)
{
    private static readonly Random Random = new();
    private static readonly NotificationTypeDefinition Failed = NotificationTypeCatalog.Get(NotificationTypeKeys.WorkflowExecutionFailed);

    private static DateTime NewWindowStart() => new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(Random.Next(0, 1800));

    private async Task<string> SeedUserAsync(CancellationToken ct, string? firstName = null)
    {
        var userId = $"adminq-{Guid.NewGuid():N}";
        var user = PersistenceTestFixture.CreateTestUser(userId);
        user.FirstName = firstName;
        user.LastName = firstName is null ? null : "Hassan";
        await using var context = fixture.CreateDbContext();
        context.Users.Add(user);
        await context.SaveChangesAsync(ct);
        return userId;
    }

    /// <summary>A notification with one failed email delivery whose last attempt was at <paramref name="attemptAt"/>.</summary>
    private async Task<Guid> SeedFailedAsync(
        string userId, DateTime attemptAt, CancellationToken ct, NotificationChannel channel = NotificationChannel.Email, bool dead = false, int? deletedAgeDays = null)
    {
        var correlation = $"adminq-{Guid.NewGuid():N}";
        var notification = Notification.Create(userId, Failed, NotificationPriority.Normal, string.Empty, string.Empty, "en", correlation, attemptAt, showInCenter: false, eventKey: correlation);
        var delivery = NotificationDelivery.CreatePending(channel, NotificationPriority.Normal, RecipientKind.User, null, 5, null, correlation, attemptAt);
        notification.AddDelivery(delivery);
        delivery.MarkSending("w", attemptAt.AddMinutes(2), attemptAt);
        if (dead)
        {
            delivery.DeadLetter("Retry limit reached.");
        }
        else
        {
            delivery.Fail(DeliveryFailureKind.Permanent, "Rejected.", "550", attemptAt);
        }

        if (deletedAgeDays is not null)
        {
            // The owner deleted it from the center; reading it needs an in-app delivery, so the flag is set directly.
            typeof(Notification).GetProperty(nameof(Notification.DeletedAtUtc))!.SetValue(notification, attemptAt.AddDays(1));
        }

        await using var context = fixture.CreateDbContext();
        context.Notifications.Add(notification);
        await context.SaveChangesAsync(ct);
        return delivery.Id;
    }

    private static AdminDeliveryFilter Window(DateTime start, params DeliveryStatus[] statuses) =>
        new(statuses.Length == 0 ? [DeliveryStatus.Failed, DeliveryStatus.DeadLettered] : statuses, null, null, null, start, start.AddDays(1));

    // ---- deliveries ----

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task Deliveries_ArePagedNewestFirst_WithAStableCursor_AndNoRowTwice()
    {
        var ct = TestContext.Current.CancellationToken;
        var start = NewWindowStart();
        var user = await SeedUserAsync(ct);
        var expected = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            expected.Add(await SeedFailedAsync(user, start.AddHours(i + 1), ct));
        }

        expected.Reverse();
        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            await using var context = fixture.CreateDbContext();
            var (items, next) = await new NotificationAdminRepository(context).ListDeliveriesAsync(Window(start), cursor, 2, ct);
            seen.AddRange(items.Select(i => i.DeliveryId));
            cursor = next;
        }
        while (cursor is not null);

        seen.Should().Equal(expected);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task Deliveries_FilterByStatusChannelAndCategory()
    {
        var ct = TestContext.Current.CancellationToken;
        var start = NewWindowStart();
        var user = await SeedUserAsync(ct);
        var failed = await SeedFailedAsync(user, start.AddHours(1), ct);
        var dead = await SeedFailedAsync(user, start.AddHours(2), ct, dead: true);
        var inApp = await SeedFailedAsync(user, start.AddHours(3), ct, NotificationChannel.InApp);

        await using var context = fixture.CreateDbContext();
        var repository = new NotificationAdminRepository(context);

        (await repository.ListDeliveriesAsync(Window(start, DeliveryStatus.DeadLettered), null, 50, ct)).Items.Select(i => i.DeliveryId).Should().Equal(dead);
        (await repository.ListDeliveriesAsync(Window(start) with { Channel = NotificationChannel.InApp }, null, 50, ct)).Items.Select(i => i.DeliveryId).Should().Equal(inApp);
        (await repository.ListDeliveriesAsync(Window(start) with { Category = NotificationCategory.Document }, null, 50, ct)).Items.Should().BeEmpty();
        (await repository.ListDeliveriesAsync(Window(start) with { Type = NotificationTypeKeys.WorkflowExecutionFailed }, null, 50, ct)).Items.Select(i => i.DeliveryId)
            .Should().BeEquivalentTo([failed, dead, inApp]);
        (await repository.FindDeliveryIdsAsync(Window(start), 2, ct)).Should().HaveCount(2);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ADelivery_CarriesItsRecipient_AndSaysWhenItsNotificationWasDeleted()
    {
        var ct = TestContext.Current.CancellationToken;
        var start = NewWindowStart();
        var user = await SeedUserAsync(ct, firstName: "Layla");
        var live = await SeedFailedAsync(user, start.AddHours(1), ct);
        var deleted = await SeedFailedAsync(user, start.AddHours(2), ct, deletedAgeDays: 1);

        await using var context = fixture.CreateDbContext();
        var repository = new NotificationAdminRepository(context);
        var liveRow = await repository.GetDeliveryAsync(live, ct);
        var deletedRow = await repository.GetDeliveryAsync(deleted, ct);

        liveRow.Should().NotBeNull();
        liveRow!.RecipientUserId.Should().Be(user);
        liveRow.RecipientDisplayName.Should().Be("Layla Hassan");
        liveRow.RecipientEmail.Should().Be($"{user}@persistence.tests.local");
        liveRow.NotificationDeleted.Should().BeFalse();
        deletedRow!.NotificationDeleted.Should().BeTrue("an administrator still sees it, and is told why it can't be retried");
        (await repository.GetDeliveryAsync(Guid.NewGuid(), ct)).Should().BeNull();
    }

    // ---- audit ----

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task Audit_IsFilteredAndPagedNewestFirst_WithTheActorsName()
    {
        var ct = TestContext.Current.CancellationToken;
        var start = NewWindowStart();
        var admin = await SeedUserAsync(ct, firstName: "Ada");
        var target = Guid.NewGuid().ToString();
        await using (var context = fixture.CreateDbContext())
        {
            for (var i = 0; i < 5; i++)
            {
                context.NotificationAuditLogs.Add(NotificationAuditLog.Record(
                    NotificationAuditAction.DeliveryRetried, admin, "NotificationDelivery", target, NotificationAuditOutcome.Succeeded, start.AddHours(i + 1), """{"n":1}""", $"corr-{i}"));
            }

            context.NotificationAuditLogs.Add(NotificationAuditLog.Record(
                NotificationAuditAction.DeliveryViewed, admin, "NotificationDelivery", target, NotificationAuditOutcome.Succeeded, start.AddHours(9)));
            await context.SaveChangesAsync(ct);
        }

        var filter = new AdminAuditFilter(NotificationAuditAction.DeliveryRetried, "NotificationDelivery", target, admin, start, start.AddDays(1));
        var seen = new List<AdminAuditRow>();
        string? cursor = null;
        do
        {
            await using var context = fixture.CreateDbContext();
            var (items, next) = await new NotificationAdminRepository(context).ListAuditAsync(filter, cursor, 2, ct);
            seen.AddRange(items);
            cursor = next;
        }
        while (cursor is not null);

        seen.Should().HaveCount(5, "the view row is a different action");
        seen.Select(r => r.OccurredAtUtc).Should().BeInDescendingOrder();
        seen.Should().OnlyContain(r => r.ActorDisplayName == "Ada Hassan" && r.Action == NotificationAuditAction.DeliveryRetried);
        seen.Select(r => r.Id).Should().OnlyHaveUniqueItems();
    }

    // ---- announcements ----

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task Announcements_ReportTheirEmailProgress_AndWhetherTheFanOutIsDone()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var admin = await SeedUserAsync(ct, firstName: "Ada");
        var announcement = SystemAnnouncement.Publish(
            AnnouncementKind.Maintenance, "Maintenance", "Down for a bit.", AnnouncementAudience.AllActiveUsers, null, isCritical: true, null, admin, now.AddYears(-30));

        await using (var context = fixture.CreateDbContext())
        {
            context.SystemAnnouncements.Add(announcement);
            var type = NotificationTypeCatalog.Get(NotificationTypeKeys.SystemAnnouncementPublished);
            foreach (var shape in new Func<NotificationDelivery, NotificationDelivery>[]
                     {
                         d => d,
                         d => { d.MarkSending("w", now.AddMinutes(2), now); d.MarkSent("en", null, "250", now); return d; },
                         d => { d.MarkSending("w", now.AddMinutes(2), now); d.MarkSent("en", null, "250", now); return d; },
                         d => { d.Expire(); return d; },
                     })
            {
                var user = await SeedUserAsync(ct);
                var correlation = $"adminq-{Guid.NewGuid():N}";
                var notification = Notification.Create(
                    user, type, NotificationPriority.Normal, string.Empty, string.Empty, "en", correlation, now, showInCenter: false,
                    relatedItemType: AnnouncementKeys.RelatedItemType, relatedItemId: announcement.Id.ToString(), eventKey: correlation);
                notification.AddDelivery(shape(NotificationDelivery.CreatePending(NotificationChannel.Email, NotificationPriority.Normal, RecipientKind.User, null, 5, null, correlation, now)));
                context.Notifications.Add(notification);
            }

            var done = NotificationOutboxEvent.Create(
                NotificationTypeKeys.SystemAnnouncementPublished, """{"kind":"Audience","allActiveUsers":true}""", "{}", "adminq-fan", now, AnnouncementKeys.EventKey(announcement.Id));
            done.Claim("w", now.AddMinutes(1), now);
            done.Complete(OutboxEventOutcome.Materialized, now);
            context.NotificationOutboxEvents.Add(done);
            await context.SaveChangesAsync(ct);
        }

        await using var read = fixture.CreateDbContext();
        var (items, _) = await new NotificationAdminRepository(read).ListAnnouncementsAsync(null, 200, ct);

        var row = items.Single(a => a.Id == announcement.Id);
        row.EmailQueued.Should().Be(1);
        row.EmailSent.Should().Be(2);
        row.EmailExpired.Should().Be(1);
        row.FanOutCompleted.Should().BeTrue();
        row.PublishedByDisplayName.Should().Be("Ada Hassan");
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task AnAnnouncementWhoseFanOutHasNotCompleted_IsInProgress()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await SeedUserAsync(ct);
        var announcement = SystemAnnouncement.Publish(
            AnnouncementKind.Maintenance, "Maintenance", "Down for a bit.", AnnouncementAudience.AllActiveUsers, null, false, null, admin, DateTime.UtcNow.AddYears(-29));
        await using (var context = fixture.CreateDbContext())
        {
            context.SystemAnnouncements.Add(announcement);
            await context.SaveChangesAsync(ct);
        }

        await using var read = fixture.CreateDbContext();
        var (items, _) = await new NotificationAdminRepository(read).ListAnnouncementsAsync(null, 200, ct);

        items.Single(a => a.Id == announcement.Id).FanOutCompleted.Should().BeFalse();
    }

    // ---- the audience ----

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ActiveUserIds_ArePagedInIdOrder_ExcludeDeletedAccounts_AndHonourRoles()
    {
        var ct = TestContext.Current.CancellationToken;
        var prefix = $"aud-{Guid.NewGuid():N}";
        var roleId = $"role-{Guid.NewGuid():N}";
        await using (var context = fixture.CreateDbContext())
        {
            context.Roles.Add(new Identity.ApplicationRole { Id = roleId, Name = roleId, NormalizedName = roleId.ToUpperInvariant() });
            for (var i = 0; i < 5; i++)
            {
                var user = PersistenceTestFixture.CreateTestUser($"{prefix}-{i}");
                user.IsDeleted = i == 3;
                context.Users.Add(user);
                if (i is 1 or 2)
                {
                    context.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = roleId });
                }
            }

            await context.SaveChangesAsync(ct);
        }

        await using var read = fixture.CreateDbContext();
        var directory = new NotificationRecipientDirectory(read);

        var all = await directory.GetActiveUserIdsAfterAsync(null, $"{prefix}-", 100, ct);
        all.Where(id => id.StartsWith(prefix, StringComparison.Ordinal)).Should().Equal($"{prefix}-0", $"{prefix}-1", $"{prefix}-2", $"{prefix}-4");

        var afterFirst = await directory.GetActiveUserIdsAfterAsync([roleId], null, 1, ct);
        afterFirst.Should().Equal($"{prefix}-1");
        var next = await directory.GetActiveUserIdsAfterAsync([roleId], afterFirst[0], 10, ct);
        next.Should().Equal($"{prefix}-2");

        (await directory.CountActiveAsync([roleId], verifiedEmailOnly: true, ct)).Should().Be(2);
    }
}

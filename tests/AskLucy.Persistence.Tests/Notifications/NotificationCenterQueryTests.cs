using AskLucy.Application.Notifications;
using AskLucy.Domain.Notifications;
using AskLucy.Persistence.Identity;
using AskLucy.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Tests.Notifications;

/// <summary>
/// specs/067 T055 — <see cref="NotificationRepository.ListAsync"/>/<see cref="NotificationRepository.MarkAllReadAsync"/>
/// against a real database: keyset stability, category/state filtering, and the
/// owner-deleted/<c>ShowInCenter=false</c> exclusions that only a real query filter and index
/// enforce (an in-memory provider would silently ignore both).
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class NotificationCenterQueryTests(PersistenceTestFixture fixture)
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ListAsync_KeysetPage_StaysStable_WhenNewerRowsAreInsertedBetweenPages()
    {
        var userId = await SeedUserAsync();
        var seeded = await SeedNotificationsAsync(userId, 5, NotificationTypeKeys.WorkflowExecutionFailed, delivered: true);

        await using var readContext = fixture.CreateDbContext();
        var repository = new NotificationRepository(readContext);

        var (firstPage, cursor) = await repository.ListAsync(
            userId, categories: null, NotificationReadState.All, cursor: null, limit: 2, TestContext.Current.CancellationToken);
        firstPage.Should().HaveCount(2);
        cursor.Should().NotBeNull();

        // A newer row lands after the first page was read — the second page must still be exactly
        // the next older rows from the *original* ordering, not shifted by the new insert.
        await SeedNotificationsAsync(userId, 1, NotificationTypeKeys.WorkflowExecutionFailed, delivered: true);

        var (secondPage, _) = await repository.ListAsync(
            userId, categories: null, NotificationReadState.All, cursor, limit: 10, TestContext.Current.CancellationToken);

        // Newest-first: firstPage took the 2 newest (seeded[4], seeded[3]); secondPage must be the
        // remaining 3, oldest-to-newest reversed (i.e. still newest-first among themselves).
        var expected = seeded.AsEnumerable().Reverse().Skip(2);
        secondPage.Select(n => n.Id).Should().BeEquivalentTo(expected.Select(n => n.Id), options => options.WithStrictOrdering());
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ListAsync_FiltersByCategoryAndReadState()
    {
        var userId = await SeedUserAsync();
        var workflow = (await SeedNotificationsAsync(userId, 1, NotificationTypeKeys.WorkflowExecutionFailed, delivered: true))[0];
        var agent = (await SeedNotificationsAsync(userId, 1, NotificationTypeKeys.AgentExecutionCompleted, delivered: true))[0];
        var readWorkflow = (await SeedNotificationsAsync(userId, 1, NotificationTypeKeys.WorkflowExecutionFailed, delivered: true, markRead: true))[0];

        await using var readContext = fixture.CreateDbContext();
        var repository = new NotificationRepository(readContext);

        var (byCategory, _) = await repository.ListAsync(
            userId, [NotificationCategory.Agent], NotificationReadState.All, cursor: null, limit: 50, TestContext.Current.CancellationToken);
        byCategory.Select(n => n.Id).Should().BeEquivalentTo([agent.Id]);

        var (unread, _) = await repository.ListAsync(
            userId, categories: null, NotificationReadState.Unread, cursor: null, limit: 50, TestContext.Current.CancellationToken);
        unread.Select(n => n.Id).Should().BeEquivalentTo([workflow.Id, agent.Id]);

        var (read, _) = await repository.ListAsync(
            userId, categories: null, NotificationReadState.Read, cursor: null, limit: 50, TestContext.Current.CancellationToken);
        read.Select(n => n.Id).Should().BeEquivalentTo([readWorkflow.Id]);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ListAsync_And_CountUnreadAsync_ExcludeOwnerDeletedAndHiddenRows()
    {
        var userId = await SeedUserAsync();
        var visible = (await SeedNotificationsAsync(userId, 1, NotificationTypeKeys.WorkflowExecutionFailed, delivered: true))[0];
        var deleted = (await SeedNotificationsAsync(userId, 1, NotificationTypeKeys.WorkflowExecutionFailed, delivered: true))[0];
        // AccountPasswordResetRequested's catalogue definition has ShowInCenter=false, so the
        // domain's Create() forces ShowInCenter off regardless of what the caller asks for.
        await SeedNotificationsAsync(userId, 1, NotificationTypeKeys.AccountPasswordResetRequested, delivered: false, showInCenterRequested: false);

        await using (var writeContext = fixture.CreateDbContext())
        {
            var toDelete = await writeContext.Notifications.SingleAsync(n => n.Id == deleted.Id, TestContext.Current.CancellationToken);
            toDelete.DeleteByOwner(userId, Now);
            await writeContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readContext = fixture.CreateDbContext();
        var repository = new NotificationRepository(readContext);

        var (items, _) = await repository.ListAsync(
            userId, categories: null, NotificationReadState.All, cursor: null, limit: 50, TestContext.Current.CancellationToken);
        items.Select(n => n.Id).Should().BeEquivalentTo([visible.Id]);

        (await repository.CountUnreadAsync(userId, TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task MarkAllReadAsync_TouchesOnlyTheCallersRows_OptionallyScopedToOneCategory()
    {
        var userId = await SeedUserAsync();
        var otherUserId = await SeedUserAsync();
        var workflow = (await SeedNotificationsAsync(userId, 1, NotificationTypeKeys.WorkflowExecutionFailed, delivered: true))[0];
        var agent = (await SeedNotificationsAsync(userId, 1, NotificationTypeKeys.AgentExecutionCompleted, delivered: true))[0];
        var othersWorkflow = (await SeedNotificationsAsync(otherUserId, 1, NotificationTypeKeys.WorkflowExecutionFailed, delivered: true))[0];

        await using (var context = fixture.CreateDbContext())
        {
            var repository = new NotificationRepository(context);
            var updated = await repository.MarkAllReadAsync(userId, NotificationCategory.Workflow, Now, TestContext.Current.CancellationToken);
            updated.Should().Be(1);
        }

        await using var verify = fixture.CreateDbContext();
        (await verify.Notifications.SingleAsync(n => n.Id == workflow.Id, TestContext.Current.CancellationToken)).ReadAtUtc.Should().NotBeNull();
        (await verify.Notifications.SingleAsync(n => n.Id == agent.Id, TestContext.Current.CancellationToken)).ReadAtUtc.Should().BeNull();
        (await verify.Notifications.SingleAsync(n => n.Id == othersWorkflow.Id, TestContext.Current.CancellationToken)).ReadAtUtc.Should().BeNull();
    }

    private async Task<string> SeedUserAsync()
    {
        var userId = $"user-{Guid.NewGuid():N}";
        await using var context = fixture.CreateDbContext();
        context.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return userId;
    }

    /// <summary>
    /// Seeds <paramref name="count"/> notifications a strictly increasing tick apart, so
    /// <see cref="Notification.CreatedAtUtc"/> ordering is deterministic regardless of how fast the
    /// test host runs. Returns them oldest-to-newest; the center itself reads newest-first.
    /// </summary>
    private async Task<List<Notification>> SeedNotificationsAsync(
        string userId, int count, string typeKey, bool delivered, bool markRead = false, bool showInCenterRequested = true)
    {
        await using var context = fixture.CreateDbContext();
        var definition = NotificationTypeCatalog.Get(typeKey);
        var notifications = new List<Notification>();
        for (var i = 0; i < count; i++)
        {
            var createdAt = Now.AddTicks(TickCounter.Next());
            var notification = Notification.Create(
                userId, definition, definition.DefaultPriority, "Title", "Message", "en",
                $"corr-{Guid.NewGuid():N}", createdAt, showInCenterRequested,
                eventKey: $"evt-{Guid.NewGuid():N}");

            if (delivered)
            {
                notification.AddDelivery(NotificationDelivery.CreateDelivered(
                    NotificationChannel.InApp, definition.DefaultPriority, "en", null, notification.CorrelationId, createdAt));
            }

            if (markRead)
            {
                notification.MarkRead(createdAt);
            }

            notifications.Add(notification);
        }

        context.Notifications.AddRange(notifications);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return notifications;
    }

    /// <summary>Monotonically increasing tick offset so seeded rows never tie on <c>CreatedAtUtc</c> within one test.</summary>
    private static class TickCounter
    {
        private static long counter;

        public static long Next() => System.Threading.Interlocked.Increment(ref counter);
    }
}

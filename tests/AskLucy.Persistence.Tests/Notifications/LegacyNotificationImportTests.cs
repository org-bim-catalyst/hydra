using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Documents;
using AskLucy.Domain.Memory;
using AskLucy.Domain.Notifications;
using AskLucy.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AskLucy.Persistence.Tests.Notifications;

/// <summary>
/// specs/067 data-model.md § Legacy mapping (T093), against a real database — the one-time
/// import that carries the pre-hub <see cref="DocumentNotification"/>/<see cref="MemoryNotification"/>
/// rows onto the hub's own <see cref="Notification"/> table.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class LegacyNotificationImportTests(PersistenceTestFixture fixture)
{
    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ImportAsync_CarriesEveryLegacyRowOnce_WithTheMappedFields_AndLeavesTheLegacyRowsUntouched()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedUserAsync();
        var documentId = Guid.NewGuid();
        var memoryId = Guid.NewGuid();

        DocumentNotification documentRow;
        MemoryNotification unreadMemoryRow;
        MemoryNotification readMemoryRow;
        await using (var context = fixture.CreateDbContext())
        {
            documentRow = DocumentNotification.Create(userId, documentId, DocumentNotificationEventType.ProcessingCompleted, "Your document finished processing.", userId);
            documentRow.MarkRead(userId);
            unreadMemoryRow = MemoryNotification.Create(userId, memoryId, MemoryNotificationEventType.AutoCreated, "A memory was created.", userId);
            readMemoryRow = MemoryNotification.Create(userId, memoryId, MemoryNotificationEventType.AutoApproved, "A memory was approved.", userId);
            readMemoryRow.MarkRead(userId);

            context.DocumentNotifications.AddRange(documentRow);
            context.MemoryNotifications.AddRange(unreadMemoryRow, readMemoryRow);
            await context.SaveChangesAsync(ct);
        }

        await using (var context = fixture.CreateDbContext())
        {
            var repository = new LegacyNotificationImportRepository(context, new FakeTemplateRenderer(), new FakeLinkBuilder(), NullLogger<LegacyNotificationImportRepository>.Instance);
            var result = await repository.ImportAsync(ct);
            result.DocumentNotificationsImported.Should().Be(1);
            result.MemoryNotificationsImported.Should().Be(2);
        }

        await using var verify = fixture.CreateDbContext();
        var imported = await verify.Notifications.AsNoTracking()
            .Include(n => n.Deliveries)
            .Where(n => n.RecipientUserId == userId && n.EventKey != null && n.EventKey.StartsWith("legacy:"))
            .ToListAsync(ct);
        imported.Should().HaveCount(3);

        var documentNotification = imported.Single(n => n.EventKey == $"legacy:document:{documentRow.Id}");
        documentNotification.Type.Should().Be(NotificationTypeKeys.DocumentProcessingCompleted);
        documentNotification.Message.Should().Be("Your document finished processing.");
        documentNotification.Title.Should().NotBeNullOrWhiteSpace();
        documentNotification.Language.Should().Be("en");
        documentNotification.CreatedAtUtc.Should().Be(documentRow.CreatedAtUtc);
        documentNotification.ReadAtUtc.Should().Be(documentRow.ModifiedAtUtc);
        documentNotification.ShowInCenter.Should().BeTrue();
        documentNotification.CorrelationId.Should().Be("legacy-import");
        documentNotification.TemplateVersionId.Should().BeNull();
        documentNotification.RelatedItemType.Should().Be("Document");
        documentNotification.RelatedItemId.Should().Be(documentId.ToString());
        documentNotification.Deliveries.Should().ContainSingle(d => d.Channel == NotificationChannel.InApp && d.Status == DeliveryStatus.Delivered);

        var unreadMemory = imported.Single(n => n.EventKey == $"legacy:memory:{unreadMemoryRow.Id}");
        unreadMemory.ReadAtUtc.Should().BeNull();
        unreadMemory.RelatedItemType.Should().Be("Memory");

        var readMemory = imported.Single(n => n.EventKey == $"legacy:memory:{readMemoryRow.Id}");
        readMemory.ReadAtUtc.Should().Be(readMemoryRow.ReadAtUtc);

        var legacyDocumentRow = await verify.DocumentNotifications.AsNoTracking().SingleAsync(n => n.Id == documentRow.Id, ct);
        legacyDocumentRow.Message.Should().Be("Your document finished processing.");
        legacyDocumentRow.IsRead.Should().BeTrue();

        await using (var second = fixture.CreateDbContext())
        {
            var repository = new LegacyNotificationImportRepository(second, new FakeTemplateRenderer(), new FakeLinkBuilder(), NullLogger<LegacyNotificationImportRepository>.Instance);
            var secondRun = await repository.ImportAsync(ct);
            secondRun.Total.Should().Be(0, "already-imported rows are identified by EventKey and skipped");
        }
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ImportAsync_SkipsRowsWhoseUserNoLongerExists()
    {
        var ct = TestContext.Current.CancellationToken;
        var deletedUserId = $"user-{Guid.NewGuid():N}"; // never inserted into Users
        DocumentNotification orphanRow;
        await using (var context = fixture.CreateDbContext())
        {
            orphanRow = DocumentNotification.Create(deletedUserId, null, DocumentNotificationEventType.StorageLimitReached, "Storage limit reached.", deletedUserId);
            context.DocumentNotifications.Add(orphanRow);
            await context.SaveChangesAsync(ct);
        }

        await using (var context = fixture.CreateDbContext())
        {
            var repository = new LegacyNotificationImportRepository(context, new FakeTemplateRenderer(), new FakeLinkBuilder(), NullLogger<LegacyNotificationImportRepository>.Instance);
            (await repository.ImportAsync(ct)).DocumentNotificationsImported.Should().Be(0);
        }

        await using var verify = fixture.CreateDbContext();
        (await verify.Notifications.AsNoTracking().AnyAsync(n => n.EventKey == $"legacy:document:{orphanRow.Id}", ct)).Should().BeFalse();
    }

    private async Task<string> SeedUserAsync()
    {
        var userId = $"user-{Guid.NewGuid():N}";
        await using var context = fixture.CreateDbContext();
        context.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return userId;
    }

    /// <summary>Deterministic stand-in for the real, Infrastructure-only <see cref="INotificationTemplateRenderer"/> (this project doesn't reference Infrastructure).</summary>
    private sealed class FakeTemplateRenderer : INotificationTemplateRenderer
    {
        public Task<RenderedInApp> RenderInAppAsync(
            NotificationTypeDefinition definition, string language, IReadOnlyDictionary<string, string?> variables, CancellationToken cancellationToken) =>
            Task.FromResult(new RenderedInApp($"{definition.Key} title", "unused", null, Guid.NewGuid(), language));
    }

    /// <summary>Deterministic stand-in for the real, Infrastructure-only <see cref="INotificationLinkBuilder"/>.</summary>
    private sealed class FakeLinkBuilder : INotificationLinkBuilder
    {
        public string? BuildRelative(NotificationTypeDefinition definition, RelatedItem? relatedItem, Guid notificationId) =>
            definition.RouteTemplate?.Replace("{id}", relatedItem?.Id, StringComparison.Ordinal);

        public string? BuildAbsolute(NotificationTypeDefinition definition, RelatedItem? relatedItem, Guid notificationId) =>
            BuildRelative(definition, relatedItem, notificationId);
    }
}

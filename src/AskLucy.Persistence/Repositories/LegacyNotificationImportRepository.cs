using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Legacy;
using AskLucy.Domain.Documents;
using AskLucy.Domain.Memory;
using AskLucy.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AskLucy.Persistence.Repositories;

/// <summary>
/// specs/067 data-model.md § Legacy mapping (T095) — replays every existing
/// <see cref="DocumentNotification"/>/<see cref="MemoryEntities.MemoryNotification"/> row onto the
/// hub, once. Reuses <see cref="LegacyNotificationTypeMap"/> for the type-key lookup (the same map
/// the live emitters use, so the two paths can't drift), <see cref="INotificationTemplateRenderer"/>
/// for the title, and <see cref="INotificationLinkBuilder"/> for the route, exactly like
/// <c>NotificationMaterializer</c> does for a live event — just with no dispatcher and no outbox.
///
/// <para>A row whose type has no published in-app template yet (<see cref="NotificationRenderException"/>
/// — nothing seeds templates at startup, they're admin-authored) is skipped and logged, not allowed
/// to fail the whole batch; it's picked up on a later run once a template is published.</para>
/// </summary>
public sealed class LegacyNotificationImportRepository(
    AskLucyDbContext dbContext,
    INotificationTemplateRenderer renderer,
    INotificationLinkBuilder links,
    ILogger<LegacyNotificationImportRepository> logger) : ILegacyNotificationImport
{
    private const string CorrelationId = "legacy-import";

    public async Task<LegacyNotificationImportResult> ImportAsync(CancellationToken cancellationToken)
    {
        var documentCount = await ImportDocumentNotificationsAsync(cancellationToken);
        var memoryCount = await ImportMemoryNotificationsAsync(cancellationToken);
        return new LegacyNotificationImportResult(documentCount, memoryCount);
    }

    private async Task<int> ImportDocumentNotificationsAsync(CancellationToken cancellationToken)
    {
        var alreadyImported = await ImportedLegacyIdsAsync("legacy:document:", cancellationToken);
        var validUserIds = await dbContext.Users.Select(u => u.Id).ToHashSetAsync(cancellationToken);

        var rows = await dbContext.DocumentNotifications.AsNoTracking().ToListAsync(cancellationToken);
        var imported = 0;
        foreach (var row in rows)
        {
            var eventKey = $"legacy:document:{row.Id}";
            if (alreadyImported.Contains(eventKey) || !validUserIds.Contains(row.UserId))
            {
                continue;
            }

            var typeKey = LegacyNotificationTypeMap.ToCatalogKey(row.EventType);
            if (!NotificationTypeCatalog.TryGet(typeKey, out var definition) || definition is null)
            {
                continue;
            }

            var relatedItem = row.DocumentId is { } documentId ? new RelatedItem("Document", documentId.ToString()) : null;
            var notification = await BuildNotificationAsync(
                definition, relatedItem, row.UserId, eventKey, row.Message, row.CreatedAtUtc,
                row.IsRead ? (row.ModifiedAtUtc ?? row.CreatedAtUtc) : null, cancellationToken);
            if (notification is null)
            {
                continue;
            }

            dbContext.Notifications.Add(notification);
            imported++;
        }

        if (imported > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return imported;
    }

    private async Task<int> ImportMemoryNotificationsAsync(CancellationToken cancellationToken)
    {
        var alreadyImported = await ImportedLegacyIdsAsync("legacy:memory:", cancellationToken);
        var validUserIds = await dbContext.Users.Select(u => u.Id).ToHashSetAsync(cancellationToken);

        var rows = await dbContext.MemoryNotifications.AsNoTracking().ToListAsync(cancellationToken);
        var imported = 0;
        foreach (var row in rows)
        {
            var eventKey = $"legacy:memory:{row.Id}";
            if (alreadyImported.Contains(eventKey) || !validUserIds.Contains(row.UserId))
            {
                continue;
            }

            var typeKey = LegacyNotificationTypeMap.ToCatalogKey(row.EventType);
            if (!NotificationTypeCatalog.TryGet(typeKey, out var definition) || definition is null)
            {
                continue;
            }

            var relatedItem = row.MemoryId is { } memoryId ? new RelatedItem("Memory", memoryId.ToString()) : null;
            var notification = await BuildNotificationAsync(
                definition, relatedItem, row.UserId, eventKey, row.Message, row.CreatedAtUtc, row.ReadAtUtc, cancellationToken);
            if (notification is null)
            {
                continue;
            }

            dbContext.Notifications.Add(notification);
            imported++;
        }

        if (imported > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return imported;
    }

    private async Task<Notification?> BuildNotificationAsync(
        NotificationTypeDefinition definition,
        RelatedItem? relatedItem,
        string userId,
        string eventKey,
        string message,
        DateTime createdAtUtc,
        DateTime? readAtUtc,
        CancellationToken cancellationToken)
    {
        var notificationId = Guid.CreateVersion7();
        var route = links.BuildRelative(definition, relatedItem, notificationId);

        RenderedInApp rendered;
        try
        {
            rendered = await renderer.RenderInAppAsync(
                definition, "en", new Dictionary<string, string?>(StringComparer.Ordinal), cancellationToken);
        }
        catch (NotificationRenderException ex)
        {
#pragma warning disable CA1848
            logger.LogWarning(ex, "Skipped legacy import of {EventKey} ({Type}) — no published in-app template yet.", eventKey, definition.Key);
#pragma warning restore CA1848
            return null;
        }

        var notification = Notification.Create(
            userId,
            definition,
            definition.DefaultPriority,
            rendered.Title,
            message,
            language: "en",
            correlationId: CorrelationId,
            now: createdAtUtc,
            showInCenter: true,
            templateVersionId: null,
            relatedItemType: relatedItem?.Type,
            relatedItemId: relatedItem?.Id,
            actionRoute: route,
            eventKey: eventKey,
            actionLabel: route is null ? null : rendered.ActionLabel,
            id: notificationId);

        notification.AddDelivery(NotificationDelivery.CreateDelivered(
            NotificationChannel.InApp, definition.DefaultPriority, "en", templateVersionId: null, CorrelationId, createdAtUtc));

        if (readAtUtc is { } readAt)
        {
            notification.MarkRead(readAt);
        }

        return notification;
    }

    private async Task<HashSet<string>> ImportedLegacyIdsAsync(string prefix, CancellationToken cancellationToken) =>
        await dbContext.Notifications
            .IgnoreQueryFilters()
            .Where(n => n.EventKey != null && n.EventKey.StartsWith(prefix))
            .Select(n => n.EventKey!)
            .ToHashSetAsync(cancellationToken);
}

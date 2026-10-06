using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Admin;
using AskLucy.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

/// <summary>
/// Read models for the administrators' notification screens (specs/067 US6, research R21). Everything is a database
/// aggregate or a keyset page; nothing here loads a table into memory. The owner-deletion filter is ignored on purpose:
/// an administrator sees the delivery of a notification its owner deleted, and is told why it can't be retried.
/// </summary>
public sealed class NotificationAdminRepository(AskLucyDbContext dbContext) : INotificationAdminRepository
{
    private IQueryable<NotificationDelivery> Deliveries() => dbContext.Set<NotificationDelivery>().AsNoTracking();

    private IQueryable<Notification> Notifications() => dbContext.Notifications.IgnoreQueryFilters().AsNoTracking();

    // ---- statistics ----

    public async Task<NotificationStatisticsData> GetStatisticsAsync(
        DateTime fromUtc, DateTime toUtc, bool hourlyBuckets, DateTime now, CancellationToken cancellationToken)
    {
        var created = Notifications().Where(n => n.CreatedAtUtc >= fromUtc && n.CreatedAtUtc < toUtc);
        var sent = Deliveries().Where(d => d.SentAtUtc != null && d.SentAtUtc >= fromUtc && d.SentAtUtc < toUtc);
        var failed = Deliveries().Where(d => d.LastAttemptAtUtc != null && d.LastAttemptAtUtc >= fromUtc && d.LastAttemptAtUtc < toUtc
            && (d.Status == DeliveryStatus.Failed || d.Status == DeliveryStatus.DeadLettered));

        var createdCount = await created.CountAsync(cancellationToken);
        var sentCount = await sent.CountAsync(cancellationToken);
        var failedCount = await failed.CountAsync(d => d.Status == DeliveryStatus.Failed, cancellationToken);
        var deadLetteredCount = await failed.CountAsync(d => d.Status == DeliveryStatus.DeadLettered, cancellationToken);
        var ambiguousCount = await failed.CountAsync(d => d.FailureKind == DeliveryFailureKind.AmbiguousOutcome, cancellationToken);

        var retries = await Deliveries()
            .Where(d => d.AttemptCount > 1 && d.LastAttemptAtUtc != null && d.LastAttemptAtUtc >= fromUtc && d.LastAttemptAtUtc < toUtc)
            .SumAsync(d => (int?)(d.AttemptCount - 1), cancellationToken) ?? 0;

        var emailSent = await sent.CountAsync(d => d.Channel == NotificationChannel.Email, cancellationToken);
        var emailFailed = await failed.CountAsync(d => d.Channel == NotificationChannel.Email, cancellationToken);

        var (average, p95) = await LatenciesAsync(sent, cancellationToken);

        var outboxPending = await dbContext.NotificationOutboxEvents.AsNoTracking()
            .CountAsync(e => e.Status == OutboxEventStatus.Pending, cancellationToken);
        var due = Deliveries().Where(d => (d.Status == DeliveryStatus.Pending || d.Status == DeliveryStatus.Retrying) && d.NextAttemptAtUtc <= now);
        var dueCount = await due.CountAsync(cancellationToken);
        var oldestDue = dueCount == 0 ? null : await due.MinAsync(d => d.NextAttemptAtUtc, cancellationToken);

        var unread = await dbContext.Notifications.AsNoTracking()
            .CountAsync(n => n.ShowInCenter && n.ReadAtUtc == null && (n.ExpiresAtUtc == null || n.ExpiresAtUtc > now), cancellationToken);

        var createdByCategory = await created.GroupBy(n => n.Category).Select(g => new { Category = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var failedByCategory = await failed
            .Join(dbContext.Notifications.IgnoreQueryFilters(), d => d.NotificationId, n => n.Id, (d, n) => n.Category)
            .GroupBy(c => c).Select(g => new { Category = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var byCategory = createdByCategory.Select(c => c.Category).Union(failedByCategory.Select(f => f.Category)).Distinct()
            .Select(category => new CategoryCountRow(
                category,
                createdByCategory.FirstOrDefault(c => c.Category == category)?.Count ?? 0,
                failedByCategory.FirstOrDefault(f => f.Category == category)?.Count ?? 0))
            .OrderByDescending(r => r.Created).ToList();

        var series = await SeriesAsync(created, sent, failed, fromUtc, hourlyBuckets, cancellationToken);

        return new NotificationStatisticsData(
            createdCount, sentCount, failedCount, deadLetteredCount, ambiguousCount, retries, emailSent, emailFailed,
            average, p95, outboxPending, dueCount, oldestDue, unread, byCategory, series);
    }

    /// <summary>
    /// Average and 95th-percentile time from a delivery being created to it being sent, in milliseconds. The percentile is the
    /// row at that rank of an ordered query, so the database does the work and no latency list is loaded.
    /// </summary>
    private static async Task<(long? Average, long? P95)> LatenciesAsync(IQueryable<NotificationDelivery> sent, CancellationToken cancellationToken)
    {
        // Seconds, not milliseconds: DATEDIFF in milliseconds overflows an int after about 24 days, and a retried delivery can take longer.
        var seconds = sent.Select(d => EF.Functions.DateDiffSecond(d.CreatedAtUtc, d.SentAtUtc!.Value));
        var count = await seconds.CountAsync(cancellationToken);
        if (count == 0)
        {
            return (null, null);
        }

        var average = await seconds.AverageAsync(s => (double)s, cancellationToken);
        var rank = Math.Max(0, (int)Math.Ceiling(0.95 * count) - 1);
        var p95 = await seconds.OrderBy(s => s).Skip(rank).FirstAsync(cancellationToken);
        return ((long)Math.Round(average * 1000), p95 * 1000L);
    }

    private static async Task<IReadOnlyList<SeriesRow>> SeriesAsync(
        IQueryable<Notification> created,
        IQueryable<NotificationDelivery> sent,
        IQueryable<NotificationDelivery> failed,
        DateTime fromUtc,
        bool hourly,
        CancellationToken cancellationToken)
    {
        // Buckets are whole hours or days counted from the window's start, so a bucket's index is a plain integer the database can group by.
        var createdBuckets = hourly
            ? await created.GroupBy(n => EF.Functions.DateDiffHour(fromUtc, n.CreatedAtUtc)).Select(g => new { Bucket = g.Key, Count = g.Count() }).ToListAsync(cancellationToken)
            : await created.GroupBy(n => EF.Functions.DateDiffDay(fromUtc, n.CreatedAtUtc)).Select(g => new { Bucket = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var sentBuckets = hourly
            ? await sent.GroupBy(d => EF.Functions.DateDiffHour(fromUtc, d.SentAtUtc!.Value)).Select(g => new { Bucket = g.Key, Count = g.Count() }).ToListAsync(cancellationToken)
            : await sent.GroupBy(d => EF.Functions.DateDiffDay(fromUtc, d.SentAtUtc!.Value)).Select(g => new { Bucket = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var failedBuckets = hourly
            ? await failed.GroupBy(d => EF.Functions.DateDiffHour(fromUtc, d.LastAttemptAtUtc!.Value)).Select(g => new { Bucket = g.Key, Count = g.Count() }).ToListAsync(cancellationToken)
            : await failed.GroupBy(d => EF.Functions.DateDiffDay(fromUtc, d.LastAttemptAtUtc!.Value)).Select(g => new { Bucket = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);

        return createdBuckets.Select(b => b.Bucket).Union(sentBuckets.Select(b => b.Bucket)).Union(failedBuckets.Select(b => b.Bucket))
            .Distinct().Order()
            .Select(bucket => new SeriesRow(
                hourly ? fromUtc.AddHours(bucket) : fromUtc.AddDays(bucket),
                createdBuckets.FirstOrDefault(b => b.Bucket == bucket)?.Count ?? 0,
                sentBuckets.FirstOrDefault(b => b.Bucket == bucket)?.Count ?? 0,
                failedBuckets.FirstOrDefault(b => b.Bucket == bucket)?.Count ?? 0))
            .ToList();
    }

    // ---- deliveries ----

    /// <summary>A delivery with its notification. Filters and ordering run on these entities; the row for the screen is projected last.</summary>
    private sealed class DeliveryJoin
    {
        public required NotificationDelivery Delivery { get; init; }

        public required Notification Notification { get; init; }
    }

    private IQueryable<DeliveryJoin> Joined() =>
        from d in Deliveries()
        join n in Notifications() on d.NotificationId equals n.Id
        select new DeliveryJoin { Delivery = d, Notification = n };

    private IQueryable<AdminDeliveryRow> ToRows(IQueryable<DeliveryJoin> joined) =>
        from j in joined
        join u in dbContext.Users.IgnoreQueryFilters().AsNoTracking() on j.Notification.RecipientUserId equals u.Id into users
        from u in users.DefaultIfEmpty()
        select new AdminDeliveryRow(
            j.Delivery.Id,
            j.Notification.Id,
            j.Notification.Type,
            j.Notification.Category,
            j.Delivery.Channel,
            j.Delivery.Status,
            j.Delivery.FailureKind,
            j.Delivery.FailureReason,
            j.Delivery.ProviderResponse,
            j.Delivery.AttemptCount,
            j.Delivery.LastAttemptAtUtc,
            j.Delivery.NextAttemptAtUtc,
            j.Delivery.RecipientKind,
            j.Notification.RecipientUserId,
            u == null ? null : ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim(),
            j.Delivery.RecipientKind == RecipientKind.User ? (u == null ? null : u.Email) : j.Delivery.RecipientAddress,
            u != null && u.IsDeleted,
            j.Delivery.CorrelationId,
            j.Notification.Title,
            j.Notification.Language,
            j.Delivery.TemplateVersionId,
            j.Notification.CreatedAtUtc,
            j.Notification.DeletedAtUtc != null,
            j.Notification.ExpiresAtUtc,
            j.Delivery.ExpiresAtUtc);

    private static IQueryable<DeliveryJoin> Filter(IQueryable<DeliveryJoin> rows, AdminDeliveryFilter filter)
    {
        var statuses = filter.Statuses.ToList();
        rows = rows.Where(j => statuses.Contains(j.Delivery.Status));
        if (filter.Channel is { } channel)
        {
            rows = rows.Where(j => j.Delivery.Channel == channel);
        }

        if (filter.Category is { } category)
        {
            rows = rows.Where(j => j.Notification.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(filter.Type))
        {
            var type = filter.Type;
            rows = rows.Where(j => j.Notification.Type == type);
        }

        if (filter.FromUtc is { } from)
        {
            rows = rows.Where(j => j.Delivery.LastAttemptAtUtc >= from);
        }

        if (filter.ToUtc is { } to)
        {
            rows = rows.Where(j => j.Delivery.LastAttemptAtUtc <= to);
        }

        return rows;
    }

    public async Task<(IReadOnlyList<AdminDeliveryRow> Items, string? NextCursor)> ListDeliveriesAsync(
        AdminDeliveryFilter filter, string? cursor, int limit, CancellationToken cancellationToken)
    {
        var joined = Filter(Joined(), filter);

        // Newest attempt first; a delivery never attempted sorts by when its notification was created.
        if (NotificationCursor.Decode(cursor) is { } position)
        {
            var (at, id) = position;
            joined = joined.Where(j => (j.Delivery.LastAttemptAtUtc ?? j.Notification.CreatedAtUtc) < at
                || ((j.Delivery.LastAttemptAtUtc ?? j.Notification.CreatedAtUtc) == at && j.Delivery.Id.CompareTo(id) < 0));
        }

        var page = await ToRows(joined
                .OrderByDescending(j => j.Delivery.LastAttemptAtUtc ?? j.Notification.CreatedAtUtc)
                .ThenByDescending(j => j.Delivery.Id)
                .Take(limit + 1))
            .ToListAsync(cancellationToken);

        // The projection join doesn't promise an order, so it is restored.
        page = [.. page.OrderByDescending(r => r.LastAttemptAtUtc ?? r.NotificationCreatedAtUtc).ThenByDescending(r => r.DeliveryId)];
        var hasMore = page.Count > limit;
        var items = hasMore ? page[..limit] : page;
        var last = items.Count == 0 ? null : items[^1];
        return (items, hasMore && last is not null ? NotificationCursor.Encode(last.LastAttemptAtUtc ?? last.NotificationCreatedAtUtc, last.DeliveryId) : null);
    }

    public Task<AdminDeliveryRow?> GetDeliveryAsync(Guid deliveryId, CancellationToken cancellationToken) =>
        ToRows(Joined().Where(j => j.Delivery.Id == deliveryId)).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> FindDeliveryIdsAsync(AdminDeliveryFilter filter, int max, CancellationToken cancellationToken) =>
        await Filter(Joined(), filter)
            .OrderByDescending(j => j.Delivery.LastAttemptAtUtc ?? j.Notification.CreatedAtUtc)
            .ThenByDescending(j => j.Delivery.Id)
            .Take(max)
            .Select(j => j.Delivery.Id)
            .ToListAsync(cancellationToken);

    // ---- audit ----

    public async Task<(IReadOnlyList<AdminAuditRow> Items, string? NextCursor)> ListAuditAsync(
        AdminAuditFilter filter, string? cursor, int limit, CancellationToken cancellationToken)
    {
        var entries = dbContext.NotificationAuditLogs.IgnoreQueryFilters().AsNoTracking().AsQueryable();
        if (filter.Action is { } action)
        {
            entries = entries.Where(a => a.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(filter.TargetType))
        {
            var targetType = filter.TargetType;
            entries = entries.Where(a => a.TargetType == targetType);
        }

        if (!string.IsNullOrWhiteSpace(filter.TargetId))
        {
            var targetId = filter.TargetId;
            entries = entries.Where(a => a.TargetId == targetId);
        }

        if (!string.IsNullOrWhiteSpace(filter.ActorUserId))
        {
            var actor = filter.ActorUserId;
            entries = entries.Where(a => a.ActorUserId == actor);
        }

        if (filter.FromUtc is { } from)
        {
            entries = entries.Where(a => a.OccurredAtUtc >= from);
        }

        if (filter.ToUtc is { } to)
        {
            entries = entries.Where(a => a.OccurredAtUtc <= to);
        }

        if (NotificationCursor.Decode(cursor) is { } position)
        {
            var (at, id) = position;
            entries = entries.Where(a => a.OccurredAtUtc < at || (a.OccurredAtUtc == at && a.Id.CompareTo(id) < 0));
        }

        var page = await (
                from a in entries
                join u in dbContext.Users.IgnoreQueryFilters().AsNoTracking() on a.ActorUserId equals u.Id into actors
                from u in actors.DefaultIfEmpty()
                orderby a.OccurredAtUtc descending, a.Id descending
                select new AdminAuditRow(
                    a.Id,
                    a.OccurredAtUtc,
                    a.ActorUserId,
                    u == null ? null : ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim(),
                    a.Action,
                    a.TargetType,
                    a.TargetId,
                    a.Outcome,
                    a.DetailsJson,
                    a.CorrelationId))
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > limit;
        var items = hasMore ? page[..limit] : page;
        return (items, hasMore ? NotificationCursor.Encode(items[^1].OccurredAtUtc, items[^1].Id) : null);
    }

    // ---- announcements ----

    public async Task<(IReadOnlyList<AdminAnnouncementRow> Items, string? NextCursor)> ListAnnouncementsAsync(
        string? cursor, int limit, CancellationToken cancellationToken)
    {
        var announcements = dbContext.SystemAnnouncements.AsNoTracking().AsQueryable();
        if (NotificationCursor.Decode(cursor) is { } position)
        {
            var (at, id) = position;
            announcements = announcements.Where(a => a.PublishedAtUtc < at || (a.PublishedAtUtc == at && a.Id.CompareTo(id) < 0));
        }

        var page = await announcements.OrderByDescending(a => a.PublishedAtUtc).ThenByDescending(a => a.Id).Take(limit + 1).ToListAsync(cancellationToken);
        var hasMore = page.Count > limit;
        var items = hasMore ? page[..limit] : page;

        var ids = items.Select(a => a.Id.ToString()).ToList();
        var itemKeys = ids.ToHashSet(StringComparer.Ordinal);

        // The event that fans an announcement out is keyed by it; it is done once the event is completed.
        var eventKeys = items.Select(a => AnnouncementKeys.EventKey(a.Id)).ToList();
        var completedKeys = (await dbContext.NotificationOutboxEvents.AsNoTracking()
                .Where(e => e.EventKey != null && eventKeys.Contains(e.EventKey) && e.Status == OutboxEventStatus.Completed)
                .Select(e => e.EventKey!).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var emailCounts = await (
                from d in Deliveries()
                join n in Notifications() on d.NotificationId equals n.Id
                where d.Channel == NotificationChannel.Email && n.RelatedItemType == AnnouncementKeys.RelatedItemType && n.RelatedItemId != null && ids.Contains(n.RelatedItemId)
                group d by new { n.RelatedItemId, d.Status } into g
                select new { g.Key.RelatedItemId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var roleIds = items.SelectMany(a => a.TargetRoleIds).Distinct(StringComparer.Ordinal).ToList();
        var roles = roleIds.Count == 0
            ? []
            : await dbContext.Roles.AsNoTracking().Where(r => roleIds.Contains(r.Id)).Select(r => new AdminRoleName(r.Id, r.Name ?? r.Id)).ToListAsync(cancellationToken);
        var publisherIds = items.Select(a => a.PublishedByUserId).Distinct(StringComparer.Ordinal).ToList();
        var publishers = await dbContext.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => publisherIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName })
            .ToListAsync(cancellationToken);

        int Count(Guid id, params DeliveryStatus[] statuses) =>
            emailCounts.Where(c => c.RelatedItemId == id.ToString() && statuses.Contains(c.Status)).Sum(c => c.Count);

        var rows = items.Select(a =>
        {
            var publisher = publishers.FirstOrDefault(p => p.Id == a.PublishedByUserId);
            var name = publisher is null ? null : $"{publisher.FirstName} {publisher.LastName}".Trim();
            return new AdminAnnouncementRow(
                a.Id,
                a.Kind,
                a.Title,
                a.Audience,
                a.TargetRoleIds,
                [.. roles.Where(r => a.TargetRoleIds.Contains(r.Id))],
                a.IsCritical,
                a.EndsAtUtc,
                a.PublishedAtUtc,
                a.PublishedByUserId,
                string.IsNullOrWhiteSpace(name) ? null : name,
                a.RecipientCount,
                completedKeys.Contains(AnnouncementKeys.EventKey(a.Id)),
                Count(a.Id, DeliveryStatus.Pending, DeliveryStatus.Sending, DeliveryStatus.Retrying),
                Count(a.Id, DeliveryStatus.Sent, DeliveryStatus.Delivered),
                Count(a.Id, DeliveryStatus.Expired));
        }).ToList();

        return (rows, hasMore ? NotificationCursor.Encode(items[^1].PublishedAtUtc, items[^1].Id) : null);
    }
}

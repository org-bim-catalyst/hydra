using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class NotificationRepository(AskLucyDbContext dbContext) : INotificationRepository
{
    public void Add(Notification notification) => dbContext.Notifications.Add(notification);

    public Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Notifications
            .Include(n => n.Deliveries)
            .SingleOrDefaultAsync(n => n.Id == id, cancellationToken);

    // The de-duplication reads ignore the soft-delete filter: a notification its owner deleted still
    // counts, so replaying the event never brings it back (FR-008, FR-016a).
    public async Task<IReadOnlySet<string>> GetRecipientsWithEventKeyAsync(
        string eventKey, IReadOnlyCollection<string> recipientUserIds, CancellationToken cancellationToken)
    {
        if (recipientUserIds.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var existing = await dbContext.Notifications
            .IgnoreQueryFilters()
            .Where(n => n.EventKey == eventKey && n.RecipientUserId != null && recipientUserIds.Contains(n.RecipientUserId))
            .Select(n => n.RecipientUserId!)
            .ToListAsync(cancellationToken);

        return existing.ToHashSet(StringComparer.Ordinal);
    }

    public Task<bool> ExistsForAddressAsync(string eventKey, CancellationToken cancellationToken) =>
        dbContext.Notifications
            .IgnoreQueryFilters()
            .AnyAsync(n => n.EventKey == eventKey && n.RecipientUserId == null, cancellationToken);

    public async Task<IReadOnlyDictionary<string, int>> CountUnreadAsync(
        IReadOnlyCollection<string> userIds, CancellationToken cancellationToken)
    {
        var counts = userIds.Distinct(StringComparer.Ordinal).ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        if (counts.Count == 0)
        {
            return counts;
        }

        var rows = await dbContext.Notifications
            .Where(n => n.RecipientUserId != null
                && userIds.Contains(n.RecipientUserId)
                && n.ShowInCenter
                && n.ReadAtUtc == null)
            .GroupBy(n => n.RecipientUserId!)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            counts[row.UserId] = row.Count;
        }

        return counts;
    }

    public Task<int> CountUnreadAsync(string userId, CancellationToken cancellationToken) =>
        dbContext.Notifications
            .Where(n => n.RecipientUserId == userId && n.ShowInCenter && n.ReadAtUtc == null)
            .CountAsync(cancellationToken);

    public async Task<(IReadOnlyList<Notification> Items, string? NextCursor)> ListAsync(
        string userId,
        IReadOnlyCollection<NotificationCategory>? categories,
        NotificationReadState state,
        string? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Notifications.Where(n => n.RecipientUserId == userId && n.ShowInCenter);

        if (categories is { Count: > 0 })
        {
            query = query.Where(n => categories.Contains(n.Category));
        }

        query = state switch
        {
            NotificationReadState.Unread => query.Where(n => n.ReadAtUtc == null),
            NotificationReadState.Read => query.Where(n => n.ReadAtUtc != null),
            _ => query,
        };

        var decodedCursor = NotificationCursor.Decode(cursor);
        if (decodedCursor is { } c)
        {
            query = query.Where(n =>
                n.CreatedAtUtc < c.CreatedAtUtc || (n.CreatedAtUtc == c.CreatedAtUtc && n.Id < c.Id));
        }

        var page = await query
            .OrderByDescending(n => n.CreatedAtUtc)
            .ThenByDescending(n => n.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasNextPage = page.Count > limit;
        var items = hasNextPage ? page.GetRange(0, limit) : page;

        string? nextCursor = null;
        if (hasNextPage && items.Count > 0)
        {
            var last = items[^1];
            nextCursor = NotificationCursor.Encode(last.CreatedAtUtc, last.Id);
        }

        return (items, nextCursor);
    }

    // Set-based (FR contract: "mark-all-read... one set-based update of the caller's rows only"),
    // so it bypasses Notification.MarkRead — mirrors the eligibility that method enforces
    // (Delivered/Sent only) directly in the predicate instead.
    public Task<int> MarkAllReadAsync(string userId, NotificationCategory? category, DateTime now, CancellationToken cancellationToken)
    {
        var query = dbContext.Notifications.Where(n =>
            n.RecipientUserId == userId &&
            n.ShowInCenter &&
            n.ReadAtUtc == null &&
            (n.Status == NotificationStatus.Delivered || n.Status == NotificationStatus.Sent));

        if (category is { } c)
        {
            query = query.Where(n => n.Category == c);
        }

        return query.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(n => n.ReadAtUtc, now)
                .SetProperty(n => n.Status, NotificationStatus.Read),
            cancellationToken);
    }

    // Account deletion (T099): ignores the soft-delete filter, so an owner-deleted notification is
    // purged too, and bypasses NotificationDelivery's "child of Notification" access rule via the
    // FK cascade (NotificationConfiguration: DeleteBehavior.Cascade) rather than loading deliveries.
    public Task<int> DeleteAllForUserAsync(string userId, CancellationToken cancellationToken) =>
        dbContext.Notifications
            .IgnoreQueryFilters()
            .Where(n => n.RecipientUserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

    // --- delivery queue (specs/067 research R4, R5) ---
    // NotificationDelivery has no DbSet (constitution §5), so the queue is reached through Set<>.

    private DbSet<NotificationDelivery> Deliveries() => dbContext.Set<NotificationDelivery>();

    private static readonly NotificationPriority[] PriorityHighToLow =
        [NotificationPriority.Critical, NotificationPriority.High, NotificationPriority.Normal, NotificationPriority.Low];

    // Priority is stored as a string, so its alphabetical order is meaningless; the queue is read one
    // priority at a time instead, highest first, each ordered by due time. A password reset (Critical) is
    // therefore never behind a backlog of announcements (SC-014), however deep that backlog is.
    public async Task<IReadOnlyList<Guid>> ClaimDueDeliveriesAsync(
        string workerId,
        IReadOnlyCollection<NotificationChannel> channels,
        DateTime leaseExpiresAtUtc,
        int batchSize,
        DateTime now,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        var channelList = channels.ToList();
        var claimed = new List<Guid>();
        if (channelList.Count == 0 || batchSize < 1)
        {
            return claimed;
        }

        foreach (var priority in PriorityHighToLow)
        {
            var remaining = batchSize - claimed.Count;
            if (remaining <= 0)
            {
                break;
            }

            var candidates = await Due(Deliveries().AsNoTracking(), channelList, now)
                .Where(d => d.Priority == priority)
                .OrderBy(d => d.NextAttemptAtUtc)
                .Select(d => d.Id)
                .Take(remaining)
                .ToListAsync(cancellationToken);

            foreach (var id in candidates)
            {
                // Re-checks the due predicate, so a row another worker took since the read is left alone.
                var affected = await Due(Deliveries().Where(d => d.Id == id), channelList, now)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(d => d.Status, DeliveryStatus.Sending)
                        .SetProperty(d => d.AttemptCount, d => d.AttemptCount + 1)
                        .SetProperty(d => d.LastAttemptAtUtc, now)
                        .SetProperty(d => d.LeaseOwner, workerId)
                        .SetProperty(d => d.LeaseExpiresAtUtc, leaseExpiresAtUtc), cancellationToken);

                if (affected == 1)
                {
                    claimed.Add(id);
                }
            }
        }

        return claimed;
    }

    public Task<Notification?> GetClaimedDeliveryAsync(Guid deliveryId, string workerId, CancellationToken cancellationToken) =>
        dbContext.Notifications
            .IgnoreQueryFilters()
            .Include(n => n.Deliveries)
            .Where(n => n.Deliveries.Any(d =>
                d.Id == deliveryId && d.Status == DeliveryStatus.Sending && d.LeaseOwner == workerId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<int> ReleaseClaimsAsync(string workerId, IReadOnlyCollection<Guid> deliveryIds, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        if (deliveryIds.Count == 0)
        {
            return 0;
        }

        var ids = deliveryIds.ToList();

        // Not a failed attempt, so the attempt the claim counted is returned. A delivery that had already
        // failed once goes back to retrying, and a first attempt back to pending.
        var affected = await Deliveries()
            .Where(d => ids.Contains(d.Id) && d.Status == DeliveryStatus.Sending && d.LeaseOwner == workerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, d => d.AttemptCount > 1 ? DeliveryStatus.Retrying : DeliveryStatus.Pending)
                .SetProperty(d => d.AttemptCount, d => d.AttemptCount > 0 ? d.AttemptCount - 1 : 0)
                .SetProperty(d => d.NextAttemptAtUtc, now)
                .SetProperty(d => d.LeaseOwner, (string?)null)
                .SetProperty(d => d.LeaseExpiresAtUtc, (DateTime?)null), cancellationToken);

        await RefreshQueuedStatusAsync(ids, cancellationToken);
        return affected;
    }

    public async Task<int> SweepExpiredLeasesAsync(DateTime now, CancellationToken cancellationToken)
    {
        var expired = Deliveries().Where(d => d.Status == DeliveryStatus.Sending && d.LeaseExpiresAtUtc <= now);
        var notificationIds = await expired.Select(d => d.NotificationId).Distinct().ToListAsync(cancellationToken);
        if (notificationIds.Count == 0)
        {
            return 0;
        }

        // A send that crashed mid-flight may or may not have reached the recipient, so it is recorded as
        // ambiguous and left for an administrator: resending it automatically could duplicate it (R5).
        var swept = await expired.ExecuteUpdateAsync(s => s
            .SetProperty(d => d.Status, DeliveryStatus.Failed)
            .SetProperty(d => d.FailureKind, DeliveryFailureKind.AmbiguousOutcome)
            .SetProperty(d => d.FailureReason, "The worker stopped while sending, so it is unknown whether the message was delivered.")
            .SetProperty(d => d.NextAttemptAtUtc, (DateTime?)null)
            .SetProperty(d => d.LeaseOwner, (string?)null)
            .SetProperty(d => d.LeaseExpiresAtUtc, (DateTime?)null), cancellationToken);

        // Only a notification that had nothing delivered, sent or still waiting becomes Failed; one the
        // user already has in the center stays Delivered (Notification.RecomputeStatus precedence). A claim
        // doesn't touch the parent, so the notification still reads Queued, not Processing.
        await dbContext.Notifications
            .IgnoreQueryFilters()
            .Where(n => notificationIds.Contains(n.Id)
                && (n.Status == NotificationStatus.Processing || n.Status == NotificationStatus.Queued)
                && !n.Deliveries.Any(d => d.Status == DeliveryStatus.Sending
                    || d.Status == DeliveryStatus.Pending
                    || d.Status == DeliveryStatus.Retrying
                    || d.Status == DeliveryStatus.Sent
                    || d.Status == DeliveryStatus.Delivered))
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, NotificationStatus.Failed), cancellationToken);

        return swept;
    }

    public async Task<DeliveryBacklog> GetDueBacklogAsync(DateTime now, CancellationToken cancellationToken)
    {
        var due = Deliveries().AsNoTracking()
            .Where(d => (d.Status == DeliveryStatus.Pending || d.Status == DeliveryStatus.Retrying) && d.NextAttemptAtUtc <= now);

        var count = await due.CountAsync(cancellationToken);
        var oldest = count == 0 ? null : await due.MinAsync(d => d.NextAttemptAtUtc, cancellationToken);
        return new DeliveryBacklog(count, oldest);
    }

    private static IQueryable<NotificationDelivery> Due(IQueryable<NotificationDelivery> source, List<NotificationChannel> channels, DateTime now) =>
        source.Where(d => (d.Status == DeliveryStatus.Pending || d.Status == DeliveryStatus.Retrying)
            && d.NextAttemptAtUtc <= now
            && channels.Contains(d.Channel));

    /// <summary>A released delivery is waiting again, so its notification reads Queued rather than Processing.</summary>
    private Task<int> RefreshQueuedStatusAsync(List<Guid> deliveryIds, CancellationToken cancellationToken) =>
        dbContext.Notifications
            .IgnoreQueryFilters()
            .Where(n => n.Status == NotificationStatus.Processing
                && n.Deliveries.Any(d => deliveryIds.Contains(d.Id))
                && !n.Deliveries.Any(d => d.Status == DeliveryStatus.Sending)
                && n.Deliveries.Any(d => d.Status == DeliveryStatus.Pending || d.Status == DeliveryStatus.Retrying))
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, NotificationStatus.Queued), cancellationToken);
}

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
}

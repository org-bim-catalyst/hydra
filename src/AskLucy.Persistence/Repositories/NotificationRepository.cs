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
}

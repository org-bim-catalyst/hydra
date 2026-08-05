using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class DocumentNotificationRepository(AskLucyDbContext dbContext) : IDocumentNotificationRepository
{
    public void Add(DocumentNotification notification) => dbContext.DocumentNotifications.Add(notification);

    public Task<DocumentNotification?> GetByIdAsync(Guid id, string userId, CancellationToken cancellationToken = default) =>
        dbContext.DocumentNotifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);

    public async Task<(IReadOnlyList<DocumentNotification> Items, string? NextCursor)> ListForUserAsync(
        string userId, bool unreadOnly, string? cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = dbContext.DocumentNotifications.Where(n => n.UserId == userId);

        if (unreadOnly)
        {
            query = query.Where(n => !n.IsRead);
        }

        var decodedCursor = DocumentCursor.Decode(cursor);
        if (decodedCursor is { } c)
        {
            query = query.Where(n => n.CreatedAtUtc < c.CreatedAtUtc || (n.CreatedAtUtc == c.CreatedAtUtc && n.Id < c.Id));
        }

        var page = await query
            .OrderByDescending(n => n.CreatedAtUtc)
            .ThenByDescending(n => n.Id)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasNextPage = page.Count > pageSize;
        var items = hasNextPage ? page.GetRange(0, pageSize) : page;

        string? nextCursor = null;
        if (hasNextPage && items.Count > 0)
        {
            var last = items[^1];
            nextCursor = DocumentCursor.Encode(last.CreatedAtUtc, last.Id);
        }

        return (items, nextCursor);
    }
}

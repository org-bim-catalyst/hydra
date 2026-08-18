using AskLucy.Application.Abstractions;
using AskLucy.Domain.Memory;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class MemoryNotificationRepository(AskLucyDbContext dbContext) : IMemoryNotificationRepository
{
    private const string AnonymizedUserId = "deleted-user";


    public Task<MemoryNotification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.MemoryNotifications.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MemoryNotification>> GetByUserAsync(string userId, Guid? afterId, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = dbContext.MemoryNotifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAtUtc)
            .AsQueryable();

        var items = await query.ToListAsync(cancellationToken);

        if (afterId is not null)
        {
            var afterIndex = items.FindIndex(n => n.Id == afterId);
            items = afterIndex >= 0 ? items.Skip(afterIndex + 1).ToList() : items;
        }

        return items.Take(pageSize).ToList();
    }

    public void Add(MemoryNotification notification) => dbContext.MemoryNotifications.Add(notification);

    public async Task AnonymizeUserAsync(string userId, CancellationToken cancellationToken = default) =>
        await dbContext.MemoryNotifications
            .Where(n => n.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.UserId, AnonymizedUserId), cancellationToken);
}

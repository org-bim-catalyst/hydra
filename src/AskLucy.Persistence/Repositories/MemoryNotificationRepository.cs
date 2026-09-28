using AskLucy.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class MemoryNotificationRepository(AskLucyDbContext dbContext) : IMemoryNotificationRepository
{
    public Task DeleteAllForUserAsync(string userId, CancellationToken cancellationToken = default) =>
        dbContext.MemoryNotifications.Where(n => n.UserId == userId).ExecuteDeleteAsync(cancellationToken);
}

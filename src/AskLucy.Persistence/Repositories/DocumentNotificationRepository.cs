using AskLucy.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class DocumentNotificationRepository(AskLucyDbContext dbContext) : IDocumentNotificationRepository
{
    public Task DeleteAllForUserAsync(string userId, CancellationToken cancellationToken = default) =>
        dbContext.DocumentNotifications.Where(n => n.UserId == userId).ExecuteDeleteAsync(cancellationToken);
}

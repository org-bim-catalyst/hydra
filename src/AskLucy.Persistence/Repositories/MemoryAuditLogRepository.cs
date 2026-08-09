using AskLucy.Application.Abstractions;
using AskLucy.Domain.Memory;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class MemoryAuditLogRepository(AskLucyDbContext dbContext) : IMemoryAuditLogRepository
{
    private const string AnonymizedUserId = "deleted-user";

    public void Add(MemoryAuditLog entry) => dbContext.MemoryAuditLogs.Add(entry);

    public async Task AnonymizeUserAsync(string userId, CancellationToken cancellationToken = default) =>
        await dbContext.MemoryAuditLogs
            .Where(a => a.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.UserId, AnonymizedUserId), cancellationToken);
}

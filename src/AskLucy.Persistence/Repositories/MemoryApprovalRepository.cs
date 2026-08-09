using AskLucy.Application.Abstractions;
using AskLucy.Domain.Memory;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class MemoryApprovalRepository(AskLucyDbContext dbContext) : IMemoryApprovalRepository
{
    public Task<MemoryApproval?> GetByMemoryIdAsync(Guid memoryId, CancellationToken cancellationToken = default) =>
        dbContext.MemoryApprovals.FirstOrDefaultAsync(a => a.MemoryId == memoryId, cancellationToken);

    public void Add(MemoryApproval approval) => dbContext.MemoryApprovals.Add(approval);
}

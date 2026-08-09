using AskLucy.Application.Abstractions;
using AskLucy.Domain.Memory;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class MemoryConflictRepository(AskLucyDbContext dbContext) : IMemoryConflictRepository
{
    public Task<MemoryConflict?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.MemoryConflicts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<MemoryConflict?> GetOpenByMemoryIdAsync(Guid memoryId, CancellationToken cancellationToken = default) =>
        dbContext.MemoryConflicts.FirstOrDefaultAsync(
            c => (c.ExistingMemoryId == memoryId || c.NewMemoryId == memoryId)
                && c.ResolutionStatus == MemoryConflictResolutionStatus.PendingUserConfirmation,
            cancellationToken);

    public void Add(MemoryConflict conflict) => dbContext.MemoryConflicts.Add(conflict);
}

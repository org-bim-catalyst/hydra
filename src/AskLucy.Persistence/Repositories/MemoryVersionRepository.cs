using AskLucy.Application.Abstractions;
using AskLucy.Domain.Memory;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class MemoryVersionRepository(AskLucyDbContext dbContext) : IMemoryVersionRepository
{
    public async Task<IReadOnlyList<MemoryVersion>> GetByMemoryIdAsync(Guid memoryId, CancellationToken cancellationToken = default) =>
        await dbContext.MemoryVersions
            .Where(v => v.MemoryId == memoryId)
            .OrderByDescending(v => v.ChangedAtUtc)
            .ToListAsync(cancellationToken);

    public void Add(MemoryVersion version) => dbContext.MemoryVersions.Add(version);
}

using AskLucy.Application.Abstractions;
using AskLucy.Domain.Memory;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class MemoryEmbeddingRepository(AskLucyDbContext dbContext) : IMemoryEmbeddingRepository
{
    public Task<MemoryEmbedding?> GetCurrentByMemoryIdAsync(Guid memoryId, CancellationToken cancellationToken = default) =>
        dbContext.MemoryEmbeddings.FirstOrDefaultAsync(e => e.MemoryId == memoryId && e.IsCurrent, cancellationToken);

    public void Add(MemoryEmbedding embedding) => dbContext.MemoryEmbeddings.Add(embedding);
}

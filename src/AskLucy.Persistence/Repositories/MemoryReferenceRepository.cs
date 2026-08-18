using AskLucy.Application.Abstractions;
using AskLucy.Domain.Memory;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class MemoryReferenceRepository(AskLucyDbContext dbContext) : IMemoryReferenceRepository
{
    public async Task<IReadOnlyList<MemoryReference>> GetByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        await dbContext.MemoryReferences.Where(r => r.MessageId == messageId).ToListAsync(cancellationToken);

    public void Add(MemoryReference reference) => dbContext.MemoryReferences.Add(reference);

    public void AddRange(IEnumerable<MemoryReference> references) => dbContext.MemoryReferences.AddRange(references);
}

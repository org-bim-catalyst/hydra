using AskLucy.Application.Abstractions;
using AskLucy.Domain.Retrieval;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class EmbeddingProviderRepository(AskLucyDbContext dbContext) : IEmbeddingProviderRepository
{
    public Task<EmbeddingProvider?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.EmbeddingProviders.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<EmbeddingProvider?> GetDefaultAsync(EmbeddingHostingType hostingType, CancellationToken cancellationToken = default) =>
        dbContext.EmbeddingProviders.FirstOrDefaultAsync(
            p => p.HostingType == hostingType && p.IsDefault && p.IsActive, cancellationToken);

    public async Task<IReadOnlyList<EmbeddingProvider>> GetActiveAsync(CancellationToken cancellationToken = default) =>
        await dbContext.EmbeddingProviders.Where(p => p.IsActive).ToListAsync(cancellationToken);
}

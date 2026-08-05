using AskLucy.Application.Abstractions;
using AskLucy.Domain.Retrieval;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class EmbeddingRepository(AskLucyDbContext dbContext) : IEmbeddingRepository
{
    public Task<Embedding?> GetCurrentAsync(Guid documentChunkId, CancellationToken cancellationToken = default) =>
        dbContext.Embeddings.FirstOrDefaultAsync(e => e.DocumentChunkId == documentChunkId && e.IsCurrent, cancellationToken);

    public void Add(Embedding embedding) => dbContext.Embeddings.Add(embedding);

    public async Task MarkExistingSupersededAsync(Guid documentChunkId, string actor, CancellationToken cancellationToken = default)
    {
        var current = await dbContext.Embeddings
            .Where(e => e.DocumentChunkId == documentChunkId && e.IsCurrent)
            .ToListAsync(cancellationToken);

        foreach (var embedding in current)
        {
            embedding.MarkSuperseded(actor);
        }
    }
}

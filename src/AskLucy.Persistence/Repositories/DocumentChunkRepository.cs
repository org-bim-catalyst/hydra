using AskLucy.Application.Abstractions;
using AskLucy.Domain.Retrieval;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class DocumentChunkRepository(AskLucyDbContext dbContext) : IDocumentChunkRepository
{
    public Task<DocumentChunk?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.DocumentChunks.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<DocumentChunk>> GetByKnowledgeBaseDocumentAsync(Guid knowledgeBaseDocumentId, CancellationToken cancellationToken = default) =>
        await dbContext.DocumentChunks
            .Where(c => c.KnowledgeBaseDocumentId == knowledgeBaseDocumentId)
            .OrderBy(c => c.Position)
            .ToListAsync(cancellationToken);

    public Task<DocumentChunk?> FindByContentHashAsync(Guid knowledgeBaseDocumentId, string contentHash, CancellationToken cancellationToken = default) =>
        dbContext.DocumentChunks.FirstOrDefaultAsync(
            c => c.KnowledgeBaseDocumentId == knowledgeBaseDocumentId && c.ContentHash == contentHash, cancellationToken);

    public void Add(DocumentChunk chunk) => dbContext.DocumentChunks.Add(chunk);

    public void AddRange(IEnumerable<DocumentChunk> chunks) => dbContext.DocumentChunks.AddRange(chunks);

    public async Task SoftDeleteByKnowledgeBaseDocumentAsync(Guid knowledgeBaseDocumentId, string actor, CancellationToken cancellationToken = default)
    {
        var chunks = await dbContext.DocumentChunks
            .Where(c => c.KnowledgeBaseDocumentId == knowledgeBaseDocumentId)
            .ToListAsync(cancellationToken);

        foreach (var chunk in chunks)
        {
            chunk.SoftDelete(actor);
        }
    }
}

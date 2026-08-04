using AskLucy.Application.Abstractions;
using AskLucy.Domain.KnowledgeBases;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class KnowledgeBaseDocumentRepository(AskLucyDbContext dbContext) : IKnowledgeBaseDocumentRepository
{
    public Task<KnowledgeBaseDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.KnowledgeBaseDocuments.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public void Add(KnowledgeBaseDocument document) => dbContext.KnowledgeBaseDocuments.Add(document);

    public async Task<IReadOnlyList<KnowledgeBaseDocument>> ListByKnowledgeBaseIdIncludingDeletedAsync(Guid knowledgeBaseId, CancellationToken cancellationToken = default) =>
        await dbContext.KnowledgeBaseDocuments.IgnoreQueryFilters()
            .Where(d => d.KnowledgeBaseId == knowledgeBaseId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<KnowledgeBaseDocument>> ListByFolderAsync(Guid knowledgeBaseId, Guid? folderId, CancellationToken cancellationToken = default) =>
        await dbContext.KnowledgeBaseDocuments
            .Where(d => d.KnowledgeBaseId == knowledgeBaseId && d.FolderId == folderId)
            .OrderBy(d => d.FileName)
            .ToListAsync(cancellationToken);
}

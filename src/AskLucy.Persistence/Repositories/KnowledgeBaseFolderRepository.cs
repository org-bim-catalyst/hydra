using AskLucy.Application.Abstractions;
using AskLucy.Domain.KnowledgeBases;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class KnowledgeBaseFolderRepository(AskLucyDbContext dbContext) : IKnowledgeBaseFolderRepository
{
    public Task<KnowledgeBaseFolder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.KnowledgeBaseFolders.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

    public void Add(KnowledgeBaseFolder folder) => dbContext.KnowledgeBaseFolders.Add(folder);

    public async Task<IReadOnlyList<KnowledgeBaseFolder>> ListByKnowledgeBaseIdAsync(Guid knowledgeBaseId, CancellationToken cancellationToken = default) =>
        await dbContext.KnowledgeBaseFolders.Where(f => f.KnowledgeBaseId == knowledgeBaseId).ToListAsync(cancellationToken);

    /// <summary>Walks up from <paramref name="folderId"/> via `ParentFolderId` (bounded by `MaxNestingDepth`, so this terminates quickly) rather than a recursive SQL CTE — simpler and adequate at this scale (folders per knowledge base are not expected to number in the thousands).</summary>
    public async Task<bool> IsSameOrDescendantAsync(Guid folderId, Guid potentialAncestorId, CancellationToken cancellationToken = default)
    {
        var currentId = (Guid?)folderId;

        while (currentId is not null)
        {
            if (currentId == potentialAncestorId)
            {
                return true;
            }

            currentId = await dbContext.KnowledgeBaseFolders
                .Where(f => f.Id == currentId)
                .Select(f => f.ParentFolderId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return false;
    }

    public async Task<bool> HasContentsAsync(Guid folderId, CancellationToken cancellationToken = default)
    {
        var hasSubfolders = await dbContext.KnowledgeBaseFolders.AnyAsync(f => f.ParentFolderId == folderId, cancellationToken);
        if (hasSubfolders)
        {
            return true;
        }

        return await dbContext.KnowledgeBaseDocuments.AnyAsync(d => d.FolderId == folderId, cancellationToken);
    }
}

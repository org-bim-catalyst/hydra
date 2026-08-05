using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class DocumentFolderRepository(AskLucyDbContext dbContext) : IDocumentFolderRepository
{
    public Task<DocumentFolder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.DocumentFolders.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

    public void Add(DocumentFolder folder) => dbContext.DocumentFolders.Add(folder);

    public async Task<bool> IsSelfOrDescendantAsync(Guid folderId, Guid candidateAncestorId, CancellationToken cancellationToken = default)
    {
        if (folderId == candidateAncestorId)
        {
            return true;
        }

        // Small, bounded tree depth (data-model.md) — walking up from candidateAncestorId is
        // simpler and just as cheap as a recursive CTE at this scale.
        var currentId = (Guid?)candidateAncestorId;
        while (currentId is not null)
        {
            var parentId = await dbContext.DocumentFolders
                .Where(f => f.Id == currentId)
                .Select(f => f.ParentFolderId)
                .FirstOrDefaultAsync(cancellationToken);

            if (parentId == folderId)
            {
                return true;
            }

            currentId = parentId;
        }

        return false;
    }

    public Task<bool> HasDocumentsAsync(Guid folderId, CancellationToken cancellationToken = default) =>
        dbContext.Documents.AnyAsync(d => d.FolderId == folderId, cancellationToken);

    public void Remove(DocumentFolder folder) => dbContext.DocumentFolders.Remove(folder);

    public async Task<IReadOnlyList<DocumentFolder>> ListByOwnerAsync(string ownerId, CancellationToken cancellationToken = default) =>
        await dbContext.DocumentFolders.Where(f => f.OwnerId == ownerId).OrderBy(f => f.Depth).ThenBy(f => f.Name).ToListAsync(cancellationToken);
}

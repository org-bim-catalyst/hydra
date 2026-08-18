using AskLucy.Application.Abstractions;
using AskLucy.Domain.Prompts;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class PromptFolderRepository(AskLucyDbContext dbContext) : IPromptFolderRepository
{
    public Task<PromptFolder?> GetByIdForOwnerAsync(Guid id, string ownerId, CancellationToken cancellationToken = default) =>
        dbContext.PromptFolders.FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == ownerId, cancellationToken);

    public void Add(PromptFolder folder) => dbContext.PromptFolders.Add(folder);

    public async Task<IReadOnlyList<PromptFolder>> GetTreeForOwnerAsync(string ownerId, CancellationToken cancellationToken = default) =>
        await dbContext.PromptFolders.Where(f => f.OwnerId == ownerId).ToListAsync(cancellationToken);

    /// <summary>Walks up from <paramref name="folderId"/> via `ParentFolderId` — mirrors `KnowledgeBaseFolderRepository.IsSameOrDescendantAsync` exactly (research.md Decision 5).</summary>
    public async Task<bool> IsSameOrDescendantAsync(Guid folderId, Guid potentialAncestorId, CancellationToken cancellationToken = default)
    {
        var currentId = (Guid?)folderId;

        while (currentId is not null)
        {
            if (currentId == potentialAncestorId)
            {
                return true;
            }

            currentId = await dbContext.PromptFolders
                .Where(f => f.Id == currentId)
                .Select(f => f.ParentFolderId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return false;
    }
}

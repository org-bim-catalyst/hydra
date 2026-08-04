using AskLucy.Application.Abstractions;
using AskLucy.Domain.KnowledgeBases;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class KnowledgeBaseCategoryRepository(AskLucyDbContext dbContext) : IKnowledgeBaseCategoryRepository
{
    public Task<KnowledgeBaseCategory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.KnowledgeBaseCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public void Add(KnowledgeBaseCategory category) => dbContext.KnowledgeBaseCategories.Add(category);

    public void Remove(KnowledgeBaseCategory category) => dbContext.KnowledgeBaseCategories.Remove(category);

    public async Task<IReadOnlyList<KnowledgeBaseCategory>> ListPredefinedAndOwnedAsync(string ownerId, CancellationToken cancellationToken = default) =>
        await dbContext.KnowledgeBaseCategories
            .Where(c => c.OwnerId == null || c.OwnerId == ownerId)
            .OrderBy(c => c.OwnerId == null ? 0 : 1)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

    public async Task<bool> ExistsByNameForOwnerAsync(string ownerId, string name, CancellationToken cancellationToken = default) =>
        await dbContext.KnowledgeBaseCategories.AnyAsync(
            c => c.OwnerId == ownerId && c.Name.ToLower() == name.ToLower(), cancellationToken);
}

using AskLucy.Application.Abstractions;
using AskLucy.Domain.Prompts;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class PromptCategoryRepository(AskLucyDbContext dbContext) : IPromptCategoryRepository
{
    public Task<PromptCategory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.PromptCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PromptCategory>> ListPredefinedAndCustomForOwnerAsync(string ownerId, CancellationToken cancellationToken = default) =>
        await dbContext.PromptCategories
            .Where(c => c.OwnerId == null || c.OwnerId == ownerId)
            .OrderBy(c => c.OwnerId == null ? 0 : 1)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

    // ToLower() runs inside an EF Core expression tree translated to SQL LOWER(); the
    // StringComparison/CultureInfo overloads CA1304/CA1311/CA1862 suggest cannot be
    // translated and would throw at query execution time.
#pragma warning disable CA1304, CA1311, CA1862
    public Task<PromptCategory?> GetCustomByOwnerAndNameAsync(string ownerId, string name, CancellationToken cancellationToken = default) =>
        dbContext.PromptCategories.FirstOrDefaultAsync(
            c => c.OwnerId == ownerId && c.Name.ToLower() == name.ToLower(), cancellationToken);
#pragma warning restore CA1304, CA1311, CA1862

    public void Add(PromptCategory category) => dbContext.PromptCategories.Add(category);
}

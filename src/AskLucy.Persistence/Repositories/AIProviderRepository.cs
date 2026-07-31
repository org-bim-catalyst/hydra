using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class AIProviderRepository(AskLucyDbContext dbContext) : IAIProviderRepository
{
    public Task<AIProvider?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.AIProviders.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<AIProvider?> GetByKeyAsync(string providerKey, CancellationToken cancellationToken = default) =>
        dbContext.AIProviders.FirstOrDefaultAsync(p => p.ProviderKey == providerKey, cancellationToken);

    public async Task<IReadOnlyList<AIProvider>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.AIProviders.OrderBy(p => p.DisplayName).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AIProvider>> ListEnabledAsync(CancellationToken cancellationToken = default) =>
        await dbContext.AIProviders.Where(p => p.IsEnabled).OrderBy(p => p.DisplayName).ToListAsync(cancellationToken);

    public void Add(AIProvider provider) => dbContext.AIProviders.Add(provider);
}

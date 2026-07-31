using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class AIModelRepository(AskLucyDbContext dbContext) : IAIModelRepository
{
    public Task<AIModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.AIModels.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<IReadOnlyList<AIModel>> ListByProviderIdAsync(Guid providerId, CancellationToken cancellationToken = default) =>
        await dbContext.AIModels
            .Where(m => m.ProviderId == providerId)
            .OrderBy(m => m.DisplayName)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AIModel>> ListAvailableByProviderIdAsync(Guid providerId, CancellationToken cancellationToken = default) =>
        await dbContext.AIModels
            .Where(m => m.ProviderId == providerId && m.Status == AIModelStatus.Available)
            .OrderBy(m => m.DisplayName)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AIModel>> ListAvailableAsync(CancellationToken cancellationToken = default) =>
        await dbContext.AIModels
            .Where(m => m.Status == AIModelStatus.Available && dbContext.AIProviders.Any(p => p.Id == m.ProviderId && p.IsEnabled))
            .OrderBy(m => m.DisplayName)
            .ToListAsync(cancellationToken);

    public void Add(AIModel model) => dbContext.AIModels.Add(model);
}

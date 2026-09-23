using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class VoiceProviderRepository(AskLucyDbContext dbContext) : IVoiceProviderRepository
{
    public Task<VoiceProvider?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.VoiceProviders.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<VoiceProvider?> GetByKeyAsync(string providerKey, CancellationToken cancellationToken = default) =>
        dbContext.VoiceProviders.FirstOrDefaultAsync(p => p.ProviderKey == providerKey, cancellationToken);

    public async Task<IReadOnlyList<VoiceProvider>> ListByPriorityAsync(CancellationToken cancellationToken = default) =>
        await dbContext.VoiceProviders.OrderBy(p => p.Priority).ThenBy(p => p.CreatedAtUtc).ToListAsync(cancellationToken);

    public void Add(VoiceProvider provider) => dbContext.VoiceProviders.Add(provider);
}

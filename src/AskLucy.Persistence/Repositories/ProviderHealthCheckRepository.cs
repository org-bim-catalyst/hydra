using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class ProviderHealthCheckRepository(AskLucyDbContext dbContext) : IProviderHealthCheckRepository
{
    public async Task<IReadOnlyList<ProviderHealthCheck>> ListRecentByProviderIdAsync(
        Guid providerId, int take, CancellationToken cancellationToken = default) =>
        await dbContext.ProviderHealthChecks
            .Where(h => h.ProviderId == providerId)
            .OrderByDescending(h => h.CheckedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public void Add(ProviderHealthCheck check) => dbContext.ProviderHealthChecks.Add(check);
}

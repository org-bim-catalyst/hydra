using AskLucy.Application.Abstractions;
using AskLucy.Domain.Appearance;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class PresenceSphereSettingsRepository(AskLucyDbContext dbContext) : IPresenceSphereSettingsRepository
{
    public Task<PresenceSphereSettings?> GetAsync(CancellationToken cancellationToken = default) =>
        dbContext.PresenceSphereSettings.FirstOrDefaultAsync(s => s.Id == PresenceSphereSettings.SingletonId, cancellationToken);

    public void Add(PresenceSphereSettings settings) => dbContext.PresenceSphereSettings.Add(settings);
}

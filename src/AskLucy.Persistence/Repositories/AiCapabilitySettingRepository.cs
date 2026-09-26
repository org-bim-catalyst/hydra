using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class AiCapabilitySettingRepository(AskLucyDbContext dbContext) : IAiCapabilitySettingRepository
{
    public async Task<IReadOnlyList<AiCapabilitySetting>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.AiCapabilitySettings.OrderBy(s => s.Capability).ThenBy(s => s.Key).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AiCapabilitySetting>> ListByCapabilityAsync(AiCapability capability, CancellationToken cancellationToken = default) =>
        await dbContext.AiCapabilitySettings.Where(s => s.Capability == capability).ToListAsync(cancellationToken);

    public async Task<AiCapabilitySetting?> GetAsync(AiCapability capability, string key, CancellationToken cancellationToken = default) =>
        await dbContext.AiCapabilitySettings.FirstOrDefaultAsync(s => s.Capability == capability && s.Key == key, cancellationToken);

    public void Add(AiCapabilitySetting setting) => dbContext.AiCapabilitySettings.Add(setting);
}

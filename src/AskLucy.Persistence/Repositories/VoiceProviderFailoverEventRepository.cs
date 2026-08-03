using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class VoiceProviderFailoverEventRepository(AskLucyDbContext dbContext) : IVoiceProviderFailoverEventRepository
{
    public void Add(VoiceProviderFailoverEvent failoverEvent) => dbContext.VoiceProviderFailoverEvents.Add(failoverEvent);

    public async Task<IReadOnlyList<VoiceProviderFailoverEvent>> GetEventsAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
        await dbContext.VoiceProviderFailoverEvents
            .Where(e => e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc <= toUtc)
            .OrderBy(e => e.OccurredAtUtc)
            .ToListAsync(cancellationToken);

    public Task<VoiceProviderFailoverEvent?> GetMostRecentForUserAsync(string userId, CancellationToken cancellationToken = default) =>
        dbContext.VoiceProviderFailoverEvents
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.OccurredAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
}

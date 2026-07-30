using AskLucy.Application.Abstractions;
using AskLucy.Domain.Consent;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class UserCookieConsentRepository(AskLucyDbContext dbContext) : IUserCookieConsentRepository
{
    public Task<CookieConsentRecord?> GetLatestAsync(string userId, CancellationToken cancellationToken = default) =>
        dbContext.CookieConsentRecords
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CookieConsentRecord>> GetHistoryAsync(string userId, CancellationToken cancellationToken = default) =>
        await dbContext.CookieConsentRecords
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(CookieConsentRecord record, CancellationToken cancellationToken = default)
    {
        dbContext.CookieConsentRecords.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

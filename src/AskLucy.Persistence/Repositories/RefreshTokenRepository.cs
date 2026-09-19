using AskLucy.Application.Abstractions;
using AskLucy.Domain.Authentication;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class RefreshTokenRepository(AskLucyDbContext dbContext) : IRefreshTokenRepository
{
    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        dbContext.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> ListByFamilyAsync(Guid tokenFamilyId, CancellationToken cancellationToken = default) =>
        await dbContext.RefreshTokens
            .Where(t => t.TokenFamilyId == tokenFamilyId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> ListActiveByUserAsync(string userId, CancellationToken cancellationToken = default) =>
        await dbContext.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null && t.ExpiresAtUtc > DateTime.UtcNow)
            .ToListAsync(cancellationToken);

    public Task<bool> IsFamilyActiveAsync(Guid tokenFamilyId, CancellationToken cancellationToken = default) =>
        dbContext.RefreshTokens
            .AnyAsync(t => t.TokenFamilyId == tokenFamilyId && t.RevokedAtUtc == null && t.ExpiresAtUtc > DateTime.UtcNow, cancellationToken);

    public void Add(RefreshToken token) => dbContext.RefreshTokens.Add(token);
}

using AskLucy.Application.Abstractions;
using AskLucy.Domain.Authentication;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class PasswordResetTokenRepository(AskLucyDbContext dbContext) : IPasswordResetTokenRepository
{
    public Task<PasswordResetToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        dbContext.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public Task<int> CountIssuedSinceAsync(string userId, DateTime sinceUtc, CancellationToken cancellationToken = default) =>
        dbContext.PasswordResetTokens
            .CountAsync(t => t.UserId == userId && t.CreatedAtUtc >= sinceUtc, cancellationToken);

    public async Task SupersedePendingForUserAsync(string userId, Guid? exceptTokenId = null, CancellationToken cancellationToken = default)
    {
        // Loaded and mutated through the entity rather than ExecuteUpdateAsync so the tracked
        // instance a caller may already hold stays consistent with the row.
        var pending = await dbContext.PasswordResetTokens
            .Where(t => t.UserId == userId && t.ConsumedAtUtc == null && t.SupersededAtUtc == null && (exceptTokenId == null || t.Id != exceptTokenId))
            .ToListAsync(cancellationToken);

        foreach (var token in pending)
        {
            token.Supersede();
        }
    }

    // ExecuteDeleteAsync, not a load-then-remove: nothing here needs to be tracked, and the rows
    // are only ever touched by this sweep. Batched so a long-neglected table cannot turn one run
    // into a table-sized transaction.
    public Task<int> DeleteSpentBeforeAsync(DateTime createdBeforeUtc, int batchSize, CancellationToken cancellationToken = default) =>
        dbContext.PasswordResetTokens
            .Where(t => t.CreatedAtUtc < createdBeforeUtc
                && (t.ConsumedAtUtc != null || t.SupersededAtUtc != null || t.ExpiresAtUtc < DateTime.UtcNow))
            .OrderBy(t => t.CreatedAtUtc)
            .Take(batchSize)
            .ExecuteDeleteAsync(cancellationToken);

    public void Add(PasswordResetToken token) => dbContext.PasswordResetTokens.Add(token);
}

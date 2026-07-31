using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class UserAiPreferenceRepository(AskLucyDbContext dbContext) : IUserAiPreferenceRepository
{
    public Task<UserAiPreference?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default) =>
        dbContext.UserAiPreferences.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

    public void Add(UserAiPreference preference) => dbContext.UserAiPreferences.Add(preference);
}

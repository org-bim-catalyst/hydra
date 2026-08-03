using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class UserVoicePreferenceRepository(AskLucyDbContext dbContext) : IUserVoicePreferenceRepository
{
    public Task<UserVoicePreference?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default) =>
        dbContext.UserVoicePreferences.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

    public void Add(UserVoicePreference preference) => dbContext.UserVoicePreferences.Add(preference);
}

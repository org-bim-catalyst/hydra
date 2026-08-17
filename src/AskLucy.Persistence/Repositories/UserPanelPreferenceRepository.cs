using AskLucy.Application.Abstractions;
using AskLucy.Domain.Panels;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class UserPanelPreferenceRepository(AskLucyDbContext dbContext) : IUserPanelPreferenceRepository
{
    public Task<UserPanelPreference?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default) =>
        dbContext.UserPanelPreferences.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

    public void Add(UserPanelPreference preference) => dbContext.UserPanelPreferences.Add(preference);
}

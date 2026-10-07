using AskLucy.Application.Localization;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class UserLanguageStore(AskLucyDbContext dbContext) : IUserLanguageStore
{
    public Task<string?> GetPreferredLanguageAsync(string userId, CancellationToken cancellationToken) =>
        dbContext.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.PreferredLanguage).SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> SetPreferredLanguageAsync(string userId, string language, CancellationToken cancellationToken) =>
        // A single statement: the choice is one column on the account, and nothing else about the user changes. Soft-deleted
        // accounts are excluded by the user query filter.
        await dbContext.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.PreferredLanguage, language), cancellationToken) > 0;
}

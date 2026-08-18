using AskLucy.Application.Abstractions;
using AskLucy.Domain.Memory;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class MemoryPreferenceRepository(AskLucyDbContext dbContext) : IMemoryPreferenceRepository
{
    public Task<MemoryPreference?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default) =>
        dbContext.MemoryPreferences.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

    public void Add(MemoryPreference preference) => dbContext.MemoryPreferences.Add(preference);

    public async Task<IReadOnlyList<MemoryCategoryPreference>> GetCategoryPreferencesAsync(string userId, CancellationToken cancellationToken = default) =>
        await dbContext.MemoryCategoryPreferences.Where(p => p.UserId == userId).ToListAsync(cancellationToken);

    public Task<MemoryCategoryPreference?> GetCategoryPreferenceAsync(string userId, MemoryCategory category, CancellationToken cancellationToken = default) =>
        dbContext.MemoryCategoryPreferences.FirstOrDefaultAsync(p => p.UserId == userId && p.Category == category, cancellationToken);

    public void AddCategoryPreference(MemoryCategoryPreference preference) => dbContext.MemoryCategoryPreferences.Add(preference);
}

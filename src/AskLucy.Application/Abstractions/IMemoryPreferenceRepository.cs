using AskLucy.Domain.Memory;

namespace AskLucy.Application.Abstractions;

/// <summary>Repository for <see cref="MemoryPreference"/> and its child <see cref="MemoryCategoryPreference"/> rows — both are lazily created with defaults on first access (data-model.md), never bulk-seeded.</summary>
public interface IMemoryPreferenceRepository
{
    Task<MemoryPreference?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    void Add(MemoryPreference preference);

    Task<IReadOnlyList<MemoryCategoryPreference>> GetCategoryPreferencesAsync(string userId, CancellationToken cancellationToken = default);

    Task<MemoryCategoryPreference?> GetCategoryPreferenceAsync(string userId, MemoryCategory category, CancellationToken cancellationToken = default);

    void AddCategoryPreference(MemoryCategoryPreference preference);
}

using AskLucy.Application.Abstractions;
using AskLucy.Domain.Memory;
using MediatR;

namespace AskLucy.Application.Memory.Queries.GetMemoryPreferences;

/// <summary>Rows are materialized with defaults on first access, not bulk-seeded at account creation (data-model.md's <c>MemoryCategoryPreference</c> doc comment) — this query is one such access point, alongside <c>UpdateMemoryPreferencesCommandHandler</c>.</summary>
public sealed class GetMemoryPreferencesQueryHandler(
    IMemoryPreferenceRepository preferenceRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetMemoryPreferencesQuery, MemoryPreferencesDto>
{
    private static readonly MemoryCategory[] AllCategories =
    [
        MemoryCategory.UserPreference, MemoryCategory.PersonalFact, MemoryCategory.ProjectContext, MemoryCategory.ConversationDerived,
    ];

    public async Task<MemoryPreferencesDto> Handle(GetMemoryPreferencesQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var preference = await preferenceRepository.GetByUserIdAsync(userId, cancellationToken);
        var madeChanges = false;

        if (preference is null)
        {
            preference = MemoryPreference.CreateDefault(userId, userId);
            preferenceRepository.Add(preference);
            madeChanges = true;
        }

        var existingCategoryPreferences = await preferenceRepository.GetCategoryPreferencesAsync(userId, cancellationToken);
        var byCategory = existingCategoryPreferences.ToDictionary(p => p.Category);

        foreach (var category in AllCategories)
        {
            if (byCategory.ContainsKey(category))
            {
                continue;
            }

            var created = MemoryCategoryPreference.CreateDefault(userId, category, userId);
            preferenceRepository.AddCategoryPreference(created);
            byCategory[category] = created;
            madeChanges = true;
        }

        if (madeChanges)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var categories = AllCategories
            .Select(c => byCategory[c])
            .Select(p => new MemoryCategoryPreferenceDto(p.Category.ToString(), p.ApprovalMode.ToString(), p.IsEnabled))
            .ToList();

        return new MemoryPreferencesDto(preference.MemoryEnabled, categories);
    }
}

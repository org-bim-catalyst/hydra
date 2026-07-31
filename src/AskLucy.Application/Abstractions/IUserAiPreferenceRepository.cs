using AskLucy.Domain.Ai;

namespace AskLucy.Application.Abstractions;

/// <summary>Aggregate-oriented repository for <see cref="UserAiPreference"/> (constitution &#167;3 Repository rules).</summary>
public interface IUserAiPreferenceRepository
{
    Task<UserAiPreference?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    void Add(UserAiPreference preference);
}

using AskLucy.Domain.Panels;

namespace AskLucy.Application.Abstractions;

/// <summary>Aggregate-oriented repository for <see cref="UserPanelPreference"/> (constitution §3 Repository rules).</summary>
public interface IUserPanelPreferenceRepository
{
    Task<UserPanelPreference?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    void Add(UserPanelPreference preference);
}

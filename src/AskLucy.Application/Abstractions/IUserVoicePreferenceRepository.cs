using AskLucy.Domain.Ai;

namespace AskLucy.Application.Abstractions;

/// <summary>Aggregate-oriented repository for <see cref="UserVoicePreference"/> (constitution §3 Repository rules).</summary>
public interface IUserVoicePreferenceRepository
{
    Task<UserVoicePreference?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    void Add(UserVoicePreference preference);
}

using AskLucy.Application.Users;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// Keeps Application from depending on the Persistence-owned <c>ApplicationUser</c> type
/// (same rationale as <see cref="IIdentityService"/>).
/// </summary>
public interface IUserProfileRepository
{
    Task<UserProfileDto?> GetByIdAsync(string userId, CancellationToken cancellationToken = default);

    Task UpdateAsync(string userId, string? firstName, string? lastName, CancellationToken cancellationToken = default);

    Task SetAvatarFileNameAsync(string userId, string? avatarFileName, CancellationToken cancellationToken = default);
}

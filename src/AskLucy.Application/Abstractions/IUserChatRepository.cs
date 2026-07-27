using AskLucy.Domain.Chats;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// Aggregate-oriented repository for <see cref="UserChat"/> (constitution &#167;3 Repository rules).
/// </summary>
public interface IUserChatRepository
{
    Task<UserChat?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserChat>> ListByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    void Add(UserChat chat);
}

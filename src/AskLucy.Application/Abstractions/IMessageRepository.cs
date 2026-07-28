using AskLucy.Domain.Chats;

namespace AskLucy.Application.Abstractions;

/// <summary>Aggregate-oriented repository for <see cref="Message"/> (constitution &#167;3 Repository rules).</summary>
public interface IMessageRepository
{
    Task<IReadOnlyList<Message>> ListByChatIdAsync(Guid userChatId, CancellationToken cancellationToken = default);

    void Add(Message message);
}

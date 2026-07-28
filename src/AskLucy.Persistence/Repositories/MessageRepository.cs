using AskLucy.Application.Abstractions;
using AskLucy.Domain.Chats;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class MessageRepository(AskLucyDbContext dbContext) : IMessageRepository
{
    public async Task<IReadOnlyList<Message>> ListByChatIdAsync(Guid userChatId, CancellationToken cancellationToken = default) =>
        await dbContext.Messages
            .Where(m => m.UserChatId == userChatId)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public void Add(Message message) => dbContext.Messages.Add(message);
}

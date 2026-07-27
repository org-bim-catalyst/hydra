using AskLucy.Application.Abstractions;
using AskLucy.Domain.Chats;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class UserChatRepository(AskLucyDbContext dbContext) : IUserChatRepository
{
    public Task<UserChat?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.UserChats.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<UserChat>> ListByUserIdAsync(string userId, CancellationToken cancellationToken = default) =>
        await dbContext.UserChats
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public void Add(UserChat chat) => dbContext.UserChats.Add(chat);
}

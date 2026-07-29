using AskLucy.Application.Abstractions;
using AskLucy.Domain.Chats;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class MessageRepository(AskLucyDbContext dbContext) : IMessageRepository
{
    public async Task<IReadOnlyList<Message>> ListByChatIdAsync(Guid userChatId, CancellationToken cancellationToken = default) =>
        await dbContext.Messages
            .Include(m => m.Attachments)
            .Include(m => m.Citations)
            .Where(m => m.UserChatId == userChatId)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<Message> Items, string? NextCursor)> ListPagedByChatIdAsync(
        Guid userChatId, string? cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Messages
            .Include(m => m.Attachments)
            .Include(m => m.Citations)
            .Where(m => m.UserChatId == userChatId);

        if (ConversationCursor.Decode(cursor) is { } c)
        {
            var cursorCreatedAtUtc = ConversationCursor.DecodeDateTime(c.SortValue);
            var cursorId = c.Id;
            query = query.Where(m =>
                m.CreatedAtUtc > cursorCreatedAtUtc || (m.CreatedAtUtc == cursorCreatedAtUtc && m.Id > cursorId));
        }

        var page = await query
            .OrderBy(m => m.CreatedAtUtc)
            .ThenBy(m => m.Id)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasNextPage = page.Count > pageSize;
        var items = hasNextPage ? page.GetRange(0, pageSize) : page;

        string? nextCursor = null;
        if (hasNextPage && items.Count > 0)
        {
            var last = items[^1];
            nextCursor = ConversationCursor.Encode(0, ConversationCursor.EncodeDateTime(last.CreatedAtUtc), last.Id);
        }

        return (items, nextCursor);
    }

    public void Add(Message message) => dbContext.Messages.Add(message);

    public async Task DeleteAllByChatIdAsync(Guid userChatId, CancellationToken cancellationToken = default) =>
        await dbContext.Messages.Where(m => m.UserChatId == userChatId).ExecuteDeleteAsync(cancellationToken);
}

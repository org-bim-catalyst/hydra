using AskLucy.Domain.Chats;

namespace AskLucy.Application.Abstractions;

/// <summary>A message without its content — what a metadata-only reader may see (specs/074 research D15).</summary>
public sealed record MessageOutline(Guid Id, MessageRole Role, DateTime CreatedAtUtc);

/// <summary>Aggregate-oriented repository for <see cref="Message"/> (constitution &#167;3 Repository rules).</summary>
public interface IMessageRepository
{
    Task<IReadOnlyList<Message>> ListByChatIdAsync(Guid userChatId, CancellationToken cancellationToken = default);

    /// <summary>The conversation's messages oldest-first, as ids, roles and timestamps only — no content is read.</summary>
    Task<IReadOnlyList<MessageOutline>> ListOutlineByChatIdAsync(Guid userChatId, CancellationToken cancellationToken = default);

    /// <summary>Cursor-paginated message history for a single conversation (FR-022/FR-024, research.md Topic 6) — ordered oldest-first, matching the existing full-history load.</summary>
    Task<(IReadOnlyList<Message> Items, string? NextCursor)> ListPagedByChatIdAsync(
        Guid userChatId, string? cursor, int pageSize, CancellationToken cancellationToken = default);

    void Add(Message message);

    /// <summary>Permanently removes every message (and, via FK cascade, their attachments/citations) for a conversation — used by Clear Messages (FR-011). A bulk operation, not a soft delete: cleared history has no undo path per the spec.</summary>
    Task DeleteAllByChatIdAsync(Guid userChatId, CancellationToken cancellationToken = default);
}

using AskLucy.Domain.Chats;

namespace AskLucy.Application.Chats;

/// <summary>A conversation as listed in the sidebar/search results (FR-012/FR-020) — deliberately lighter than a full message-history load.</summary>
public sealed record UserChatSummaryDto(
    Guid Id,
    string Title,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc,
    bool IsArchived,
    bool IsPinned,
    bool IsFavorite,
    bool IsDeleted)
{
    public static UserChatSummaryDto FromEntity(UserChat chat) => new(
        chat.Id,
        chat.Title,
        chat.CreatedAtUtc,
        chat.ModifiedAtUtc,
        IsArchived: chat.ArchivedAtUtc is not null,
        IsPinned: chat.PinnedAtUtc is not null,
        chat.IsFavorite,
        IsDeleted: chat.DeletedAtUtc is not null);
}

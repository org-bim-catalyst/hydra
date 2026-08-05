using AskLucy.Domain.Documents;

namespace AskLucy.Application.Abstractions;

/// <summary>Repository for <see cref="DocumentNotification"/> (constitution §3 Repository rules).</summary>
public interface IDocumentNotificationRepository
{
    void Add(DocumentNotification notification);

    Task<DocumentNotification?> GetByIdAsync(Guid id, string userId, CancellationToken cancellationToken = default);

    /// <summary>Newest-first, optionally unread-only, cursor-paginated (contracts/document-processing-api.md — constitution §6).</summary>
    Task<(IReadOnlyList<DocumentNotification> Items, string? NextCursor)> ListForUserAsync(
        string userId, bool unreadOnly, string? cursor, int pageSize, CancellationToken cancellationToken = default);
}

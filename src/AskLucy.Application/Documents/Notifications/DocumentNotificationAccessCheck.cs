using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;

namespace AskLucy.Application.Documents.Notifications;

/// <summary>T091 — the <c>Document</c> related-item type's <see cref="INotificationAccessCheck"/> (research R24). Reuses <see cref="Document.IsOwnedBy"/>, the same predicate <c>DocumentOwnershipGuard</c> checks, without that guard's throw-as-404 behavior — an inaccessible item here just means "hide it," not "fail the request."</summary>
public sealed class DocumentNotificationAccessCheck(IDocumentRepository documentRepository) : INotificationAccessCheck
{
    public string ItemType => "Document";

    public async Task<bool> CanAccessAsync(string userId, string itemId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(itemId, out var documentId))
        {
            return false;
        }

        var document = await documentRepository.GetByIdAsync(documentId, cancellationToken);
        return document is not null && document.IsOwnedBy(userId);
    }

    public async Task<IReadOnlySet<string>> GetAvailableAsync(string userId, IReadOnlyCollection<string> itemIds, CancellationToken cancellationToken)
    {
        var available = new HashSet<string>(StringComparer.Ordinal);
        foreach (var itemId in itemIds)
        {
            if (await CanAccessAsync(userId, itemId, cancellationToken))
            {
                available.Add(itemId);
            }
        }

        return available;
    }
}

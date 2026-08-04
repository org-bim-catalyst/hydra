using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Authorization;
using AskLucy.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.KnowledgeBases.Commands.DeleteFolder;

/// <summary>Mirrors <c>ClearUserChatMessagesCommandHandlerLog</c> — a business-event log for an irreversible-feeling cascade (constitution §14), only emitted when the cascade actually removed something (an empty-folder delete isn't a data-loss event worth logging).</summary>
internal static partial class DeleteFolderCommandHandlerLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Folder {FolderId} deleted with {DocumentCount} document(s) and {SubfolderCount} subfolder(s) cascaded in knowledge base {KnowledgeBaseId} by {UserId}")]
    public static partial void FolderCascadeDeleted(ILogger logger, Guid folderId, int documentCount, int subfolderCount, Guid knowledgeBaseId, string userId);
}

/// <summary>
/// Deleting a non-empty folder (confirmed) cascades: every descendant subfolder and every
/// document anywhere in that subtree is also soft-deleted, and the owning knowledge base's
/// cached statistics are decremented per document — leaving no folder/document dangling with
/// a reference to a now-invisible parent (FR-015's "explains what will happen to that content"
/// is this cascade; the confirmation dialog's copy states it, see contracts).
/// </summary>
public sealed class DeleteFolderCommandHandler(
    IKnowledgeBaseRepository knowledgeBaseRepository,
    IKnowledgeBaseFolderRepository folderRepository,
    IKnowledgeBaseDocumentRepository documentRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    ILogger<DeleteFolderCommandHandler> logger) : IRequestHandler<DeleteFolderCommand>
{
    public async Task Handle(DeleteFolderCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var knowledgeBase = KnowledgeBaseOwnershipGuard.EnsureOwnedBy(
            await knowledgeBaseRepository.GetByIdAsync(request.KnowledgeBaseId, cancellationToken), userId);
        var folder = KnowledgeBaseFolderGuard.EnsureBelongsTo(
            await folderRepository.GetByIdAsync(request.FolderId, cancellationToken), request.KnowledgeBaseId);

        var allFolders = await folderRepository.ListByKnowledgeBaseIdAsync(request.KnowledgeBaseId, cancellationToken);
        var subtreeFolderIds = CollectSubtree(folder.Id, allFolders);

        var hasContents = subtreeFolderIds.Count > 1
            || await folderRepository.HasContentsAsync(folder.Id, cancellationToken);

        if (!request.Confirm && hasContents)
        {
            throw new DomainRuleViolationException(
                "This folder still contains subfolders or documents. Confirm to permanently remove them from the folder tree along with it.");
        }

        var documentsInSubtree = new List<Domain.KnowledgeBases.KnowledgeBaseDocument>();
        foreach (var folderId in subtreeFolderIds)
        {
            documentsInSubtree.AddRange(await documentRepository.ListByFolderAsync(request.KnowledgeBaseId, folderId, cancellationToken));
        }

        foreach (var document in documentsInSubtree)
        {
            document.SoftDelete(userId);
            knowledgeBase.ApplyDocumentRemoved(document.PageCount, document.SizeBytes, userId);
        }

        foreach (var folderInSubtree in allFolders.Where(f => subtreeFolderIds.Contains(f.Id)))
        {
            folderInSubtree.SoftDelete(userId);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (documentsInSubtree.Count > 0 || subtreeFolderIds.Count > 1)
        {
            DeleteFolderCommandHandlerLog.FolderCascadeDeleted(
                logger, folder.Id, documentsInSubtree.Count, subtreeFolderIds.Count - 1, knowledgeBase.Id, userId);
        }
    }

    private static HashSet<Guid> CollectSubtree(Guid rootFolderId, IReadOnlyList<Domain.KnowledgeBases.KnowledgeBaseFolder> allFolders)
    {
        var subtree = new HashSet<Guid> { rootFolderId };
        var frontier = new Queue<Guid>([rootFolderId]);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var child in allFolders.Where(f => f.ParentFolderId == current))
            {
                if (subtree.Add(child.Id))
                {
                    frontier.Enqueue(child.Id);
                }
            }
        }

        return subtree;
    }
}

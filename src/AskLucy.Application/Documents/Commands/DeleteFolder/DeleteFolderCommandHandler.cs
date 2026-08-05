using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using AskLucy.Domain.Common;
using MediatR;

namespace AskLucy.Application.Documents.Commands.DeleteFolder;

/// <summary>
/// FR-033, Edge Cases — a non-empty folder requires an explicit <see cref="OnContainedDocumentsAction"/>
/// choice; omitting it is a <see cref="DomainRuleViolationException"/> (400), never a silent
/// default, per the Edge Case's "explicit, non-silent handling" requirement.
/// </summary>
public sealed class DeleteFolderCommandHandler(
    IDocumentFolderRepository folderRepository,
    IDocumentRepository documentRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<DeleteFolderCommand>
{
    public async Task Handle(DeleteFolderCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var folder = DocumentFolderOwnershipGuard.EnsureOwnedBy(
            await folderRepository.GetByIdAsync(request.FolderId, cancellationToken), userId);

        var containedDocuments = await documentRepository.ListByFolderIdAsync(folder.Id, cancellationToken);

        if (containedDocuments.Count > 0 && request.OnContainedDocuments is null)
        {
            throw new DomainRuleViolationException(
                "This folder contains documents — specify onContainedDocuments (MoveToParent, ArchiveAll, or DeleteAll) to proceed.");
        }

        foreach (var document in containedDocuments)
        {
            switch (request.OnContainedDocuments)
            {
                case OnContainedDocumentsAction.MoveToParent:
                    document.MoveToFolder(folder.ParentFolderId, userId);
                    break;
                case OnContainedDocumentsAction.ArchiveAll:
                    document.Archive(userId);
                    break;
                case OnContainedDocumentsAction.DeleteAll:
                    document.SoftDelete(userId);
                    break;
            }
        }

        folderRepository.Remove(folder);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

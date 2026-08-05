using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using MediatR;

namespace AskLucy.Application.Documents.Commands.MoveDocument;

public sealed class MoveDocumentCommandHandler(
    IDocumentRepository documentRepository,
    IDocumentFolderRepository folderRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<MoveDocumentCommand, DocumentSummaryDto>
{
    public async Task<DocumentSummaryDto> Handle(MoveDocumentCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var document = DocumentOwnershipGuard.EnsureOwnedBy(
            await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken), userId);

        if (request.FolderId is { } folderId)
        {
            DocumentFolderOwnershipGuard.EnsureOwnedBy(await folderRepository.GetByIdAsync(folderId, cancellationToken), userId);
        }

        document.MoveToFolder(request.FolderId, userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return DocumentSummaryDto.FromEntity(document);
    }
}

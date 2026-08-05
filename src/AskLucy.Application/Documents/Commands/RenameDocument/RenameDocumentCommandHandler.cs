using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using MediatR;

namespace AskLucy.Application.Documents.Commands.RenameDocument;

public sealed class RenameDocumentCommandHandler(
    IDocumentRepository documentRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<RenameDocumentCommand, DocumentSummaryDto>
{
    public async Task<DocumentSummaryDto> Handle(RenameDocumentCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var document = DocumentOwnershipGuard.EnsureOwnedBy(
            await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken), userId);

        document.Rename(request.FileName, userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return DocumentSummaryDto.FromEntity(document);
    }
}

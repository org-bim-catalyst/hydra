using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using MediatR;

namespace AskLucy.Application.Documents.Commands.ArchiveDocument;

public sealed class ArchiveDocumentCommandHandler(
    IDocumentRepository documentRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<ArchiveDocumentCommand>
{
    public async Task Handle(ArchiveDocumentCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var document = DocumentOwnershipGuard.EnsureOwnedBy(
            await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken), userId);

        document.Archive(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

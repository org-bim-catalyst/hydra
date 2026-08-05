using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using MediatR;

namespace AskLucy.Application.Documents.Commands.RestoreDocument;

public sealed class RestoreDocumentCommandHandler(
    IDocumentRepository documentRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<RestoreDocumentCommand>
{
    public async Task Handle(RestoreDocumentCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var document = DocumentOwnershipGuard.EnsureOwnedBy(
            await documentRepository.GetByIdIncludingDeletedAsync(request.DocumentId, cancellationToken), userId);

        document.Undelete(userId);
        document.Restore(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

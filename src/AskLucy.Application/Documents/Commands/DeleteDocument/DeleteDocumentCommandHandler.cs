using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using MediatR;

namespace AskLucy.Application.Documents.Commands.DeleteDocument;

/// <summary>FR-017 — recoverable soft delete, never an immediate, irreversible removal.</summary>
public sealed class DeleteDocumentCommandHandler(
    IDocumentRepository documentRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<DeleteDocumentCommand>
{
    public async Task Handle(DeleteDocumentCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var document = DocumentOwnershipGuard.EnsureOwnedBy(
            await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken), userId);

        document.SoftDelete(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
